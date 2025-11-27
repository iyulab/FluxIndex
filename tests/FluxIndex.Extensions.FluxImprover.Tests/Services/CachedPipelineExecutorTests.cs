using FluentAssertions;
using FluxIndex.Extensions.FluxImprover.Services;
using FluxImprover.Abstractions.Options;
using FluxImprover.Abstractions.Services;
using FluxImprover.Enrichment;
using FluxImprover.Evaluation;
using FluxImprover.QAGeneration;
using Moq;
using Xunit;
using FluxIndexChunk = FluxIndex.Core.Application.Interfaces.IEnrichedChunk;
using FluxIndexSource = FluxIndex.Core.Application.Interfaces.ISourceMetadata;

namespace FluxIndex.Extensions.FluxImprover.Tests.Services;

/// <summary>
/// CachedPipelineExecutor 테스트 - 캐싱 및 성능 최적화
/// </summary>
public class CachedPipelineExecutorTests : IDisposable
{
    private readonly Mock<ITextCompletionService> _mockCompletionService;
    private readonly Mock<ISummarizationService> _mockSummarizationService;
    private readonly Mock<IKeywordExtractionService> _mockKeywordService;
    private CachedPipelineExecutor? _executor;

    public CachedPipelineExecutorTests()
    {
        _mockCompletionService = new Mock<ITextCompletionService>();
        _mockSummarizationService = new Mock<ISummarizationService>();
        _mockKeywordService = new Mock<IKeywordExtractionService>();
        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        _mockSummarizationService
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test summary.");

        _mockKeywordService
            .Setup(s => s.ExtractKeywordsAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "keyword1", "keyword2" });

        var qaResponse = """[{"question": "Q1", "answer": "A1"}]""";
        var evalResponse = """{"score": 0.85, "details": {}}""";

        _mockCompletionService
            .Setup(s => s.CompleteAsync(It.Is<string>(p => p.Contains("question")), It.IsAny<CompletionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(qaResponse);

        _mockCompletionService
            .Setup(s => s.CompleteAsync(It.Is<string>(p => !p.Contains("question")), It.IsAny<CompletionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(evalResponse);
    }

    [Fact]
    public async Task EnrichWithCacheAsync_WithoutService_ThrowsInvalidOperationException()
    {
        // Arrange
        _executor = new CachedPipelineExecutor();
        var chunk = CreateMockChunk("1", "Content");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _executor.EnrichWithCacheAsync(chunk));
    }

    [Fact]
    public async Task EnrichWithCacheAsync_CachesResult()
    {
        // Arrange
        var enrichmentService = CreateEnrichmentWrapper();
        _executor = new CachedPipelineExecutor(enrichmentService: enrichmentService);
        var chunk = CreateMockChunk("chunk-001", "Test content");

        // Act - First call (cache miss)
        var result1 = await _executor.EnrichWithCacheAsync(chunk);
        var stats1 = _executor.Statistics;

        // Act - Second call (cache hit)
        var result2 = await _executor.EnrichWithCacheAsync(chunk);
        var stats2 = _executor.Statistics;

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();

        stats1.EnrichmentCacheMisses.Should().Be(1);
        stats1.EnrichmentCacheHits.Should().Be(0);

        stats2.EnrichmentCacheMisses.Should().Be(1);
        stats2.EnrichmentCacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GenerateQAWithCacheAsync_WithoutService_ThrowsInvalidOperationException()
    {
        // Arrange
        _executor = new CachedPipelineExecutor();
        var chunk = CreateMockChunk("1", "Content");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _executor.GenerateQAWithCacheAsync(chunk));
    }

    [Fact]
    public async Task GenerateQAWithCacheAsync_CachesResult()
    {
        // Arrange
        var qaService = CreateQAGenerationService();
        _executor = new CachedPipelineExecutor(qaService: qaService);
        var chunk = CreateMockChunk("chunk-001", "Test content");

        // Act - First call
        var result1 = await _executor.GenerateQAWithCacheAsync(chunk);
        var stats1 = _executor.Statistics;

        // Act - Second call
        var result2 = await _executor.GenerateQAWithCacheAsync(chunk);
        var stats2 = _executor.Statistics;

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();

        stats1.QACacheMisses.Should().Be(1);
        stats1.QACacheHits.Should().Be(0);

        stats2.QACacheMisses.Should().Be(1);
        stats2.QACacheHits.Should().Be(1);
    }

    [Fact]
    public async Task ProcessWithCacheAsync_ProcessesAllChunks()
    {
        // Arrange
        var enrichmentService = CreateEnrichmentWrapper();
        _executor = new CachedPipelineExecutor(enrichmentService: enrichmentService);
        var chunks = Enumerable.Range(1, 3).Select(i => CreateMockChunk($"chunk-{i}", $"Content {i}")).ToList();
        var options = new PipelineOptions
        {
            EnableEnrichment = true,
            EnableQAGeneration = false,
            EnableEvaluation = false
        };

        // Act
        var results = await _executor.ProcessWithCacheAsync(chunks, options);

        // Assert
        results.Should().HaveCount(3);
        results.All(r => r.Success).Should().BeTrue();
    }

    [Fact]
    public void ClearEnrichmentCache_ClearsCache()
    {
        // Arrange
        _executor = new CachedPipelineExecutor();

        // Act
        _executor.ClearEnrichmentCache();

        // Assert - Should not throw
        _executor.Statistics.EnrichmentCacheCount.Should().Be(0);
    }

    [Fact]
    public void ClearAllCaches_ClearsBothCaches()
    {
        // Arrange
        _executor = new CachedPipelineExecutor();

        // Act
        _executor.ClearAllCaches();

        // Assert
        _executor.Statistics.EnrichmentCacheCount.Should().Be(0);
        _executor.Statistics.QACacheCount.Should().Be(0);
    }

    [Fact]
    public void CacheOptions_HasCorrectDefaults()
    {
        // Arrange & Act
        var options = new CacheOptions();

        // Assert
        options.EnrichmentTTL.Should().Be(TimeSpan.FromHours(1));
        options.QAGenerationTTL.Should().Be(TimeSpan.FromHours(1));
        options.MaxEnrichmentCacheSize.Should().Be(1000);
        options.MaxQACacheSize.Should().Be(1000);
        options.EnableAutomaticCleanup.Should().BeTrue();
        options.CleanupInterval.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void CacheStatistics_CalculatesHitRate()
    {
        // Arrange
        var stats = new CacheStatistics
        {
            EnrichmentCacheCount = 10,
            QACacheCount = 5,
            EnrichmentCacheHits = 80,
            EnrichmentCacheMisses = 20,
            QACacheHits = 60,
            QACacheMisses = 40
        };

        // Assert
        stats.EnrichmentHitRate.Should().Be(0.8);
        stats.QAHitRate.Should().Be(0.6);
    }

    [Fact]
    public void CacheStatistics_ReturnsZeroWhenNoCalls()
    {
        // Arrange
        var stats = new CacheStatistics
        {
            EnrichmentCacheHits = 0,
            EnrichmentCacheMisses = 0,
            QACacheHits = 0,
            QACacheMisses = 0
        };

        // Assert
        stats.EnrichmentHitRate.Should().Be(0);
        stats.QAHitRate.Should().Be(0);
    }

    [Fact]
    public void CachedPipelineResult_TracksFromCacheFlag()
    {
        // Arrange & Act
        var result = new CachedPipelineResult
        {
            ChunkId = "test",
            SourceId = "doc",
            EnrichmentFromCache = true,
            QAFromCache = false,
            Success = true
        };

        // Assert
        result.EnrichmentFromCache.Should().BeTrue();
        result.QAFromCache.Should().BeFalse();
    }

    private ChunkEnrichmentServiceWrapper CreateEnrichmentWrapper()
    {
        var enrichmentService = new ChunkEnrichmentService(
            _mockSummarizationService.Object,
            _mockKeywordService.Object);
        return new ChunkEnrichmentServiceWrapper(enrichmentService);
    }

    private QAGenerationService CreateQAGenerationService()
    {
        var generatorService = new QAGeneratorService(_mockCompletionService.Object);
        var faithfulnessEvaluator = new FaithfulnessEvaluator(_mockCompletionService.Object);
        var relevancyEvaluator = new RelevancyEvaluator(_mockCompletionService.Object);
        var answerabilityEvaluator = new AnswerabilityEvaluator(_mockCompletionService.Object);
        var filterService = new QAFilterService(faithfulnessEvaluator, relevancyEvaluator, answerabilityEvaluator);
        var qaPipeline = new QAPipeline(generatorService, filterService);
        return new QAGenerationService(generatorService, filterService, qaPipeline);
    }

    private static FluxIndexChunk CreateMockChunk(string chunkId, string content)
    {
        var mockSource = new Mock<FluxIndexSource>();
        mockSource.Setup(s => s.SourceId).Returns("doc-123");
        mockSource.Setup(s => s.Title).Returns("Test Document");

        var mockChunk = new Mock<FluxIndexChunk>();
        mockChunk.Setup(c => c.ChunkId).Returns(chunkId);
        mockChunk.Setup(c => c.Content).Returns(content);
        mockChunk.Setup(c => c.ChunkIndex).Returns(0);
        mockChunk.Setup(c => c.HeadingPath).Returns(new List<string> { "Section 1" });
        mockChunk.Setup(c => c.SectionTitle).Returns("Section 1");
        mockChunk.Setup(c => c.Quality).Returns(0.85);
        mockChunk.Setup(c => c.ContextDependency).Returns(0.3);
        mockChunk.Setup(c => c.TokenCount).Returns(50);
        mockChunk.Setup(c => c.Source).Returns(mockSource.Object);

        return mockChunk.Object;
    }

    public void Dispose()
    {
        _executor?.Dispose();
    }
}
