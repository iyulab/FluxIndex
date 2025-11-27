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
/// ParallelPipelineExecutor 테스트 - 병렬 처리 및 성능 최적화
/// </summary>
public class ParallelPipelineExecutorTests
{
    private readonly Mock<ITextCompletionService> _mockCompletionService;
    private readonly Mock<ISummarizationService> _mockSummarizationService;
    private readonly Mock<IKeywordExtractionService> _mockKeywordService;

    public ParallelPipelineExecutorTests()
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
    public async Task EnrichParallelAsync_WithoutService_ThrowsInvalidOperationException()
    {
        // Arrange
        var executor = new ParallelPipelineExecutor();
        var chunks = new[] { CreateMockChunk("1", "Content") };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.EnrichParallelAsync(chunks));
    }

    [Fact]
    public async Task EnrichParallelAsync_ProcessesAllChunks()
    {
        // Arrange
        var enrichmentService = CreateEnrichmentWrapper();
        var executor = new ParallelPipelineExecutor(enrichmentService: enrichmentService);
        var chunks = Enumerable.Range(1, 5).Select(i => CreateMockChunk($"chunk-{i}", $"Content {i}")).ToList();

        // Act
        var results = await executor.EnrichParallelAsync(chunks);

        // Assert
        results.Should().HaveCount(5);
        results.All(r => r.Success).Should().BeTrue();
    }

    [Fact]
    public async Task GenerateQAParallelAsync_WithoutService_ThrowsInvalidOperationException()
    {
        // Arrange
        var executor = new ParallelPipelineExecutor();
        var chunks = new[] { CreateMockChunk("1", "Content") };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.GenerateQAParallelAsync(chunks));
    }

    [Fact]
    public async Task GenerateQAParallelAsync_ProcessesAllChunks()
    {
        // Arrange
        var qaService = CreateQAGenerationService();
        var executor = new ParallelPipelineExecutor(qaService: qaService);
        var chunks = Enumerable.Range(1, 3).Select(i => CreateMockChunk($"chunk-{i}", $"Content {i}")).ToList();

        // Act
        var results = await executor.GenerateQAParallelAsync(chunks);

        // Assert
        results.Should().HaveCount(3);
        results.All(r => r.Success).Should().BeTrue();
    }

    [Fact]
    public async Task ProcessStreamAsync_ReturnsAsyncEnumerable()
    {
        // Arrange
        var executor = new ParallelPipelineExecutor();
        var chunks = Enumerable.Range(1, 3).Select(i => CreateMockChunk($"chunk-{i}", $"Content {i}")).ToList();
        var options = new PipelineOptions
        {
            EnableEnrichment = false,
            EnableQAGeneration = false,
            EnableEvaluation = false
        };

        // Act
        var results = new List<PipelineResult>();
        await foreach (var result in executor.ProcessStreamAsync(chunks, options))
        {
            results.Add(result);
        }

        // Assert
        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task ProcessBatchesAsync_ProcessesInBatches()
    {
        // Arrange
        var executor = new ParallelPipelineExecutor();
        var chunks = Enumerable.Range(1, 10).Select(i => CreateMockChunk($"chunk-{i}", $"Content {i}")).ToList();
        var options = new PipelineOptions
        {
            EnableEnrichment = false,
            EnableQAGeneration = false,
            EnableEvaluation = false
        };

        var progressInfos = new List<BatchProgressInfo>();

        // Act
        var result = await executor.ProcessBatchesAsync(
            chunks,
            batchSize: 3,
            options: options,
            progressCallback: info => progressInfos.Add(info));

        // Assert
        result.TotalChunks.Should().Be(10);
        result.SuccessfulChunks.Should().Be(10);
        progressInfos.Should().HaveCount(10);
    }

    [Fact]
    public void ParallelExecutionOptions_HasCorrectDefaults()
    {
        // Arrange & Act
        var options = new ParallelExecutionOptions();

        // Assert
        options.MaxDegreeOfParallelism.Should().Be(Environment.ProcessorCount);
    }

    [Fact]
    public void BatchProgressInfo_CalculatesOverallProgress()
    {
        // Arrange
        var info = new BatchProgressInfo
        {
            CurrentBatch = 2,
            TotalBatches = 4,
            ProcessedInBatch = 5,
            BatchSize = 10,
            TotalProcessed = 15,
            TotalChunks = 40
        };

        // Assert
        info.OverallProgress.Should().Be(0.375); // 15/40
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
}
