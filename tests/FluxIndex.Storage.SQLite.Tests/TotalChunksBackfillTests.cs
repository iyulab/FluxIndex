using FluxIndex.Core.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// A database written before <c>TotalChunks</c> existed has rows the column cannot describe. Provisioning
/// adds the (nullable) column in place, and the backfill gives those rows the per-document count the
/// indexer would have written — a silent <c>0</c> on every older row is exactly the defect this replaces.
/// </summary>
public sealed class TotalChunksBackfillTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    private SQLiteDbContext NewContext() => new(
        new DbContextOptionsBuilder<SQLiteDbContext>().UseSqlite(_connection).Options,
        Options.Create(new SQLiteOptions()));

    [Fact]
    public async Task OlderRows_GetThePerDocumentCount_NewRowsKeepWhatTheyWereStoredWith()
    {
        var ct = TestContext.Current.CancellationToken;
        _connection.Open();

        // The schema an earlier version left behind: derived from the current model minus the column,
        // with three rows for one document and one for another, written the old way.
        await using (var older = NewContext())
        {
            older.Database.EnsureCreated();
            await older.Database.ExecuteSqlRawAsync("ALTER TABLE \"vectors\" DROP COLUMN \"TotalChunks\"", ct);
            // Plain command: ExecuteSqlRaw would read the '{}' metadata literals as format placeholders.
            await using var insert = _connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO \"vectors\" (\"Id\", \"DocumentId\", \"ChunkIndex\", \"Content\", \"Embedding\", \"TokenCount\", \"Metadata\") VALUES " +
                "('a0','doc-a',0,'a zero',NULL,1,'{}'), ('a1','doc-a',1,'a one',NULL,1,'{}'), ('a2','doc-a',2,'a two',NULL,1,'{}'), " +
                "('b0','doc-b',0,'b zero',NULL,1,'{}')";
            await insert.ExecuteNonQueryAsync(ct);
        }

        await using var context = NewContext();
        var store = new SQLiteVectorStore(context, NullLogger<SQLiteVectorStore>.Instance, Options.Create(new SQLiteOptions()));

        // First use provisions (adds the column) and backfills.
        var docA = (await store.GetByDocumentIdAsync("doc-a", ct)).OrderBy(c => c.ChunkIndex).ToList();
        var docB = (await store.GetByDocumentIdAsync("doc-b", ct)).ToList();

        Assert.Equal(3, docA.Count);
        Assert.All(docA, c => Assert.Equal(3, c.TotalChunks));
        Assert.Equal(1, Assert.Single(docB).TotalChunks);

        // A row written after the column exists keeps its own value; the backfill only fills NULLs.
        await store.StoreAsync(new DocumentChunk { Id = "c0", DocumentId = "doc-c", ChunkIndex = 0, TotalChunks = 7, Content = "c", TokenCount = 1, Embedding = [1f, 0f] }, ct);
        await using var again = NewContext();
        TotalChunksBackfill.Run(again, "vectors");
        Assert.Equal(7, (await store.GetAsync("c0", ct))?.TotalChunks);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
