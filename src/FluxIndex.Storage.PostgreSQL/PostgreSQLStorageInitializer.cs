using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using FluxIndex.SDK;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// What the initializer must do with the vector schema, given which of its own relations already
/// exist in the target database.
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
/// PostgreSQL storage initializer. Creates the pgvector extension and the vector store schema on
/// Build(), symmetric with the SQLite initializer. Registered by
/// <see cref="FluxIndexContextBuilderExtensions.AddPostgreSQLStorage"/> when
/// <see cref="FluxIndex.SDK.Configuration.VectorStoreOptions.EnableAutoMigration"/> is true (the default).
/// </summary>
/// <remarks>
/// The schema is provisioned per owned relation rather than through <c>EnsureCreated()</c>. EF's
/// <c>EnsureCreated</c> short-circuits as soon as the database contains ANY relation, so pointing
/// FluxIndex at a database that already holds the consumer's application tables — the common
/// "share my app database" configuration — silently created nothing and failed with
/// <c>42P01: relation "vectors" does not exist</c> on the first index write, while Build() reported
/// success. Reported by All.Manual (2026-07-21).
/// </remarks>
internal sealed class PostgreSQLStorageInitializer : IStorageInitializer
{
    public void InitializeSync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FluxIndexDbContext>();
        var creator = context.Database.GetService<IRelationalDatabaseCreator>();

        if (!creator.Exists())
        {
            creator.Create();
        }

        // The pgvector extension must exist before the vector-typed column and HNSW index are built.
        // CREATE EXTENSION IF NOT EXISTS is a privilege-free no-op when the extension is already
        // installed (the managed-PostgreSQL case); it only needs CREATE privilege when the extension
        // is absent — the one scenario where a caller should opt out via EnableAutoMigration.
        context.Database.ExecuteSqlRaw("CREATE EXTENSION IF NOT EXISTS vector");

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
                    $"FluxIndex PostgreSQL schema is partially present in this database (present: {present}; " +
                    $"missing: {missing}). Automatic migration will not repair a partial schema because it " +
                    "cannot know whether the existing relations match the current model. Either drop the " +
                    "FluxIndex relations listed as present so Build() can recreate the schema, create the " +
                    "missing relations to match the model, or set VectorStoreOptions.EnableAutoMigration to " +
                    "false and manage the schema externally.");
        }
    }

    /// <summary>
    /// Decide what to do from the relations this context owns and the ones that already exist.
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
    /// Schema-qualified relation names owned by this context, taken from the EF model so the DDL
    /// stays single-sourced (a hand-written CREATE TABLE would duplicate the vector column type and
    /// the HNSW index definition and drift from the model).
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
