using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Extensions.FileVault.Tests.Services;

/// <summary>
/// Strategy routing in <see cref="VaultPipeline.SearchAsync"/> (ISSUE-161). Verifies that a hybrid
/// request is routed to <see cref="IHybridSearchService"/> when available and otherwise degrades to
/// vector while reporting the executed strategy truthfully (no silent mismatch).
/// </summary>
public class VaultPipelineSearchStrategyTests
{
    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly IContentHasher _hasher = Substitute.For<IContentHasher>();
    private readonly IVaultStorageService _storage = Substitute.For<IVaultStorageService>();
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly IEmbeddingService _embedding = Substitute.For<IEmbeddingService>();

    private VaultPipeline CreatePipeline(IHybridSearchService? hybrid)
    {
        _embedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new float[] { 0.1f, 0.2f, 0.3f, 0.4f });

        _vectorStore.SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk>
            {
                new() { Id = "vec-chunk", DocumentId = "doc-1", Content = "vector hit", ChunkIndex = 0, Score = 0.8f }
            });

        return new VaultPipeline(
            _git, _hasher, _storage, NullLogger<VaultPipeline>.Instance,
            options: null, extractor: null, chunker: null,
            vectorStore: _vectorStore, embeddingService: _embedding, hybridSearch: hybrid);
    }

    [Fact]
    public async Task SearchAsync_VectorRequest_ExecutesVector()
    {
        var hybrid = Substitute.For<IHybridSearchService>();
        var pipeline = CreatePipeline(hybrid);

        var response = await pipeline.SearchAsync("q", documentIds: null, topK: 5, minScore: 0f, strategy: VaultSearchStrategy.Vector);

        response.ExecutedStrategy.Should().Be(VaultSearchStrategy.Vector);
        response.Results.Should().ContainSingle().Which.DocumentId.Should().Be("doc-1");
        await hybrid.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<HybridSearchOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_HybridRequest_WithHybridService_ExecutesHybrid()
    {
        var hybrid = Substitute.For<IHybridSearchService>();
        hybrid.SearchAsync(Arg.Any<string>(), Arg.Any<HybridSearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new List<HybridSearchResult>
            {
                new() { Chunk = new DocumentChunk { Id = "hyb-chunk", DocumentId = "doc-2", Content = "fused hit", ChunkIndex = 1 }, FusedScore = 0.95 }
            });
        var pipeline = CreatePipeline(hybrid);

        var response = await pipeline.SearchAsync("q", documentIds: null, topK: 5, minScore: 0f, strategy: VaultSearchStrategy.Hybrid);

        response.ExecutedStrategy.Should().Be(VaultSearchStrategy.Hybrid);
        var item = response.Results.Should().ContainSingle().Subject;
        item.DocumentId.Should().Be("doc-2");
        item.Content.Should().Be("fused hit");
        item.Score.Should().BeApproximately(0.95f, 0.0001f);
        await _vectorStore.DidNotReceive().SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_HybridRequest_WithoutHybridService_DegradesToVectorTruthfully()
    {
        var pipeline = CreatePipeline(hybrid: null);

        var response = await pipeline.SearchAsync("q", documentIds: null, topK: 5, minScore: 0f, strategy: VaultSearchStrategy.Hybrid);

        // No IHybridSearchService registered: executes vector and says so (this is the #2 fix).
        response.ExecutedStrategy.Should().Be(VaultSearchStrategy.Vector);
        response.Results.Should().ContainSingle().Which.DocumentId.Should().Be("doc-1");
    }

    [Fact]
    public async Task SearchAsync_HybridRequest_FiltersByDocumentIds()
    {
        var hybrid = Substitute.For<IHybridSearchService>();
        hybrid.SearchAsync(Arg.Any<string>(), Arg.Any<HybridSearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new List<HybridSearchResult>
            {
                new() { Chunk = new DocumentChunk { Id = "a", DocumentId = "keep", Content = "in scope", ChunkIndex = 0 }, FusedScore = 0.9 },
                new() { Chunk = new DocumentChunk { Id = "b", DocumentId = "drop", Content = "out of scope", ChunkIndex = 0 }, FusedScore = 0.8 }
            });
        var pipeline = CreatePipeline(hybrid);

        var response = await pipeline.SearchAsync("q", documentIds: new[] { "keep" }, topK: 5, minScore: 0f, strategy: VaultSearchStrategy.Hybrid);

        response.ExecutedStrategy.Should().Be(VaultSearchStrategy.Hybrid);
        response.Results.Should().ContainSingle().Which.DocumentId.Should().Be("keep");
    }
}
