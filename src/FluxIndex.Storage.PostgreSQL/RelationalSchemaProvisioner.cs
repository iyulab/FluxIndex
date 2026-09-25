using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
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
/// <para>
/// Once every owned relation exists, provisioning also adds columns the current model declares that
/// the database does not have — but only columns that can be added without inventing data for
/// existing rows: nullable ones, or ones with a default. A missing column that is neither is reported
/// as a partial schema, the same way a missing relation is. Columns the database has that the model
/// does not, and columns whose type differs from the model, are left alone: they are not evidence of
/// a wrong schema and this provisioner does not rewrite tables.
/// </para>
/// </remarks>
internal static class RelationalSchemaProvisioner
{
    /// <summary>
    /// Ensure the database exists and that every relation <paramref name="context"/> owns is present,
    /// with every column the model can add in place. Unrelated relations are left untouched. Safe to
    /// run repeatedly.
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
                AddMissingColumns(context);
                WidenBoundedColumns(context);
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
    /// A column the model declares, and whether it can be added to an existing relation without
    /// inventing a value for the rows already there.
    /// </summary>
    internal readonly record struct ModelColumn(string Name, bool CanAddInPlace);

    /// <summary>
    /// Decide which of a relation's model columns to add and which to refuse, given the columns the
    /// database already has. Kept separate from the database round-trip so the decision is
    /// unit-testable without a server.
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
            var schema = table.Schema ?? "public";
            var existing = GetExistingColumns(context, schema, table.Name);
            var (add, refuse) = PlanColumns(
                table.Columns.Select(c => new ModelColumn(c.Name, CanAddInPlace(c))),
                existing);

            refused.AddRange(refuse.Select(name => $"{schema}.{table.Name}.{name}"));
            operations.AddRange(table.Columns
                .Where(c => add.Contains(c.Name, StringComparer.Ordinal))
                .Select(c => ToAddColumn(table, c)));
        }

        if (refused.Count > 0)
        {
            throw new InvalidOperationException(
                $"FluxIndex PostgreSQL schema for {context.GetType().Name} is partially present in " +
                $"this database: the current model declares column(s) {string.Join(", ", refused)} that " +
                "the database lacks, and they cannot be added automatically because they are required " +
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

    /// <summary>
    /// A model column's declared character bound: <c>null</c> when the model leaves it unbounded (<c>text</c>).
    /// </summary>
    internal readonly record struct BoundedColumn(string Name, int? ModelMaxLength);

    /// <summary>
    /// Decide which existing columns are narrower than the model now declares and must be widened. Only widening is
    /// planned: narrowing can fail on, or truncate, rows already stored, so it is a migration and never done here.
    /// Kept separate from the database round-trip so the decision is unit-testable without a server.
    /// </summary>
    internal static IReadOnlyList<string> PlanWidenings(
        IEnumerable<BoundedColumn> modelColumns,
        IReadOnlyDictionary<string, int?> existingMaxLengths)
    {
        var widen = new List<string>();
        foreach (var column in modelColumns)
        {
            if (!existingMaxLengths.TryGetValue(column.Name, out var existing) || existing is not { } existingLength)
            {
                continue;
            }

            if (column.ModelMaxLength is not { } modelLength || modelLength > existingLength)
            {
                widen.Add(column.Name);
            }
        }

        return widen;
    }

    /// <summary>
    /// Widens character columns an earlier version created narrower than the current model — for example
    /// <c>vectors."DocumentId"</c>, <c>varchar(50)</c> until 0.52.0, where a longer document id failed inside the
    /// store with <c>22001</c>. In PostgreSQL, <c>varchar(n)</c> to <c>text</c> or to a larger <c>varchar</c> is a
    /// catalog-only change: no table rewrite, and indexes stay valid.
    /// </summary>
    private static void WidenBoundedColumns(DbContext context)
    {
        var model = context.GetService<IDesignTimeModel>().Model;

        foreach (var table in model.GetRelationalModel().Tables)
        {
            var schema = table.Schema ?? "public";
            var existing = GetExistingColumnMaxLengths(context, schema, table.Name);
            var widen = PlanWidenings(
                table.Columns.Select(c => new BoundedColumn(c.Name, c.MaxLength)),
                existing);

            foreach (var column in table.Columns.Where(c => widen.Contains(c.Name, StringComparer.Ordinal)))
            {
                ExecuteNonQuery(context,
                    $"ALTER TABLE \"{schema}\".\"{table.Name}\" ALTER COLUMN \"{column.Name}\" TYPE {column.StoreType}");
            }
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

        WithOpenConnection(context, connection =>
        {
            foreach (var relation in ownedRelations)
            {
                using var command = connection.CreateCommand();
                // ::text is required — Npgsql has no reader mapping for the raw regclass OID type.
                command.CommandText = "SELECT to_regclass(@relation)::text";
                command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
                AddParameter(command, "relation", relation);

                var result = command.ExecuteScalar();

                if (result is not null and not DBNull)
                {
                    existing.Add(relation);
                }
            }
        });

        return existing;
    }

    private static HashSet<string> GetExistingColumns(DbContext context, string schema, string table)
    {
        var existing = new HashSet<string>(StringComparer.Ordinal);

        WithOpenConnection(context, connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT column_name FROM information_schema.columns " +
                "WHERE table_schema = @schema AND table_name = @table";
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            AddParameter(command, "schema", schema);
            AddParameter(command, "table", table);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                existing.Add(reader.GetString(0));
            }
        });

        return existing;
    }

    private static Dictionary<string, int?> GetExistingColumnMaxLengths(DbContext context, string schema, string table)
    {
        var lengths = new Dictionary<string, int?>(StringComparer.Ordinal);

        WithOpenConnection(context, connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT column_name, character_maximum_length FROM information_schema.columns " +
                "WHERE table_schema = @schema AND table_name = @table";
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            AddParameter(command, "schema", schema);
            AddParameter(command, "table", table);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                lengths[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetInt32(1);
            }
        });

        return lengths;
    }

    private static void ExecuteNonQuery(DbContext context, string sql)
    {
        WithOpenConnection(context, connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.ExecuteNonQuery();
        });
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// Run <paramref name="action"/> on the context's connection, opening it only if it is not
    /// already open and closing it again in that case. Commands enlist in an ambient EF transaction
    /// if one is open — ADO.NET refuses a command on a connection with a pending local transaction
    /// unless the transaction is set.
    /// </summary>
    private static void WithOpenConnection(DbContext context, Action<DbConnection> action)
    {
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            connection.Open();
        }

        try
        {
            action(connection);
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
