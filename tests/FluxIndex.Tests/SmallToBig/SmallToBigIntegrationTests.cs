using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Storage.PostgreSQL;
using FluxIndex.Storage.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FluxIndex.Tests.SmallToBig;

/// <summary>
/// Small-to-Big 검색 통합 테스트
/// </summary>
public class SmallToBigIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly FluxIndexDbContext _context;
    private readonly ISmallToBigRetriever _retriever;
    private readonly IChunkHierarchyRepository _hierarchyRepository;

    public SmallToBigIntegrationTests()
    {
        var services = new ServiceCollection();

        // In-memory database for testing
        services.AddDbContext<FluxIndexDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        // Services
        services.AddMemoryCache();
        services.AddLogging(builder => builder.AddConsole());
        services.AddScoped<IChunkHierarchyRepository, ChunkHierarchyRepository>();

        // Mock HybridSearchService for integration testing
        services.AddScoped<IHybridSearchService, MockHybridSearchService>();
        services.AddScoped<ISmallToBigRetriever, SmallToBigRetriever>();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<FluxIndexDbContext>();
        _retriever = _serviceProvider.GetRequiredService<ISmallToBigRetriever>();
        _hierarchyRepository = _serviceProvider.GetRequiredService<IChunkHierarchyRepository>();
    }

    [Fact]
    public async Task EndToEnd_CompleteWorkflow_Works()
    {
        // Arrange: Create test document with hierarchical structure
        var documentId = Guid.NewGuid().ToString();
        var chunks = await CreateTestDocumentHierarchy(documentId);

        // Act: Search with Small-to-Big
        var query = "machine learning algorithms";
        var options = new SmallToBigOptions
        {
            MaxResults = 5,
            EnableAdaptiveWindowing = true,
            EnableHierarchicalExpansion = true,
            EnableSemanticExpansion = true
        };

        var results = await _retriever.SearchAsync(query, options, CancellationToken.None);

        // Assert: Verify results structure
        Assert.NotEmpty(results);

        var result = results.First();
        Assert.NotNull(result.PrimaryChunk);
        Assert.NotEmpty(result.ContextChunks);
        Assert.True(result.RelevanceScore > 0);
        Assert.True(result.WindowSize > 0);
        Assert.NotNull(result.Strategy);
        Assert.NotNull(result.Metadata);

        // Verify context expansion worked
        Assert.True(result.ContextChunks.Count >= 1,
            "컨텍스트 확장으로 최소 1개 이상의 추가 청크가 있어야 합니다.");

        // Verify quality metrics
        Assert.True(result.ContextQuality > 0.5,
            "컨텍스트 품질이 임계값 이상이어야 합니다.");
    }

    [Fact]
    public async Task ComplexityBasedWindowing_DifferentQueries_AdaptsWindowSize()
    {
        // Arrange: Create test data
        var documentId = Guid.NewGuid().ToString();
        await CreateTestDocumentHierarchy(documentId);

        var simpleQuery = "hello";
        var complexQuery = "deep learning transformer attention mechanisms and their mathematical foundations";

        // Act: Test different complexity queries
        var simpleWindowSize = await _retriever.DetermineOptimalWindowSizeAsync(simpleQuery, CancellationToken.None);
        var complexWindowSize = await _retriever.DetermineOptimalWindowSizeAsync(complexQuery, CancellationToken.None);

        // Assert: Complex query should get larger window
        Assert.True(complexWindowSize > simpleWindowSize,
            $"복잡한 쿼리({complexWindowSize})가 간단한 쿼리({simpleWindowSize})보다 큰 윈도우를 가져야 합니다.");

        Assert.InRange(simpleWindowSize, 1, 3);
        Assert.InRange(complexWindowSize, 4, 10);
    }

    [Fact]
    public async Task HierarchicalExpansion_MultiLevel_ExpandsCorrectly()
    {
        // Arrange: Create 3-level hierarchy (sentence -> paragraph -> section)
        var documentId = Guid.NewGuid().ToString();
        var chunks = await CreateMultiLevelHierarchy(documentId);

        var sentenceChunk = chunks.First(c => c.Metadata.ContainsKey("level") && c.Metadata["level"].ToString() == "0");

        // Act: Expand context from sentence level
        var result = await _retriever.ExpandContextAsync(
            sentenceChunk,
            windowSize: 3,
            new ContextExpansionOptions
            {
                EnableHierarchicalExpansion = true,
                EnableSequentialExpansion = true,
                MaxExpansionDistance = 2
            },
            CancellationToken.None);

        // Assert: Should include parent paragraph and section
        Assert.NotEmpty(result.ExpandedChunks);
        Assert.Contains(ExpansionMethod.Hierarchical, result.ExpansionBreakdown.Keys);

        // Verify parent chunks are included
        var parentLevels = result.ExpandedChunks
            .Where(c => c.Metadata.ContainsKey("level"))
            .Select(c => int.Parse(c.Metadata["level"].ToString()!))
            .ToList();

        Assert.Contains(1, parentLevels); // Paragraph level
        Assert.True(result.ExpansionQuality > 0.6);
    }

    [Fact]
    public async Task PerformanceEvaluation_WithGroundTruth_CalculatesMetrics()
    {
        // Arrange: Create test data with known ground truth
        var documentId = Guid.NewGuid().ToString();
        await CreateTestDocumentHierarchy(documentId);

        var testQueries = new List<string>
        {
            "machine learning",
            "data science",
            "artificial intelligence"
        };

        // Create realistic ground truth (what chunks should be returned)
        var groundTruth = new List<IReadOnlyList<string>>
        {
            new List<string> { "chunk_ml_1", "chunk_ml_2" },
            new List<string> { "chunk_ds_1", "chunk_ds_2" },
            new List<string> { "chunk_ai_1", "chunk_ai_2" }
        };

        // Act: Evaluate performance
        var metrics = await _retriever.EvaluatePerformanceAsync(testQueries, groundTruth, CancellationToken.None);

        // Assert: Verify metrics are calculated
        Assert.InRange(metrics.Precision, 0.0, 1.0);
        Assert.InRange(metrics.Recall, 0.0, 1.0);
        Assert.InRange(metrics.F1Score, 0.0, 1.0);
        Assert.InRange(metrics.ContextQuality, 0.0, 1.0);
        Assert.True(metrics.AverageResponseTime > 0);
        Assert.True(metrics.ExpansionEfficiency > 0);

        // Verify strategy performance breakdown
        Assert.NotEmpty(metrics.StrategyPerformance);
        Assert.NotEmpty(metrics.WindowSizePerformance);
    }

    [Fact]
    public async Task ChunkHierarchyBuilding_FromFlatChunks_CreatesHierarchy()
    {
        // Arrange: Create flat chunks representing a document structure
        var chunks = CreateStructuredDocument();

        // Act: Build hierarchy
        var buildResult = await _retriever.BuildChunkHierarchyAsync(chunks, CancellationToken.None);

        // Assert: Verify hierarchy was built
        Assert.True(buildResult.HierarchyCount > 0);
        Assert.True(buildResult.SuccessRate > 0.8);
        Assert.True(buildResult.QualityScore > 0.7);
        Assert.NotEmpty(buildResult.LevelDistribution);

        // Verify hierarchies were saved
        foreach (var chunk in chunks.Take(3)) // Check first few
        {
            var hierarchy = await _hierarchyRepository.GetHierarchyAsync(chunk.Id, CancellationToken.None);
            Assert.NotNull(hierarchy);
        }
    }

    [Fact]
    public async Task AdaptiveStrategy_QueriesOfDifferentTypes_SelectsAppropriateStrategy()
    {
        // Arrange
        var documentId = Guid.NewGuid().ToString();
        var chunks = await CreateTestDocumentHierarchy(documentId);
        var testChunk = chunks.First();

        var factualQuery = "What is machine learning?";
        var analyticalQuery = "Compare deep learning architectures and analyze their trade-offs in computational efficiency versus accuracy";
        var simpleQuery = "hello";

        // Act: Get strategy recommendations
        var factualStrategy = await _retriever.RecommendExpansionStrategyAsync(factualQuery, testChunk, CancellationToken.None);
        var analyticalStrategy = await _retriever.RecommendExpansionStrategyAsync(analyticalQuery, testChunk, CancellationToken.None);
        var simpleStrategy = await _retriever.RecommendExpansionStrategyAsync(simpleQuery, testChunk, CancellationToken.None);

        // Assert: Different strategies for different query types
        Assert.Equal(ExpansionStrategyType.Balanced, factualStrategy.Type);
        Assert.True(analyticalStrategy.Type == ExpansionStrategyType.Aggressive || analyticalStrategy.Type == ExpansionStrategyType.Adaptive);
        Assert.Equal(ExpansionStrategyType.Conservative, simpleStrategy.Type);

        // Verify strategy confidence
        Assert.True(factualStrategy.Confidence > 0.6);
        Assert.True(analyticalStrategy.Confidence > 0.7);
        Assert.True(simpleStrategy.Confidence > 0.8);
    }

    private async Task<List<DocumentChunk>> CreateTestDocumentHierarchy(string documentId)
    {
        var chunks = new List<DocumentChunk>
        {
            CreateChunk("chunk_ml_1", documentId, "Machine learning is a subset of artificial intelligence.", 0),
            CreateChunk("chunk_ml_2", documentId, "It focuses on algorithms that improve through experience.", 1),
            CreateChunk("chunk_ds_1", documentId, "Data science combines statistics and programming.", 2),
            CreateChunk("chunk_ai_1", documentId, "Artificial intelligence aims to create intelligent machines.", 3),
            CreateChunk("chunk_context", documentId, "This provides additional context for understanding.", 4)
        };

        // Add to database
        _context.Chunks.AddRange(chunks);
        await _context.SaveChangesAsync();

        // Create hierarchies
        var hierarchies = new List<ChunkHierarchy>
        {
            CreateHierarchy("chunk_ml_1", null, new[] { "chunk_ml_2" }, 0),
            CreateHierarchy("chunk_ml_2", "chunk_ml_1", Array.Empty<string>(), 1),
            CreateHierarchy("chunk_ds_1", null, Array.Empty<string>(), 0),
            CreateHierarchy("chunk_ai_1", null, Array.Empty<string>(), 0),
            CreateHierarchy("chunk_context", null, Array.Empty<string>(), 0)
        };

        foreach (var hierarchy in hierarchies)
        {
            await _hierarchyRepository.SaveHierarchyAsync(hierarchy, CancellationToken.None);
        }

        return chunks;
    }

    private async Task<List<DocumentChunk>> CreateMultiLevelHierarchy(string documentId)
    {
        var chunks = new List<DocumentChunk>
        {
            CreateChunk("sentence_1", documentId, "This is the first sentence.", 0, new Dictionary<string, object> { ["level"] = 0 }),
            CreateChunk("paragraph_1", documentId, "This paragraph contains multiple sentences about AI.", 1, new Dictionary<string, object> { ["level"] = 1 }),
            CreateChunk("section_1", documentId, "Section 1: Introduction to Artificial Intelligence", 2, new Dictionary<string, object> { ["level"] = 2 })
        };

        _context.Chunks.AddRange(chunks);
        await _context.SaveChangesAsync();

        // Create 3-level hierarchy
        var hierarchies = new List<ChunkHierarchy>
        {
            CreateHierarchy("sentence_1", "paragraph_1", Array.Empty<string>(), 0),
            CreateHierarchy("paragraph_1", "section_1", new[] { "sentence_1" }, 1),
            CreateHierarchy("section_1", null, new[] { "paragraph_1" }, 2)
        };

        foreach (var hierarchy in hierarchies)
        {
            await _hierarchyRepository.SaveHierarchyAsync(hierarchy, CancellationToken.None);
        }

        return chunks;
    }

    private List<DocumentChunk> CreateStructuredDocument()
    {
        return new List<DocumentChunk>
        {
            CreateChunk("title", Guid.NewGuid().ToString(), "Introduction to Machine Learning", 0),
            CreateChunk("para1_sent1", Guid.NewGuid().ToString(), "Machine learning is powerful.", 1),
            CreateChunk("para1_sent2", Guid.NewGuid().ToString(), "It learns from data automatically.", 2),
            CreateChunk("para2_sent1", Guid.NewGuid().ToString(), "Deep learning is a subset.", 3),
            CreateChunk("conclusion", Guid.NewGuid().ToString(), "ML will transform industries.", 4)
        };
    }

    private DocumentChunk CreateChunk(string id, string documentId, string content, int index, Dictionary<string, object>? metadata = null)
    {
        return new DocumentChunk
        {
            Id = id,
            DocumentId = documentId,
            Content = content,
            ChunkIndex = index,
            Embedding = new float[1536], // Default OpenAI embedding dimension
            TokenCount = content.Split(' ').Length,
            Metadata = metadata ?? new Dictionary<string, object>(),
            CreatedAt = DateTime.UtcNow
        };
    }

    private ChunkHierarchy CreateHierarchy(string chunkId, string? parentId, string[] childIds, int level)
    {
        return new ChunkHierarchy
        {
            ChunkId = chunkId,
            ParentChunkId = parentId,
            ChildChunkIds = childIds.ToList(),
            HierarchyLevel = level,
            RecommendedWindowSize = level + 2,
            Boundary = new ChunkBoundary
            {
                StartPosition = 0,
                EndPosition = 100,
                Type = BoundaryType.Sentence,
                Confidence = 0.95
            },
            Metadata = new HierarchyMetadata
            {
                Depth = level,
                SiblingCount = childIds.Length,
                QualityScore = 0.9
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
}

/// <summary>
/// Mock HybridSearchService for integration testing
/// </summary>
internal class MockHybridSearchService : IHybridSearchService
{
    public Task<IReadOnlyList<HybridSearchResult>> SearchAsync(string query, HybridSearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Return mock results based on query content
        var results = new List<HybridSearchResult>();

        if (query.Contains("machine learning") || query.Contains("ML"))
        {
            results.Add(new HybridSearchResult
            {
                Chunk = CreateMockChunk("chunk_ml_1", "Machine learning is a subset of artificial intelligence."),
                Score = 0.9,
                VectorScore = 0.85,
                SparseScore = 0.95
            });
        }

        if (query.Contains("algorithms") || query.Contains("complex"))
        {
            results.Add(new HybridSearchResult
            {
                Chunk = CreateMockChunk("chunk_ml_2", "It focuses on algorithms that improve through experience."),
                Score = 0.8,
                VectorScore = 0.75,
                SparseScore = 0.85
            });
        }

        // Always return at least one result for testing
        if (!results.Any())
        {
            results.Add(new HybridSearchResult
            {
                Chunk = CreateMockChunk("default_chunk", "Default test content for query: " + query),
                Score = 0.7,
                VectorScore = 0.65,
                SparseScore = 0.75
            });
        }

        return Task.FromResult<IReadOnlyList<HybridSearchResult>>(results);
    }

    public Task<DocumentChunk?> GetChunkByIdAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        var chunk = CreateMockChunk(chunkId, $"Content for chunk {chunkId}");
        return Task.FromResult<DocumentChunk?>(chunk);
    }

    private DocumentChunk CreateMockChunk(string id, string content)
    {
        return new DocumentChunk
        {
            Id = id,
            DocumentId = Guid.NewGuid().ToString(),
            Content = content,
            ChunkIndex = 0,
            Embedding = new float[1536],
            TokenCount = content.Split(' ').Length,
            Metadata = new Dictionary<string, object>(),
            CreatedAt = DateTime.UtcNow
        };
    }
}