using Microsoft.Data.Sqlite;

namespace FluxIndex.Tests.Shared;

/// <summary>
/// Releases the pooled SQLite connections of a fixture's own database files so they can be deleted.
/// </summary>
/// <remarks>
/// Not <see cref="SqliteConnection.ClearAllPools"/>: that is process-global. Called from one fixture's cleanup while
/// another fixture, running in parallel, is opening a connection, it disposes the other fixture's native handle mid-use
/// — the intermittent <c>ObjectDisposedException: 'SQLitePCL.sqlite3'</c> in an unrelated test. A pool is keyed by its
/// connection string; the stores open <c>Data Source=&lt;file&gt;</c>, so each file's pool is cleared by name. Files a
/// store derives from the configured path (for example <c>&lt;name&gt;-entitygraph.db</c>) are found by the name's
/// stem.
/// </remarks>
internal static class SqliteTestPools
{
    public static void Release(params string[] databasePaths) => Release((IEnumerable<string>)databasePaths);

    public static void Release(IEnumerable<string> databasePaths)
    {
        foreach (var path in databasePaths)
        {
            foreach (var file in WithDerivedFiles(path))
            {
                SqliteConnection.ClearPool(new SqliteConnection($"Data Source={file}"));
            }
        }
    }

    private static IEnumerable<string> WithDerivedFiles(string path)
    {
        yield return path;

        var directory = Path.GetDirectoryName(path);
        var stem = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(stem) || !Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory, stem + "*.db"))
        {
            if (!string.Equals(file, path, StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }
}
