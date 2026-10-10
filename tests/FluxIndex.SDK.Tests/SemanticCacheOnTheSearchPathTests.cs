using System.Reflection;
using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// The semantic cache opted into with <c>SemanticCacheOptions.Provider = "SQLite"</c> is the one
/// <see cref="FluxIndexContext.SearchAsync"/> consults. Before 0.83.0 the SQLite and PostgreSQL caches were registered
/// under a second interface nothing on the search path read, so every search reached the store. Selecting the store
/// alone does not turn caching on. The retriever's own result cache is switched off here so that only the semantic cache
/// can answer, and the two queries differ so that only a similarity match can.
/// </summary>
public class SemanticCacheOnTheSearchPathTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Asked = "what is fluxindex";
    private const string AskedAgain = "what's fluxindex";

    [Fact]
    public async Task UseSQLiteAlone_RegistersNoSemanticCache_AndASimilarQueryReachesTheStore()
    {
        var counter = new SearchCounter();
        using var context = (FluxIndexContext)Build(counter, optIn: false);
        context.ServiceProvider.GetService<ISemanticCacheService>().Should().BeNull("selecting the store does not opt into caching");
        await context.Indexer.IndexDocumentAsync("FluxIndex is a retrieval library", "doc-a", cancellationToken: Ct);

        await context.SearchAsync(Asked, cancellationToken: Ct);
        await context.SearchAsync(AskedAgain, cancellationToken: Ct);

        counter.Searches.Should().Be(2, "without the opt-in each search reaches the store");
    }

    [Fact]
    public void OptingInWithoutTheStoragePackage_BuildNamesTheMissingRegistration()
    {
        var builder = FluxIndexContext.CreateBuilder()
            .UseEmbeddingService(new ConstantEmbeddingService())
            .WithSemanticCacheOptions(o => o.Provider = "SQLite")
            .SuppressStartupMessages();

        var build = () => builder.Build();

        build.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().ContainAll("SemanticCacheOptions.Provider", "SQLite", "AddSQLiteStorage");
    }

    [Fact]
    public async Task WarmupCacheAsync_StoresSearchResults_SoTheFirstRealQueryIsAHit()
    {
        // Warm-up handed the queries to the cache, which has no search: SQLite/PostgreSQL stored nothing and Redis only
        // embeddings, while the context reported true. It now searches each query, so the results are cached.
        var counter = new SearchCounter();
        using var context = (FluxIndexContext)Build(counter);
        await context.Indexer.IndexDocumentAsync("FluxIndex is a retrieval library", "doc-a", cancellationToken: Ct);

        (await context.WarmupCacheAsync([Asked, " ", Asked], cancellationToken: Ct)).Should().BeTrue();
        counter.Searches.Should().Be(1, "one distinct non-blank query was warmed");

        var answered = (await context.SearchAsync(AskedAgain, cancellationToken: Ct)).ToList();

        answered.Should().ContainSingle().Which.DocumentId.Should().Be("doc-a");
        counter.Searches.Should().Be(1, "the real query is answered from what the warm-up stored");
    }

    [Fact]
    public async Task WarmupCacheAsync_WithoutASemanticCache_ReportsFalseAndSearchesNothing()
    {
        var counter = new SearchCounter();
        using var context = (FluxIndexContext)Build(counter, optIn: false);

        (await context.WarmupCacheAsync([Asked], cancellationToken: Ct)).Should().BeFalse();
        counter.Searches.Should().Be(0);
    }

    [Fact]
    public async Task SQLitePreset_ASimilarRepeatedQuery_IsAnsweredWithoutTheStore()
    {
        var counter = new SearchCounter();
        using var context = (FluxIndexContext)Build(counter);
        await context.Indexer.IndexDocumentAsync("FluxIndex is a retrieval library", "doc-a", cancellationToken: Ct);

        var first = (await context.SearchAsync(Asked, cancellationToken: Ct)).ToList();
        first.Should().ContainSingle().Which.DocumentId.Should().Be("doc-a");
        counter.Searches.Should().Be(1);

        var second = (await context.SearchAsync(AskedAgain, cancellationToken: Ct)).ToList();

        counter.Searches.Should().Be(1, "the similar query is answered from the semantic cache, not the store");
        second.Select(r => (r.Id, r.DocumentId, r.Content, r.ChunkIndex))
            .Should().Equal(first.Select(r => (r.Id, r.DocumentId, r.Content, r.ChunkIndex)));
    }

    [Fact]
    public async Task SQLitePreset_ACachedHit_KeepsTheChunkMetadataAsPlainValues()
    {
        var counter = new SearchCounter();
        using var context = (FluxIndexContext)Build(counter);
        await context.Indexer.IndexDocumentAsync(
            "FluxIndex is a retrieval library", "doc-a",
            new Dictionary<string, object> { ["source"] = "guide.md", ["page"] = 3 }, Ct);

        var first = (await context.SearchAsync(Asked, cancellationToken: Ct)).Single();
        var second = (await context.SearchAsync(AskedAgain, cancellationToken: Ct)).Single();

        counter.Searches.Should().Be(1, "positive control: the second search is the cached one");
        first.Metadata.Should().ContainKey("source");
        second.Metadata["source"].Should().Be("guide.md", "a hit reads like a miss: a string stays a string");
        second.Metadata["page"].Should().Be(3L);
    }

    [Fact]
    public async Task SQLitePreset_AWriteThroughTheIndexer_IsNotAnsweredFromBeforeIt()
    {
        var counter = new SearchCounter();
        using var context = (FluxIndexContext)Build(counter);
        await context.Indexer.IndexDocumentAsync("FluxIndex is a retrieval library", "doc-a", cancellationToken: Ct);
        (await context.SearchAsync(Asked, cancellationToken: Ct)).Select(r => r.DocumentId).Should().Equal("doc-a");

        await context.Indexer.IndexDocumentAsync("FluxIndex also indexes keywords", "doc-b", cancellationToken: Ct);

        var after = (await context.SearchAsync(Asked, cancellationToken: Ct)).Select(r => r.DocumentId).ToList();
        counter.Searches.Should().Be(2, "a write empties the semantic cache, like the retriever's own cache");
        after.Should().Contain("doc-b");
    }

    [Fact]
    public async Task SQLitePreset_AFilteredSearch_NeitherReadsNorFillsTheCache()
    {
        var counter = new SearchCounter();
        using var context = (FluxIndexContext)Build(counter);
        await context.Indexer.IndexDocumentAsync(
            "FluxIndex is a retrieval library", "doc-a", new Dictionary<string, object> { ["tenant"] = "a" }, Ct);
        await context.SearchAsync(Asked, cancellationToken: Ct);

        var filter = new Dictionary<string, object> { ["tenant"] = "b" };
        (await context.SearchAsync(AskedAgain, filter: filter, cancellationToken: Ct)).Should().BeEmpty(
            "the cached results were not filtered for tenant b");
        (await context.SearchAsync(AskedAgain, filter: filter, cancellationToken: Ct)).Should().BeEmpty();
        counter.Searches.Should().Be(3, "neither filtered search was answered from the cache");

        (await context.SearchAsync(AskedAgain, cancellationToken: Ct)).Select(r => r.DocumentId).Should().Equal("doc-a");
        counter.Searches.Should().Be(3, "the unfiltered entry is still the one cached: the filtered searches stored nothing over it");
    }

    [Fact]
    public async Task SQLitePreset_AHit_IsTrimmedToTheRequest_AndALargerRequestIsAMiss()
    {
        var counter = new SearchCounter();
        using var context = (FluxIndexContext)Build(counter);
        await context.Indexer.IndexDocumentAsync("FluxIndex is a retrieval library", "doc-a", cancellationToken: Ct);
        await context.Indexer.IndexDocumentAsync("FluxIndex also indexes keywords", "doc-b", cancellationToken: Ct);
        await context.Indexer.IndexDocumentAsync("FluxIndex ranks with fusion", "doc-c", cancellationToken: Ct);

        (await context.SearchAsync(Asked, maxResults: 2, cancellationToken: Ct)).Should().HaveCount(2);
        (await context.SearchAsync(AskedAgain, maxResults: 1, cancellationToken: Ct)).Should().HaveCount(1);
        counter.Searches.Should().Be(1, "a smaller request is served from the two cached results");

        (await context.SearchAsync(AskedAgain, maxResults: 5, cancellationToken: Ct)).Should().HaveCount(3,
            "the cached entry holds only the top two, so a request for five must reach the store");
        counter.Searches.Should().Be(2);
    }

    [Fact]
    public void SQLitePreset_WithAnotherSemanticCacheRegistered_BuildNamesTheConflict()
    {
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .AddSQLiteStorage()
            .UseEmbeddingService(new ConstantEmbeddingService())
            .WithSemanticCacheOptions(o => o.Provider = "SQLite")
            .SuppressStartupMessages()
            .ConfigureServices(s => s.AddSingleton<ISemanticCacheService>(NSubstitute.Substitute.For<ISemanticCacheService>()));

        var build = () => builder.Build();

        build.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().ContainAll("ISemanticCacheService", "SemanticCacheOptions.Provider", "SQLite");
    }

    private static IFluxIndexContext Build(SearchCounter counter, bool optIn = true)
    {
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .AddSQLiteStorage()
            .UseEmbeddingService(new ConstantEmbeddingService())
            .WithSemanticCacheOptions(o => o.Provider = optIn ? "SQLite" : "None")
            .WithSearchOptions(defaultMaxResults: 10, defaultMinScore: 0f)
            .SuppressStartupMessages()
            // After AddSQLiteStorage: storage registrations run in order during Build(), so this wraps the SQLite store.
            .RegisterStorageServices(services => counter.Wrap(services));
        builder.Options.Cache.EnableSearchCache = false;
        return builder.Build();
    }

    /// <summary>Counts the vector searches that reach the store, forwarding every call to the registered store.</summary>
    internal sealed class SearchCounter
    {
        private int _searches;

        public int Searches => Volatile.Read(ref _searches);

        public void Wrap(IServiceCollection services)
        {
            var inner = services.Last(d => d.ServiceType == typeof(IVectorStore));
            services.Remove(inner);
            services.Add(ServiceDescriptor.Describe(typeof(IVectorStore), sp =>
            {
                var store = (IVectorStore)(inner.ImplementationInstance
                    ?? inner.ImplementationFactory?.Invoke(sp)
                    ?? ActivatorUtilities.CreateInstance(sp, inner.ImplementationType!));
                var proxy = DispatchProxy.Create<IVectorStore, CountingProxy>();
                ((CountingProxy)(object)proxy).Configure(store, this);
                return proxy;
            }, inner.Lifetime));
        }

        internal void Count() => Interlocked.Increment(ref _searches);
    }

    public class CountingProxy : DispatchProxy
    {
        private object _target = null!;
        private SearchCounter _counter = null!;

        internal void Configure(object target, SearchCounter counter)
        {
            _target = target;
            _counter = counter;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IVectorStore.SearchAsync))
                _counter.Count();
            try
            {
                return targetMethod.Invoke(_target, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex.InnerException);
                throw;
            }
        }
    }

    /// <summary>Every text embeds to the same vector: every chunk matches every query, and similar queries are equal.</summary>
    private sealed class ConstantEmbeddingService : IEmbeddingService
    {
        private static readonly float[] Vector = [1f, 0f, 0f];

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(Vector.ToArray());
        public Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
            => Task.FromResult(texts.Select(_ => Vector.ToArray()));
        public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(text.Length / 4);
        public int GetEmbeddingDimension() => Vector.Length;
        public string GetModelName() => "test-constant";
        public int GetMaxTokens() => 512;
        public EmbeddingIdentity GetIdentity() => new() { Provider = "Test", Model = "test-constant", Dimension = Vector.Length };
    }
}
