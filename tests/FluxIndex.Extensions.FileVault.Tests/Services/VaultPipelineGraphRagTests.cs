using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using FluxIndex.Extensions.FileVault.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace FluxIndex.Extensions.FileVault.Tests.Services;

/// <summary>
/// Verifies that the FileVault memorize path wires GraphRAG with semantics equivalent to the
/// SDK direct-index path (Indexer.IndexAsync): null = auto-when-registered, true = force,
/// false = off. Regression guard for upstream issue
/// ISSUE-FluxIndex-20260619-filevault-indexingoptions-exposure.
/// </summary>
public sealed class VaultPipelineGraphRagTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _vaultDir;
    private readonly IGitService _git;
    private readonly VaultStorageService _storage;
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;

    public VaultPipelineGraphRagTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"FileVaultGraphRag_{Guid.NewGuid():N}");
        _vaultDir = Path.Combine(_testDir, ".vault");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_vaultDir);

        _git = Substitute.For<IGitService>();
        _git.CommitAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("commit-hash");

        _storage = new VaultStorageService(
            NullLogger<VaultStorageService>.Instance,
            _git,
            MsOptions.Create(new FileVaultOptions { VaultBasePath = _vaultDir }));

        _vectorStore = Substitute.For<IVectorStore>();
        _vectorStore.StoreBatchAsync(Arg.Any<IEnumerable<DocumentChunk>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IEnumerable<string>>(
                ((IEnumerable<DocumentChunk>)ci[0]).Select(_ => Guid.NewGuid().ToString()).ToList()));

        _embeddingService = Substitute.For<IEmbeddingService>();
        _embeddingService.GenerateEmbeddingsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IEnumerable<float[]>>(
                ((IEnumerable<string>)ci[0]).Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToList()));
        _embeddingService.GetIdentity()
            .Returns(new EmbeddingIdentity { Provider = "Test", Model = "test", Dimension = 3 });
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch { /* ignore cleanup errors */ }
    }

    private VaultPipeline CreatePipeline(IGraphRAGService? graphRAG) =>
        new(
            _git,
            new ContentHasher(),
            _storage,
            NullLogger<VaultPipeline>.Instance,
            options: null,
            extractor: null,
            chunker: null,
            vectorStore: _vectorStore,
            embeddingService: _embeddingService,
            hybridSearch: null,
            graphRAGService: graphRAG);

    private static IGraphRAGService CreateGraphMock()
    {
        var graph = Substitute.For<IGraphRAGService>();
        graph.BuildIndexAsync(
                Arg.Any<IEnumerable<DocumentChunk>>(),
                Arg.Any<GraphRAGBuildOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GraphRAGIndex()));
        return graph;
    }

    private async Task<MemorizeResult> MemorizeFileAsync(VaultPipeline pipeline, MemorizeOptions options)
    {
        var docPath = Path.Combine(_testDir, "doc.txt");
        await File.WriteAllTextAsync(docPath, "Alice works at Acme Corp. Bob manages the project in Seoul.");
        var entry = VaultEntry.Create(docPath, _vaultDir);
        await _storage.InitializeEntryAsync(entry, default);
        return await pipeline.MemorizeAsync(entry, options);
    }

    [Fact]
    public async Task Memorize_WithGraphRagServiceRegistered_AutoBuildsGraphIndex()
    {
        var graph = CreateGraphMock();
        var pipeline = CreatePipeline(graph);

        var result = await MemorizeFileAsync(pipeline, new MemorizeOptions { MaxChunkSize = 200 });

        result.Success.Should().BeTrue();
        pipeline.SupportsGraphRAG.Should().BeTrue();
        await graph.Received(1).BuildIndexAsync(
            Arg.Is<IEnumerable<DocumentChunk>>(c => c.Any()),
            Arg.Any<GraphRAGBuildOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Memorize_GraphRagExplicitlyDisabled_SkipsGraphBuildEvenWhenRegistered()
    {
        var graph = CreateGraphMock();
        var pipeline = CreatePipeline(graph);

        var result = await MemorizeFileAsync(
            pipeline,
            new MemorizeOptions { MaxChunkSize = 200, EnableGraphRAG = false });

        result.Success.Should().BeTrue();
        await graph.DidNotReceive().BuildIndexAsync(
            Arg.Any<IEnumerable<DocumentChunk>>(),
            Arg.Any<GraphRAGBuildOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Memorize_ForceEnableGraphRagWithoutService_FailsWithClearError()
    {
        var pipeline = CreatePipeline(graphRAG: null);

        var result = await MemorizeFileAsync(
            pipeline,
            new MemorizeOptions { MaxChunkSize = 200, EnableGraphRAG = true });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("IGraphRAGService is not registered");
    }

    [Fact]
    public async Task Memorize_WithoutGraphRagService_CompletesWithoutGraphBuild()
    {
        var pipeline = CreatePipeline(graphRAG: null);

        var result = await MemorizeFileAsync(pipeline, new MemorizeOptions { MaxChunkSize = 200 });

        result.Success.Should().BeTrue();
        result.ChunkCount.Should().BeGreaterThan(0);
        pipeline.SupportsGraphRAG.Should().BeFalse();
    }

    [Fact]
    public async Task Memorize_PassesGraphRagBuildOptionsThrough()
    {
        var graph = CreateGraphMock();
        var pipeline = CreatePipeline(graph);
        var buildOptions = new GraphRAGBuildOptions { MaxChunks = 7 };

        await MemorizeFileAsync(
            pipeline,
            new MemorizeOptions { MaxChunkSize = 200, EnableGraphRAG = true, GraphRAGOptions = buildOptions });

        await graph.Received(1).BuildIndexAsync(
            Arg.Any<IEnumerable<DocumentChunk>>(),
            buildOptions,
            Arg.Any<CancellationToken>());
    }
}
