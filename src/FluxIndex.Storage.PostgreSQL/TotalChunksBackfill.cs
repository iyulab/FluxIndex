using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// Fills <c>TotalChunks</c> on rows written before the column existed. The column is nullable so that
/// <see cref="RelationalSchemaProvisioner"/> can add it to an existing database in place; without this step
/// every older row would read back as <c>0</c> — the value consumers cite as "chunk i of 0". The count of
/// rows sharing a <c>DocumentId</c> is what the indexer would have written, so that is what older rows get.
/// Idempotent: only <c>NULL</c> cells are touched.
/// </summary>
internal static class TotalChunksBackfill
{
    /// <summary>Backfills <paramref name="table"/> (a table this context owns, named in code — never user input) in place.</summary>
    public static void Run(DbContext context, string table)
    {
        context.Database.ExecuteSqlRaw(Sql(table));
    }

    private static string Sql(string table) =>
        $"UPDATE \"{table}\" SET \"TotalChunks\" = (SELECT COUNT(*) FROM \"{table}\" t2 WHERE t2.\"DocumentId\" = \"{table}\".\"DocumentId\") " +
        "WHERE \"TotalChunks\" IS NULL";
}
