using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// Creates the tables an EF context owns, without disturbing anything else in the database file.
/// </summary>
/// <remarks>
/// FluxIndex puts several components in one SQLite file — <c>UseSQLite(path)</c> points the vector
/// store, the graph store and the semantic cache at it. EF's <c>EnsureCreated()</c> skips schema
/// creation entirely once the database holds ANY table, so whichever component was provisioned first
/// silently suppressed the rest (and a database shared with the consumer's own tables suppressed all
/// of them). Provisioning per owned table is what makes the components independent.
/// </remarks>
internal static class SQLiteSchemaProvisioner
{
    /// <summary>
    /// Ensure the database exists and that every table <paramref name="context"/> owns is present.
    /// Unrelated tables are left untouched. Safe to run repeatedly.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The context's schema is partially present, which automatic provisioning will not repair.
    /// </exception>
    public static void Provision(DbContext context)
    {
        var creator = context.Database.GetService<IRelationalDatabaseCreator>();

        if (!creator.Exists())
        {
            creator.Create();
        }

        var owned = GetOwnedTables(context);
        var existing = GetExistingTables(context);
        var missing = owned.Where(table => !existing.Contains(table)).ToArray();

        if (missing.Length == 0)
        {
            return;
        }

        if (missing.Length < owned.Length)
        {
            var present = string.Join(", ", owned.Where(existing.Contains));
            throw new InvalidOperationException(
                $"FluxIndex SQLite schema for {context.GetType().Name} is partially present in this " +
                $"database (present: {present}; missing: {string.Join(", ", missing)}). Automatic " +
                "migration will not repair a partial schema because it cannot know whether the existing " +
                "tables match the current model. Either drop the tables listed as present so start-up " +
                "can recreate the schema, create the missing tables to match the model, or turn " +
                "auto-migration off for this component and manage the schema externally.");
        }

        creator.CreateTables();
    }

    /// <summary>
    /// Table names owned by the context, taken from the EF model so the DDL stays single-sourced.
    /// </summary>
    private static string[] GetOwnedTables(DbContext context)
    {
        return context.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(table => !string.IsNullOrEmpty(table))
            .Select(table => table!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static HashSet<string> GetExistingTables(DbContext context)
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
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                existing.Add(reader.GetString(0));
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
