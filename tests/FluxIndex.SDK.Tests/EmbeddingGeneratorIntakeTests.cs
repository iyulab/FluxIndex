using System.Collections.Concurrent;
using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// FluxIndex takes <c>Microsoft.Extensions.AI</c> embedding generators directly (#1292): the consumer registers its
/// generator(s) and writes no <see cref="IEmbeddingService"/>. The role is carried by the instance — a documents
/// generator for stored text, an optional queries generator for an asymmetric model's query side.
/// </summary>
public class EmbeddingGeneratorIntakeTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"fluxindex_meai_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Builder_IndexesWithTheDocumentsGenerator_AndSearchesWithTheQueriesGenerator()
    {
        var ct = TestContext.Current.CancellationToken;
        // Stored text containing "alpha" lies on axis 0, anything else on axis 1. The queries generator puts the query on
        // axis 0, the documents generator on axis 1 — so the document that comes back shows which generator embedded it.
        var documents = new RecordingGenerator("documents", text => text.Contains("alpha", StringComparison.Ordinal) ? 0 : 1);
        var queries = new RecordingGenerator("queries", _ => 0);
        var context = FluxIndexContext.CreateBuilder()
            .UseSQLite(_dbPath)
            .UseEmbeddingGenerator(documents, queries)
            .AddSQLiteStorage()
            .Build();

        try
        {
            await context.Indexer.IndexDocumentAsync("alpha alpha alpha", "query-target", cancellationToken: ct);
            await context.Indexer.IndexDocumentAsync("bravo bravo bravo", "document-target", cancellationToken: ct);
            queries.Inputs.Should().BeEmpty("indexing embeds stored text only");
            documents.Inputs.Should().NotBeEmpty();
            documents.Inputs.Clear();

            var results = (await context.Retriever.SearchAsync("which one", maxResults: 1, minScore: 0f, cancellationToken: ct)).ToList();

            queries.Inputs.Should().ContainSingle().Which.Should().Be("which one");
            documents.Inputs.Should().NotContain("which one");
            results.Should().ContainSingle().Which.DocumentChunk.DocumentId.Should().Be("query-target");
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task WithoutAQueriesGenerator_TheDocumentsGeneratorEmbedsQueries()
    {
        var documents = new RecordingGenerator("documents", _ => 0);
        IEmbeddingService service = new EmbeddingGeneratorEmbeddingService(documents);

        await service.GenerateQueryEmbeddingAsync("a question", TestContext.Current.CancellationToken);

        documents.Inputs.Should().Equal("a question");
    }

    [Fact]
    public void Identity_IsTheGeneratorsProviderModelAndDimension_PlusTheRevisionsFromOptions()
    {
        var service = new EmbeddingGeneratorEmbeddingService(
            new RecordingGenerator("documents", _ => 0),
            options: new EmbeddingGeneratorServiceOptions { Revision = "r2", VectorSpaceRevision = "vs1" });

        var identity = service.GetIdentity();

        identity.Provider.Should().Be("test-provider");
        identity.Model.Should().Be("test-model");
        identity.Dimension.Should().Be(RecordingGenerator.Dimensions);
        identity.Revision.Should().Be("r2");
        identity.VectorSpaceRevision.Should().Be("vs1");
    }

    [Fact]
    public async Task AGeneratorWithoutAModelId_IsRefusedWhenUsed_NotWhenConstructed()
    {
        var service = new EmbeddingGeneratorEmbeddingService(new RecordingGenerator("documents", _ => 0, modelId: null));

        service.Invoking(s => s.GetIdentity()).Should().Throw<InvalidOperationException>().WithMessage("*model id*");
        await service.Awaiting(s => s.GenerateEmbeddingAsync("text", TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*model id*");
    }

    [Fact]
    public void AnUnknownDimension_IsRefusedWhenUsed_AndAnOptionSuppliesIt()
    {
        var unknown = new EmbeddingGeneratorEmbeddingService(new RecordingGenerator("documents", _ => 0, reportDimensions: false));
        unknown.Invoking(s => s.GetEmbeddingDimension())
            .Should().Throw<InvalidOperationException>().WithMessage("*EmbeddingGeneratorServiceOptions.Dimensions*");

        var supplied = new EmbeddingGeneratorEmbeddingService(
            new RecordingGenerator("documents", _ => 0, reportDimensions: false),
            options: new EmbeddingGeneratorServiceOptions { Dimensions = RecordingGenerator.Dimensions });
        supplied.GetEmbeddingDimension().Should().Be(RecordingGenerator.Dimensions);
    }

    [Fact]
    public void ADimensionThatContradictsTheGenerator_IsRefused()
    {
        var service = new EmbeddingGeneratorEmbeddingService(
            new RecordingGenerator("documents", _ => 0),
            options: new EmbeddingGeneratorServiceOptions { Dimensions = RecordingGenerator.Dimensions + 1 });

        service.Invoking(s => s.GetIdentity()).Should().Throw<InvalidOperationException>().WithMessage("*reports 4*");
    }

    [Fact]
    public void ANonPositiveDimensionOption_IsRefusedAtConstruction()
    {
        var act = () => new EmbeddingGeneratorEmbeddingService(
            new RecordingGenerator("documents", _ => 0),
            options: new EmbeddingGeneratorServiceOptions { Dimensions = 0 });

        act.Should().Throw<ArgumentException>().WithMessage("*positive*");
    }

    [Fact]
    public void TheIdentity_IsReadFromTheGeneratorWhenAskedFor_SoAModelChosenLaterIsTheOneReported()
    {
        // A host whose model is picked at run time registers the generator before any model is set.
        var documents = new RecordingGenerator("documents", _ => 0, modelId: null);
        var service = new EmbeddingGeneratorEmbeddingService(documents);

        documents.ModelId = "picked-at-runtime";

        service.GetIdentity().Model.Should().Be("picked-at-runtime");
        service.GetModelName().Should().Be("picked-at-runtime");
    }

    [Fact]
    public async Task AContextBuiltBeforeAModelIsSet_WorksOnceItIsSet()
    {
        var ct = TestContext.Current.CancellationToken;
        var documents = new RecordingGenerator("documents", _ => 0, modelId: null);
        var context = FluxIndexContext.CreateBuilder()
            .UseSQLite(_dbPath)
            .UseEmbeddingGenerator(documents)
            .AddSQLiteStorage()
            .Build();

        try
        {
            documents.ModelId = "model-a";
            await context.Indexer.IndexDocumentAsync("alpha alpha alpha", "doc-a", cancellationToken: ct);
            (await context.Retriever.SearchAsync("alpha", maxResults: 1, minScore: 0f, cancellationToken: ct))
                .Should().ContainSingle();
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task TheRetriever_BindsTheStoreToTheIdentityAtEachUse_SoAStoreCanRefuseAModelSwitch()
    {
        // Binding at construction would hand the store the identity of the moment the retriever was built; the store's
        // mismatch check (EmbeddingModelMismatchException) needs the identity of the model that embeds this query.
        var ct = TestContext.Current.CancellationToken;
        var documents = new RecordingGenerator("documents", _ => 0, modelId: null);
        var store = Substitute.For<IVectorStore>();
        var retriever = new Retriever(
            store,
            Substitute.For<IDocumentRepository>(),
            new EmbeddingGeneratorEmbeddingService(documents),
            new RetrieverOptions());

        documents.ModelId = "model-a";
        await retriever.SearchAsync("first", maxResults: 1, minScore: 0f, cancellationToken: ct);
        documents.ModelId = "model-b";
        await retriever.SearchAsync("second", maxResults: 1, minScore: 0f, cancellationToken: ct);

        store.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IVectorStore.BindIdentity))
            .Select(c => ((FluxIndex.Core.Domain.ValueObjects.EmbeddingIdentity)c.GetArguments()[0]!).Model)
            .Should().Equal("model-a", "model-b");
    }

    [Fact]
    public async Task ABatch_IsOneGeneratorCall_AndBlankTextsGetEmptyVectorsInPlace()
    {
        var documents = new RecordingGenerator("documents", _ => 0);
        var service = new EmbeddingGeneratorEmbeddingService(documents);

        var vectors = (await service.GenerateEmbeddingsBatchAsync(["one", " ", "three"], TestContext.Current.CancellationToken)).ToList();

        documents.CallCount.Should().Be(1);
        documents.Inputs.Should().Equal("one", "three");
        vectors.Select(v => v.Length).Should().Equal(RecordingGenerator.Dimensions, 0, RecordingGenerator.Dimensions);
    }

    [Fact]
    public async Task AShortResultSet_Throws_RatherThanMisalignVectors()
    {
        var service = new EmbeddingGeneratorEmbeddingService(new RecordingGenerator("documents", _ => 0, dropLast: true));

        var act = () => service.GenerateEmbeddingsBatchAsync(["one", "two"], TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*positional*");
    }

    [Fact]
    public async Task AVectorOfAnotherSize_Throws()
    {
        var service = new EmbeddingGeneratorEmbeddingService(
            new RecordingGenerator("documents", _ => 0, reportDimensions: false),
            options: new EmbeddingGeneratorServiceOptions { Dimensions = 8 });

        var act = () => service.GenerateEmbeddingAsync("text", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*4-dimension*expects 8*");
    }

    [Fact]
    public async Task DependencyInjection_UsesTheContainersGenerator_AndTheKeyedQueriesGenerator()
    {
        var documents = new RecordingGenerator("documents", _ => 0);
        var queries = new RecordingGenerator("queries", _ => 1);
        var services = new ServiceCollection();
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(documents);
        services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>("query", queries);
        services.AddEmbeddingGeneratorEmbedding(queriesServiceKey: "query");
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IEmbeddingService>();

        await service.GenerateEmbeddingAsync("stored", TestContext.Current.CancellationToken);
        await service.GenerateQueryEmbeddingAsync("asked", TestContext.Current.CancellationToken);

        documents.Inputs.Should().Equal("stored");
        queries.Inputs.Should().Equal("asked");
    }

    public void Dispose()
    {
        FluxIndex.Tests.Shared.SqliteTestPools.Release(_dbPath);
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    /// <summary>A generator that puts each text on one axis of a 4-dimension space and records what it embedded.</summary>
    private sealed class RecordingGenerator(
        string name,
        Func<string, int> axis,
        string? modelId = "test-model",
        bool reportDimensions = true,
        bool dropLast = false) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public const int Dimensions = 4;

        public ConcurrentQueue<string> Inputs { get; } = new();
        public int CallCount;

        /// <summary>The model the generator reports; settable, as a host that picks the model at run time would.</summary>
        public string? ModelId { get; set; } = modelId;

        private EmbeddingGeneratorMetadata Metadata =>
            new("test-provider", providerUri: null, defaultModelId: ModelId, defaultModelDimensions: reportDimensions ? Dimensions : null);

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, Microsoft.Extensions.AI.EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            var list = values.ToList();
            foreach (var v in list)
                Inputs.Enqueue(v);
            var vectors = list.Select(v =>
            {
                var vector = new float[Dimensions];
                vector[axis(v)] = 1f;
                return new Embedding<float>(vector) { ModelId = name };
            }).ToList();
            if (dropLast && vectors.Count > 0)
                vectors.RemoveAt(vectors.Count - 1);
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(vectors));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType == typeof(EmbeddingGeneratorMetadata) ? Metadata
            : serviceKey is null && serviceType.IsInstanceOfType(this) ? this
            : null;

        public void Dispose() { }
    }
}
