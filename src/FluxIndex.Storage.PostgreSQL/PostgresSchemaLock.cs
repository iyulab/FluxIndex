using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// Serializes FluxIndex schema creation within one database. Every statement is "if not exists", but PostgreSQL does not
/// make concurrent DDL on the same catalogue safe: two sessions creating the same relation, type or extension at once
/// fail with a unique violation on the catalogue (23505), a duplicate relation (42P07) or a deadlock (40P01). Several
/// processes or workers starting against a fresh database at the same moment hit exactly that, so each FluxIndex
/// component holds this session-level advisory lock around its DDL — the usual way migration tools serialize.
/// </summary>
/// <remarks>
/// <para>
/// The lock is held on a connection of its own, cloned from the one the DDL runs on (same database, same credentials):
/// an advisory lock is database-wide, so the session holding it need not be the one creating the schema — and EF's
/// <c>CreateTables()</c> opens and closes its connection itself, so the lock could not stay on that one.
/// </para>
/// <para>
/// One key for every FluxIndex component: the vector store, the keyword index, the graph and the cache all create
/// relations in the same catalogue, and a single key cannot deadlock against itself. Advisory locks are per database,
/// so stores in different databases do not wait on each other. The lock is held only while schema is created or
/// checked, never during normal reads and writes.
/// </para>
/// <para>
/// Not re-entrant: the lock is held by a separate session, so a nested <c>Run</c> would wait on its own caller forever.
/// Wrap each block of DDL on its own, never a caller that already takes it (<c>ProvisionTables</c>,
/// <c>EnsureVectorExtension</c>).
/// </para>
/// </remarks>
internal static class PostgresSchemaLock
{
    /// <summary>The advisory lock key (an arbitrary constant: the ASCII of "FluxIdx\0").</summary>
    internal const long Key = 0x466C7578_49647800;

    private static readonly string LockSql = FormattableString.Invariant($"SELECT pg_advisory_lock({Key})");
    private static readonly string UnlockSql = FormattableString.Invariant($"SELECT pg_advisory_unlock({Key})");

    /// <summary>Runs <paramref name="action"/> while holding the lock in the database <paramref name="connection"/> points at.</summary>
    public static void Run(DbConnection connection, Action action)
    {
        using var holder = Clone(connection);
        holder.Open();
        Execute(holder, LockSql);
        try
        {
            action();
        }
        finally
        {
            Execute(holder, UnlockSql);
        }
    }

    /// <summary>Runs <paramref name="action"/> while holding the lock in the database <paramref name="connection"/> points at.</summary>
    public static async Task RunAsync(DbConnection connection, Func<Task> action, CancellationToken cancellationToken)
    {
        await using var holder = Clone(connection);
        await holder.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(holder, LockSql, cancellationToken).ConfigureAwait(false);
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            // Not the caller's token: an unlock skipped on cancellation would hold the lock until the pooled
            // connection is reset.
            await ExecuteAsync(holder, UnlockSql, CancellationToken.None).ConfigureAwait(false);
        }
    }

    // NpgsqlConnection.Clone keeps the data source and its authentication, which a connection string read back from an
    // opened connection may no longer carry.
    private static NpgsqlConnection Clone(DbConnection connection) =>
        connection is NpgsqlConnection npgsql
            ? (NpgsqlConnection)((ICloneable)npgsql).Clone()
            : throw new ArgumentException($"Expected an {nameof(NpgsqlConnection)}, got {connection.GetType().Name}.", nameof(connection));

    private static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
