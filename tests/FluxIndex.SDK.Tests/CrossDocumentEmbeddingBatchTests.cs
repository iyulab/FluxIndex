using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.SDK;
using FluxIndex.Storage.SQLite;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// A batch embeds across documents: a corpus of one-chunk documents costs one embedding call per group of up to 64
/// chunks, not one call per document (measured on MIRACL-ko: 8,704 one-passage documents made 8,704 calls whatever the
/// batch size). A document the embedder refuses still fails alone.
/// </summary>
public sealed class CrossDocumentEmbeddingBatchTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly List<string> _paths = [];

    private FluxIndexContext NewContext(IEmbeddingService embedder)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluxindex-xdoc-{Guid.NewGuid():N}.db");
        _paths.Add(path);
        return (FluxIndexContext)FluxIndexContext.CreateBuilder()
            .UseSQLite(path)
            .UseEmbeddingService(embedder)
            .AddSQLiteStorage()
            .SuppressStartupMessages()
            .Build();
    }

    private static List<Document> OneChunkDocuments(int count, Func<int, string>? content = null) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var text = content?.Invoke(i) ?? $"passage number {i} about printers";
            var document = Document.Create($"d{i:D3}");
            document.Content = text;
            document.Chunks = [DocumentChunk.Create(document.Id, text, 0, 1)];
            return document;
        }).ToList();

    [Fact]
    public async Task OneChunkDocuments_AreEmbeddedTogether()
    {
        var embedder = new CountingEmbeddingService();
        await using var context = NewContext(embedder);

        var ids = (await context.Indexer.IndexBatchAsync(OneChunkDocuments(100), parallelism: 4, cancellationToken: Ct)).ToList();

        ids.Should().HaveCount(100);
        embedder.BatchSizes.Should().BeEquivalentTo([64, 36], "100 one-chunk documents fill one call of 64 and one of 36 (groups run concurrently)");
        embedder.SingleCalls.Should().Be(0);
        (await context.Indexer.GetStatisticsAsync(Ct)).TotalChunks.Should().Be(100);
    }

    [Fact]
    public async Task ARefusedText_FailsOnlyItsDocument()
    {
        var embedder = new CountingEmbeddingService(refuse: "refuse");
        await using var context = NewContext(embedder);
        var documents = OneChunkDocuments(10, i => i == 4 ? "refuse this passage" : $"passage {i} about scanners");

        var act = () => context.Indexer.IndexBatchAsync(documents, parallelism: 2, cancellationToken: Ct);

        var ex = (await act.Should().ThrowAsync<BatchIndexingException>()).Which;
        ex.Failures.Keys.Should().Equal("d004");
        ex.IndexedIds.Should().HaveCount(9);
        (await context.Indexer.GetStatisticsAsync(Ct)).TotalChunks.Should().Be(9);
    }

    private sealed class CountingEmbeddingService(string? refuse = null) : IEmbeddingService
    {
        private readonly object _lock = new();
        public List<int> BatchSizes { get; } = [];
        public int SingleCalls { get; private set; }

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            lock (_lock) SingleCalls++;
            return Embed(text);
        }

        public async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            var list = texts.ToList();
            lock (_lock) BatchSizes.Add(list.Count);
            var vectors = new List<float[]>();
            foreach (var text in list)
                vectors.Add(await Embed(text));
            return vectors;
        }

        private Task<float[]> Embed(string text) =>
            refuse is not null && text.Contains(refuse, StringComparison.Ordinal)
                ? throw new InvalidOperationException("the embedding server refused the text")
                : Task.FromResult(new[] { text.Length, 1f, 0f });

        public int GetEmbeddingDimension() => 3;
        public string GetModelName() => "counting-test-embedder";
        public EmbeddingIdentity GetIdentity() => new() { Provider = "Test", Model = "counting-test-embedder", Dimension = 3 };
    }

    public void Dispose()
    {
        FluxIndex.Tests.Shared.SqliteTestPools.Release(_paths);
        foreach (var path in _paths.SelectMany(p => new[] { p, p + "-wal", p + "-shm" }))
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
