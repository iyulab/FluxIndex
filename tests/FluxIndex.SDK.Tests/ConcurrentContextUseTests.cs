using AwesomeAssertions;
using FluxIndex.Storage.SQLite;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// One context built once and shared by concurrent callers — the hosting shape a DI singleton gives
/// (HTTP requests plus a background indexer). Overlapping delete / index / search calls must all
/// succeed; before 0.63.0 the context held one EF Core <c>DbContext</c> for its lifetime and an
/// overlapping call failed with "A second operation was started on this context instance".
/// </summary>
public class ConcurrentContextUseTests : IDisposable
{
    private const int Callers = 16;

    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"fluxindex_concurrent_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task OverlappingDeleteIndexSearch_OnOneContext_AllSucceed()
    {
        var context = FluxIndexContext.CreateBuilder()
            .UseSQLite(_dbPath)
            .UseInMemoryEmbedding()
            .AddSQLiteStorage()
            .Build();

        try
        {
            var ct = TestContext.Current.CancellationToken;
            using var start = new ManualResetEventSlim(false);

            var callers = Enumerable.Range(0, Callers).Select(i => Task.Run(async () =>
            {
                start.Wait(ct);
                var id = $"doc-{i}";
                await context.DeleteDocumentAsync(id, ct);
                await context.Indexer.IndexDocumentAsync($"Concurrent caller {i} writes about shared storage", id, cancellationToken: ct);
                await context.SearchAsync("shared storage", cancellationToken: ct);
            }, ct)).ToArray();

            start.Set();
            await Task.WhenAll(callers);

            (await context.GetDocumentCountAsync(ct)).Should().Be(Callers,
                "every caller's document must have been written despite the overlap");
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(path); } catch (IOException) { }
        }
        GC.SuppressFinalize(this);
    }
}
