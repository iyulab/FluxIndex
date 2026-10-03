using System.Collections.Concurrent;
using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Storage.SQLite;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// The embedding contract carries two roles: stored text goes through <see cref="IEmbeddingService.GenerateEmbeddingAsync"/>
/// and a search query through <see cref="IEmbeddingService.GenerateQueryEmbeddingAsync"/>, so an asymmetric model
/// (E5, Qwen3-Embedding, BGE) can apply its query convention. These run a real context (SQLite) with an embedder that
/// gives a query a different vector in each role: the query-role vector points at one document and the document-role
/// vector at the other, so which document comes back shows which role the search used.
/// </summary>
public class EmbeddingRoleContractTests : IDisposable
{
    private const string Query = "which one";

    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"fluxindex_roles_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Search_EmbedsTheQueryInTheQueryRole_AndIndexingInTheDocumentRole()
    {
        var ct = TestContext.Current.CancellationToken;
        var embedder = new RoleRecordingEmbeddingService();
        var context = FluxIndexContext.CreateBuilder()
            .UseSQLite(_dbPath)
            .UseEmbeddingService(embedder)
            .AddSQLiteStorage()
            .Build();

        try
        {
            await context.Indexer.IndexDocumentAsync(RoleRecordingEmbeddingService.QueryTarget, "query-target", cancellationToken: ct);
            await context.Indexer.IndexDocumentAsync(RoleRecordingEmbeddingService.DocumentTarget, "document-target", cancellationToken: ct);

            embedder.Calls.Should().NotBeEmpty();
            embedder.Calls.Should().OnlyContain(c => c.Role == "document", "indexing embeds stored text");
            embedder.Calls.Clear();

            var results = (await context.Retriever.SearchAsync(Query, maxResults: 1, minScore: 0f, cancellationToken: ct)).ToList();

            embedder.Calls.Should().ContainSingle(c => c.Text == Query).Which.Role.Should().Be("query");
            results.Should().ContainSingle().Which.DocumentChunk.DocumentId.Should().Be("query-target",
                "the vector search must run on the query-role vector — the document-role vector points at the other document");
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task HybridSearch_EmbedsTheQueryInTheQueryRole()
    {
        var ct = TestContext.Current.CancellationToken;
        var embedder = new RoleRecordingEmbeddingService();
        var context = FluxIndexContext.CreateBuilder()
            .UseSQLite(_dbPath)
            .UseEmbeddingService(embedder)
            .AddSQLiteStorage()
            .Build();

        try
        {
            await context.Indexer.IndexDocumentAsync(RoleRecordingEmbeddingService.QueryTarget, "query-target", cancellationToken: ct);
            await context.Indexer.IndexDocumentAsync(RoleRecordingEmbeddingService.DocumentTarget, "document-target", cancellationToken: ct);
            embedder.Calls.Clear();

            await context.Retriever.HybridSearchAsync(Query, Query, maxResults: 2, cancellationToken: ct);
            await context.HybridSearchV2Async(Query, cancellationToken: ct);

            embedder.Calls.Where(c => c.Text == Query).Should().NotBeEmpty()
                .And.OnlyContain(c => c.Role == "query", "both hybrid paths (Retriever and IHybridSearchService) embed their query for the vector leg");
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task QueryRole_DefaultsToTheDocumentRole_ForAServiceThatDoesNotOverrideIt()
    {
        IEmbeddingService symmetric = new SymmetricEmbeddingService();

        var query = await symmetric.GenerateQueryEmbeddingAsync("text", TestContext.Current.CancellationToken);
        var document = await symmetric.GenerateEmbeddingAsync("text", TestContext.Current.CancellationToken);

        query.Should().Equal(document, "a symmetric model embeds a query like stored text");
    }

    public void Dispose()
    {
        FluxIndex.Tests.Shared.SqliteTestPools.Release(_dbPath);
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    /// <summary>
    /// Stored text containing <see cref="QueryTarget"/> embeds along the first axis, anything else along the second.
    /// The query embeds along the first axis in the query role and along the second in the document role.
    /// </summary>
    private sealed class RoleRecordingEmbeddingService : IEmbeddingService
    {
        public const string QueryTarget = "alpha alpha alpha";
        public const string DocumentTarget = "bravo bravo bravo";

        public ConcurrentQueue<(string Role, string Text)> Calls { get; } = new();

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls.Enqueue(("document", text));
            return Task.FromResult(text.Contains("alpha", StringComparison.Ordinal) ? Axis(0) : Axis(1));
        }

        public async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            var vectors = new List<float[]>();
            foreach (var text in texts)
                vectors.Add(await GenerateEmbeddingAsync(text, cancellationToken));
            return vectors;
        }

        public Task<float[]> GenerateQueryEmbeddingAsync(string query, CancellationToken cancellationToken = default)
        {
            Calls.Enqueue(("query", query));
            return Task.FromResult(Axis(0));
        }

        public int GetEmbeddingDimension() => 4;
        public string GetModelName() => "test-roles";
        public int GetMaxTokens() => 512;
        public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(text.Length / 4);
        public EmbeddingIdentity GetIdentity() => new() { Provider = "Test", Model = "test-roles", Dimension = 4 };

        private static float[] Axis(int i)
        {
            var v = new float[4];
            v[i] = 1f;
            return v;
        }
    }

    private sealed class SymmetricEmbeddingService : IEmbeddingService
    {
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new[] { text.Length, 1f, 0f });
        public Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult(texts.Select(t => new[] { t.Length, 1f, 0f }));
        public int GetEmbeddingDimension() => 3;
        public string GetModelName() => "test-symmetric";
        public int GetMaxTokens() => 512;
        public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(1);
        public EmbeddingIdentity GetIdentity() => new() { Provider = "Test", Model = "test-symmetric", Dimension = 3 };
    }
}
