using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// What a component must do with its schema, given which of its own relations already exist in the
/// target database.
/// </summary>
internal enum SchemaInitializationPlan
{
    /// <summary>Every owned relation is present — nothing to do.</summary>
    UpToDate,

    /// <summary>No owned relation is present — provision the whole schema.</summary>
    CreateAll,

    /// <summary>Some owned relations are present and some are missing — cannot be repaired safely.</summary>
    PartiallyPresent
}

/// <summary>
/// Creates the relations an EF context owns, without disturbing anything else in the database.
/// </summary>
/// <remarks>
/// Every FluxIndex PostgreSQL component provisions through this rather than <c>EnsureCreated()</c>.
/// EF's <c>EnsureCreated</c> short-circuits as soon as the database contains ANY relation, so a
/// database shared with the consumer's application tables — or simply shared with another FluxIndex
/// component that was provisioned first — silently got no schema at all, and the first write failed
/// with <c>42P01</c> while startup reported success.
/// </remarks>
internal static class RelationalSchemaProvisioner
{
    /// <summary>
    /// Ensure the database exists and that every relation <paramref name="context"/> owns is present.
    /// Unrelated relations are left untouched. Safe to run repeatedly.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The context's schema is partially present, which automatic provisioning will not repair.
    /// </exception>
    public static void Provision(DbContext context)
    {
        EnsureDatabase(context);
        ProvisionTables(context, context.Database.GetService<IRelationalDatabaseCreator>());
    }

    /// <summary>
    /// Create the database itself when absent. Callers that must connect before provisioning — to
    /// install an extension, say — run this first.
    /// </summary>
    public static void EnsureDatabase(DbContext context)
    {
        var creator = context.Database.GetService<IRelationalDatabaseCreator>();

        if (!creator.Exists())
        {
            creator.Create();
        }
    }

    /// <summary>
    /// Provision the owned relations, assuming the database itself exists. Use when the caller has
    /// already had to connect (for example to install an extension) before provisioning.
    /// </summary>
    public static void ProvisionTables(DbContext context)
    {
        ProvisionTables(context, context.Database.GetService<IRelationalDatabaseCreator>());
    }

    private static void ProvisionTables(DbContext context, IRelationalDatabaseCreator creator)
    {
        var owned = GetOwnedRelations(context);
        var existing = GetExistingRelations(context, owned);

        switch (Plan(owned, existing))
        {
            case SchemaInitializationPlan.UpToDate:
                return;

            case SchemaInitializationPlan.CreateAll:
                creator.CreateTables();
                return;

            default:
                var missing = string.Join(", ", owned.Where(relation => !existing.Contains(relation)));
                var present = string.Join(", ", owned.Where(existing.Contains));
                throw new InvalidOperationException(
                    $"FluxIndex PostgreSQL schema for {context.GetType().Name} is partially present in " +
                    $"this database (present: {present}; missing: {missing}). Automatic migration will " +
                    "not repair a partial schema because it cannot know whether the existing relations " +
                    "match the current model. Either drop the relations listed as present so startup " +
                    "can recreate the schema, create the missing relations to match the model, or turn " +
                    "auto-migration off for this component and manage the schema externally.");
        }
    }

    /// <summary>
    /// Decide what to do from the relations a context owns and the ones that already exist.
    /// Kept separate from the database round-trip so the decision is unit-testable without a server.
    /// </summary>
    internal static SchemaInitializationPlan Plan(
        IReadOnlyCollection<string> ownedRelations,
        IReadOnlySet<string> existingRelations)
    {
        if (ownedRelations.Count == 0)
        {
            return SchemaInitializationPlan.UpToDate;
        }

        var present = ownedRelations.Count(existingRelations.Contains);

        if (present == 0)
        {
            return SchemaInitializationPlan.CreateAll;
        }

        return present == ownedRelations.Count
            ? SchemaInitializationPlan.UpToDate
            : SchemaInitializationPlan.PartiallyPresent;
    }

    /// <summary>
    /// Schema-qualified relation names owned by the context, taken from the EF model so the DDL stays
    /// single-sourced (hand-written CREATE TABLE would restate column types and index definitions and
    /// drift from the model).
    /// </summary>
    private static string[] GetOwnedRelations(DbContext context)
    {
        return context.Model.GetEntityTypes()
            .Select(entityType => (schema: entityType.GetSchema() ?? "public", table: entityType.GetTableName()))
            .Where(relation => !string.IsNullOrEmpty(relation.table))
            .Select(relation => $"{relation.schema}.{relation.table}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static HashSet<string> GetExistingRelations(
        DbContext context,
        IReadOnlyCollection<string> ownedRelations)
    {
        var existing = new HashSet<string>(StringComparer.Ordinal);
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            connection.Open();
        }

        try
        {
            foreach (var relation in ownedRelations)
            {
                using var command = connection.CreateCommand();
                // ::text is required — Npgsql has no reader mapping for the raw regclass OID type.
                command.CommandText = "SELECT to_regclass(@relation)::text";

                var parameter = command.CreateParameter();
                parameter.ParameterName = "relation";
                parameter.Value = relation;
                command.Parameters.Add(parameter);

                var result = command.ExecuteScalar();

                if (result is not null and not DBNull)
                {
                    existing.Add(relation);
                }
            }
        }
        finally
        {
            if (openedHere)
            {
                connection.Close();
            }
        }

        return existing;
    }
}
