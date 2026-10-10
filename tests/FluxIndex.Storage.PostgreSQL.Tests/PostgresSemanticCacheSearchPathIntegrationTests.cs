using System.Reflection;
using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// The semantic cache opted into with <c>SemanticCacheOptions.Provider = "PostgreSQL"</c> is the one
/// <see cref="FluxIndexContext.SearchAsync"/> consults. Before 0.83.0 it was registered under a second interface nothing
/// on the search path read, so every search reached the store. Selecting the store alone does not turn caching on. The
/// retriever's own result cache is off and the two queries differ, so only the semantic cache can answer.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgresSemanticCacheSearchPathIntegrationTests : IAsyncLifetime
{
    // The vector store's table is created at the default dimension; the test vectors match it.
    private const int Dimensions = FluxIndex.Core.Constants.EmbeddingDefaults.DefaultVectorDimension;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task PostgreSQLPreset_ASimilarRepeatedQuery_IsAnsweredWithoutTheStore()
    {
        var counter = new SearchCounter();
        using var context = (FluxIndexContext)Build(counter);
        await context.Indexer.IndexDocumentAsync("FluxIndex is a retrieval library", "doc-a", cancellationToken: Ct);

        var first = (await context.SearchAsync("what is fluxindex", cancellationToken: Ct)).ToList();
        first.Should().ContainSingle().Which.DocumentId.Should().Be("doc-a");
        counter.Searches.Should().Be(1);

        var second = (await context.SearchAsync("what's fluxindex", cancellationToken: Ct)).ToList();

        counter.Searches.Should().Be(1, "the similar query is answered from the semantic cache, not the store");
        second.Select(r => (r.Id, r.DocumentId, r.Content)).Should().Equal(first.Select(r => (r.Id, r.DocumentId, r.Content)));

        await context.Indexer.IndexDocumentAsync("FluxIndex also indexes keywords", "doc-b", cancellationToken: Ct);
        (await context.SearchAsync("what is fluxindex", cancellationToken: Ct)).Select(r => r.DocumentId).Should().Contain("doc-b");
        counter.Searches.Should().Be(2, "a write empties the semantic cache");
    }

    [Fact]
    public async Task UsePostgreSQLAlone_RegistersNoSemanticCache_AndASimilarQueryReachesTheStore()
    {
        var counter = new SearchCounter();
        using var context = (FluxIndexContext)Build(counter, optIn: false);
        context.ServiceProvider.GetService<ISemanticCacheService>().Should().BeNull("selecting the store does not opt into caching");
        await context.Indexer.IndexDocumentAsync("FluxIndex is a retrieval library", "doc-a", cancellationToken: Ct);

        await context.SearchAsync("what is fluxindex", cancellationToken: Ct);
        await context.SearchAsync("what's fluxindex", cancellationToken: Ct);

        counter.Searches.Should().Be(2, "without the opt-in each search reaches the store");
    }

    [Fact]
    public async Task ARowItCannotRead_IsAMissAndIsRemoved()
    {
        using var context = (FluxIndexContext)Build(new SearchCounter());
        var cache = context.ServiceProvider.GetRequiredService<ISemanticCacheService>();

        // The shape an earlier release wrote for the retired ISemanticCache: a JSONB list of arbitrary values.
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO semantic_cache ("Id", "QueryHash", "Query", "Embedding", "Results", "ExpiresAt")
                VALUES ('legacy', 'legacy-hash', 'what is fluxindex', @embedding::vector, '["an answer"]', now() + interval '1 hour')
                """, connection);
            command.Parameters.AddWithValue("embedding", ConstantEmbeddingService.Literal);
            await command.ExecuteNonQueryAsync(Ct);
        }

        (await cache.GetCachedResultAsync("what is fluxindex", cancellationToken: Ct)).Should().BeNull();
        (await cache.GetCacheStatisticsAsync(Ct)).TotalEntries.Should().Be(0, "an unreadable row is removed, not served or kept");
    }

    private IFluxIndexContext Build(SearchCounter counter, bool optIn = true)
    {
        var builder = FluxIndexContext.CreateBuilder()
            .UsePostgreSQL(_container.GetConnectionString())
            .AddPostgreSQLStorage()
            .UseEmbeddingService(new ConstantEmbeddingService())
            .WithSemanticCacheOptions(o =>
            {
                o.Provider = optIn ? "PostgreSQL" : "None";
                o.EmbeddingDimensions = Dimensions;
            })
            .WithSearchOptions(defaultMaxResults: 10, defaultMinScore: 0f)
            .SuppressStartupMessages()
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
        private static readonly float[] Vector = [1f, .. new float[Dimensions - 1]];

        public static string Literal => "[" + string.Join(",", Vector.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]";

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
