using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
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
/// <para>
/// Once every owned table exists, provisioning also adds columns the current model declares that the
/// database does not have — but only columns that can be added without inventing data for existing
/// rows: nullable ones, or ones with a default. A missing column that is neither is reported as a
/// partial schema, the same way a missing table is. Columns the database has that the model does
/// not, and columns whose type differs from the model, are left alone: they are not evidence of a
/// wrong schema and this provisioner does not rewrite tables.
/// </para>
/// </remarks>
internal static class SQLiteSchemaProvisioner
{
    /// <summary>
    /// Ensure the database exists and that every table <paramref name="context"/> owns is present,
    /// with every column the model can add in place. Unrelated tables are left untouched. Safe to run
    /// repeatedly.
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
            AddMissingColumns(context);
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
    /// A column the model declares, and whether it can be added to an existing table without
    /// inventing a value for the rows already there.
    /// </summary>
    internal readonly record struct ModelColumn(string Name, bool CanAddInPlace);

    /// <summary>
    /// Decide which of a table's model columns to add and which to refuse, given the columns the
    /// database already has. Kept separate from the database round-trip so the decision is
    /// unit-testable.
    /// </summary>
    internal static (IReadOnlyList<string> Add, IReadOnlyList<string> Refuse) PlanColumns(
        IEnumerable<ModelColumn> modelColumns,
        IReadOnlySet<string> existingColumns)
    {
        var add = new List<string>();
        var refuse = new List<string>();

        foreach (var column in modelColumns)
        {
            if (existingColumns.Contains(column.Name))
            {
                continue;
            }

            (column.CanAddInPlace ? add : refuse).Add(column.Name);
        }

        return (add, refuse);
    }

    private static void AddMissingColumns(DbContext context)
    {
        var operations = new List<MigrationOperation>();
        var refused = new List<string>();

        // The runtime model is read-optimised and drops column facets (defaults, computed SQL,
        // collation) that the ADD COLUMN needs; the design-time model keeps them.
        var model = context.GetService<IDesignTimeModel>().Model;

        foreach (var table in model.GetRelationalModel().Tables)
        {
            var existing = GetExistingColumns(context, table.Name);
            var (add, refuse) = PlanColumns(
                table.Columns.Select(c => new ModelColumn(c.Name, CanAddInPlace(c))),
                existing);

            refused.AddRange(refuse.Select(name => $"{table.Name}.{name}"));
            operations.AddRange(table.Columns
                .Where(c => add.Contains(c.Name, StringComparer.Ordinal))
                .Select(c => ToAddColumn(table, c)));
        }

        if (refused.Count > 0)
        {
            throw new InvalidOperationException(
                $"FluxIndex SQLite schema for {context.GetType().Name} is partially present in this " +
                $"database: the current model declares column(s) {string.Join(", ", refused)} that the " +
                "database lacks, and they cannot be added automatically because they are required " +
                "with no default (existing rows would need a value). Add the column(s) to match the " +
                "model, or turn auto-migration off for this component and manage the schema externally.");
        }

        if (operations.Count == 0)
        {
            return;
        }

        // DDL single-sourced from the model: the provider's own migrations SQL generator renders the
        // ADD COLUMN, so the type and nullability match what CreateTables would have produced.
        var generator = context.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(operations, model))
        {
            ExecuteNonQuery(context, command.CommandText);
        }
    }

    private static bool CanAddInPlace(IColumn column) =>
        column.IsNullable || column.DefaultValue is not null || column.DefaultValueSql is not null;

    private static AddColumnOperation ToAddColumn(ITable table, IColumn column) => new()
    {
        Schema = table.Schema,
        Table = table.Name,
        Name = column.Name,
        ClrType = column.PropertyMappings[0].Property.ClrType,
        ColumnType = column.StoreType,
        IsNullable = column.IsNullable,
        DefaultValue = column.DefaultValue,
        DefaultValueSql = column.DefaultValueSql,
        ComputedColumnSql = column.ComputedColumnSql,
        IsStored = column.IsStored,
        Collation = column.Collation,
        Comment = column.Comment
    };

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
        return ReadStrings(context, "SELECT name FROM sqlite_master WHERE type = 'table'", ordinal: 0);
    }

    private static HashSet<string> GetExistingColumns(DbContext context, string table)
    {
        // PRAGMA takes no parameters; quote the identifier by doubling embedded quotes.
        var quoted = table.Replace("\"", "\"\"", StringComparison.Ordinal);
        return ReadStrings(context, $"PRAGMA table_info(\"{quoted}\")", ordinal: 1);
    }

    private static HashSet<string> ReadStrings(DbContext context, string sql, int ordinal)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            // Enlist in an ambient EF transaction if one is open — ADO.NET refuses a command on a
            // connection with a pending local transaction unless the transaction is set.
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                values.Add(reader.GetString(ordinal));
            }
        }
        finally
        {
            if (openedHere)
            {
                connection.Close();
            }
        }

        return values;
    }

    private static void ExecuteNonQuery(DbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.ExecuteNonQuery();
        }
        finally
        {
            if (openedHere)
            {
                connection.Close();
            }
        }
    }
}
