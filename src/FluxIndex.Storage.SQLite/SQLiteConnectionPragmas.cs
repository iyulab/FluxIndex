using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// The <see cref="SQLiteOptions"/> settings SQLite keeps per connection — <c>busy_timeout</c>, <c>cache_size</c>,
/// <c>mmap_size</c>, <c>temp_store</c>, <c>synchronous</c>, <c>wal_autocheckpoint</c>. A connection forgets them when it
/// closes and a new one starts from SQLite's defaults, so they are applied each time a connection opens; applying them
/// once at startup (as the schema migration did) left every later connection at the defaults.
/// </summary>
/// <remarks>
/// The settings SQLite stores in the database file itself (<c>journal_mode=WAL</c>, <c>page_size</c>, <c>auto_vacuum</c>)
/// are not here — they are applied once by the migration.
/// </remarks>
internal static class SQLiteConnectionPragmas
{
    /// <summary>The per-connection PRAGMAs of <paramref name="options"/> as one command text.</summary>
    public static string For(SQLiteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var invariant = CultureInfo.InvariantCulture;
        return string.Join(';',
            string.Create(invariant, $"PRAGMA busy_timeout={options.BusyTimeout}"),
            string.Create(invariant, $"PRAGMA cache_size={options.CacheSize}"),
            string.Create(invariant, $"PRAGMA mmap_size={Math.Max(0, options.MmapSize)}"),
            $"PRAGMA temp_store={options.TempStore.ToString().ToUpperInvariant()}",
            $"PRAGMA synchronous={options.Synchronous.ToString().ToUpperInvariant()}",
            string.Create(invariant, $"PRAGMA wal_autocheckpoint={options.WalAutocheckpoint}"));
    }

    /// <summary>Runs <paramref name="pragmas"/> on an open connection.</summary>
    public static async Task ApplyAsync(DbConnection connection, string pragmas, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="pragmas"/> on an open connection.</summary>
    public static void Apply(DbConnection connection, string pragmas)
    {
        using var command = connection.CreateCommand();
        command.CommandText = pragmas;
        command.ExecuteNonQuery();
    }
}

/// <summary>
/// Applies <see cref="SQLiteConnectionPragmas"/> to every connection an EF Core context of this library opens.
/// </summary>
internal sealed class SQLiteConnectionPragmaInterceptor(SQLiteOptions options) : DbConnectionInterceptor
{
    private readonly string _pragmas = SQLiteConnectionPragmas.For(options);

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        SQLiteConnectionPragmas.Apply(connection, _pragmas);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default) =>
        SQLiteConnectionPragmas.ApplyAsync(connection, _pragmas, cancellationToken);
}
