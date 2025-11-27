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
/// FluxImproverPipeline 테스트 - 전체 파이프라인 오케스트레이션
/// </summary>
public class FluxImproverPipelineTests
{
    private readonly Mock<ITextCompletionService> _mockCompletionService;
    private readonly Mock<ISummarizationService> _mockSummarizationService;
    private readonly Mock<IKeywordExtractionService> _mockKeywordService;

    public FluxImproverPipelineTests()
    {
        _mockCompletionService = new Mock<ITextCompletionService>();
        _mockSummarizationService = new Mock<ISummarizationService>();
        _mockKeywordService = new Mock<IKeywordExtractionService>();
        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        // Summarization service
        _mockSummarizationService
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test summary of the content.");

        // Keyword extraction service
        _mockKeywordService
            .Setup(s => s.ExtractKeywordsAsync(It.IsAny<string>(), It.IsAny<EnrichmentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "keyword1", "keyword2", "keyword3" });

        // QA Generation response
        var qaGenerationResponse = """
        [
            {"question": "What is FluxIndex?", "answer": "FluxIndex is a RAG library."},
            {"question": "What features does FluxIndex have?", "answer": "FluxIndex supports hybrid search."}
        ]
        """;

        // Evaluation response
        var evaluationResponse = """{"score": 0.85, "details": {}}""";

        _mockCompletionService
            .Setup(s => s.CompleteAsync(
                It.Is<string>(p => p.Contains("question") || p.Contains("QA")),
                It.IsAny<CompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(qaGenerationResponse);

        _mockCompletionService
            .Setup(s => s.CompleteAsync(
                It.Is<string>(p => p.Contains("score") || p.Contains("evaluate") || p.Contains("faithfulness") || p.Contains("relevancy") || p.Contains("answerability")),
                It.IsAny<CompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluationResponse);
    }

    [Fact]
    public void Capabilities_WithNoServices_ReturnsAllFalse()
    {
        // Arrange
        var pipeline = new FluxImproverPipeline();

        // Act
        var capabilities = pipeline.Capabilities;

        // Assert
        capabilities.CanEnrich.Should().BeFalse();
        capabilities.CanGenerateQA.Should().BeFalse();
        capabilities.CanEvaluate.Should().BeFalse();
        capabilities.IsFullyCapable.Should().BeFalse();
    }

    [Fact]
    public void Capabilities_WithAllServices_ReturnsAllTrue()
    {
        // Arrange
        var enrichmentService = CreateEnrichmentWrapper();
        var qaService = CreateQAGenerationService();
        var evaluationService = CreateRAGEvaluationService();

        var pipeline = new FluxImproverPipeline(enrichmentService, qaService, evaluationService);

        // Act
        var capabilities = pipeline.Capabilities;

        // Assert
        capabilities.CanEnrich.Should().BeTrue();
        capabilities.CanGenerateQA.Should().BeTrue();
        capabilities.CanEvaluate.Should().BeTrue();
        capabilities.IsFullyCapable.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessChunkAsync_WithNullChunk_ThrowsArgumentNullException()
    {
        // Arrange
        var pipeline = new FluxImproverPipeline();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => pipeline.ProcessChunkAsync(null!));
    }

    [Fact]
    public async Task ProcessChunkAsync_WithNoServices_ReturnsSuccessWithNoProcessing()
    {
        // Arrange
        var pipeline = new FluxImproverPipeline();
        var chunk = CreateMockChunk("chunk-001", "Test content");

        // Act
        var result = await pipeline.ProcessChunkAsync(chunk);

        // Assert
        result.Should().NotBeNull();
        result.ChunkId.Should().Be("chunk-001");
        result.Success.Should().BeTrue();
        result.EnrichmentCompleted.Should().BeFalse();
        result.QAGenerationCompleted.Should().BeFalse();
        result.EvaluationCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessChunkAsync_WithEnrichmentService_EnrichesChunk()
    {
        // Arrange
        var enrichmentService = CreateEnrichmentWrapper();
        var pipeline = new FluxImproverPipeline(enrichmentService: enrichmentService);
        var chunk = CreateMockChunk("chunk-001", "Test content for enrichment.");

        var options = new PipelineOptions
        {
            EnableEnrichment = true,
            EnableQAGeneration = false,
            EnableEvaluation = false
        };

        // Act
        var result = await pipeline.ProcessChunkAsync(chunk, options);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.EnrichmentCompleted.Should().BeTrue();
        result.EnrichedChunk.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessChunkAsync_WithQAService_GeneratesQAPairs()
    {
        // Arrange
        var qaService = CreateQAGenerationService();
        var pipeline = new FluxImproverPipeline(qaService: qaService);
        var chunk = CreateMockChunk("chunk-001", "Test content for QA generation.");

        var options = new PipelineOptions
        {
            EnableEnrichment = false,
            EnableQAGeneration = true,
            EnableEvaluation = false
        };

        // Act
        var result = await pipeline.ProcessChunkAsync(chunk, options);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.QAGenerationCompleted.Should().BeTrue();
        result.GeneratedQAPairs.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessChunkAsync_WithDisabledOptions_SkipsProcessing()
    {
        // Arrange
        var enrichmentService = CreateEnrichmentWrapper();
        var qaService = CreateQAGenerationService();
        var evaluationService = CreateRAGEvaluationService();
        var pipeline = new FluxImproverPipeline(enrichmentService, qaService, evaluationService);
        var chunk = CreateMockChunk("chunk-001", "Test content");

        var options = new PipelineOptions
        {
            EnableEnrichment = false,
            EnableQAGeneration = false,
            EnableEvaluation = false
        };

        // Act
        var result = await pipeline.ProcessChunkAsync(chunk, options);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.EnrichmentCompleted.Should().BeFalse();
        result.QAGenerationCompleted.Should().BeFalse();
        result.EvaluationCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessBatchAsync_ProcessesAllChunks()
    {
        // Arrange
        var qaService = CreateQAGenerationService();
        var pipeline = new FluxImproverPipeline(qaService: qaService);
        var chunks = new List<FluxIndexChunk>
        {
            CreateMockChunk("chunk-001", "Content 1"),
            CreateMockChunk("chunk-002", "Content 2"),
            CreateMockChunk("chunk-003", "Content 3")
        };

        var options = new PipelineOptions
        {
            EnableEnrichment = false,
            EnableQAGeneration = true,
            EnableEvaluation = false
        };

        // Act
        var result = await pipeline.ProcessBatchAsync(chunks, options);

        // Assert
        result.Should().NotBeNull();
        result.TotalChunks.Should().Be(3);
        result.SuccessfulChunks.Should().Be(3);
        result.FailedChunks.Should().Be(0);
        result.SuccessRate.Should().Be(1.0);
    }

    [Fact]
    public async Task ProcessBatchAsync_InvokesProgressCallback()
    {
        // Arrange
        var pipeline = new FluxImproverPipeline();
        var chunks = new List<FluxIndexChunk>
        {
            CreateMockChunk("chunk-001", "Content 1"),
            CreateMockChunk("chunk-002", "Content 2")
        };

        var progressCalls = new List<(int processed, int total)>();
        void ProgressCallback(int processed, int total) => progressCalls.Add((processed, total));

        // Act
        await pipeline.ProcessBatchAsync(chunks, progressCallback: ProgressCallback);

        // Assert
        progressCalls.Should().HaveCount(2);
        progressCalls[0].Should().Be((1, 2));
        progressCalls[1].Should().Be((2, 2));
    }

    [Fact]
    public async Task GenerateDocumentQADatasetAsync_WithoutQAService_ThrowsInvalidOperationException()
    {
        // Arrange
        var pipeline = new FluxImproverPipeline();
        var chunks = new List<FluxIndexChunk> { CreateMockChunk("chunk-001", "Content") };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.GenerateDocumentQADatasetAsync(chunks));
    }

    [Fact]
    public async Task GenerateDocumentQADatasetAsync_WithQAService_ReturnsDataset()
    {
        // Arrange
        var qaService = CreateQAGenerationService();
        var pipeline = new FluxImproverPipeline(qaService: qaService);
        var chunks = new List<FluxIndexChunk>
        {
            CreateMockChunk("chunk-001", "Content 1"),
            CreateMockChunk("chunk-002", "Content 2")
        };

        // Act
        var result = await pipeline.GenerateDocumentQADatasetAsync(chunks);

        // Assert
        result.Should().NotBeNull();
        result.ChunkCount.Should().Be(2);
    }

    [Fact]
    public async Task EvaluateRAGResponseAsync_WithoutEvaluationService_ThrowsInvalidOperationException()
    {
        // Arrange
        var pipeline = new FluxImproverPipeline();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.EvaluateRAGResponseAsync("context", "question", "answer"));
    }

    [Fact]
    public async Task EvaluateRAGResponseAsync_WithEvaluationService_ReturnsEvaluation()
    {
        // Arrange
        var evaluationService = CreateRAGEvaluationService();
        var pipeline = new FluxImproverPipeline(evaluationService: evaluationService);

        // Act
        var result = await pipeline.EvaluateRAGResponseAsync(
            "FluxIndex is a RAG library.",
            "What is FluxIndex?",
            "FluxIndex is a RAG library for indexing and search.");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void PipelineOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new PipelineOptions();

        // Assert
        options.EnableEnrichment.Should().BeTrue();
        options.EnableQAGeneration.Should().BeTrue();
        options.EnableEvaluation.Should().BeTrue();
        options.EnrichmentOptions.Should().BeNull();
        options.QAGenerationOptions.Should().BeNull();
        options.EvaluationOptions.Should().BeNull();
    }

    [Fact]
    public void BatchPipelineResult_SuccessRate_CalculatesCorrectly()
    {
        // Arrange
        var results = new List<PipelineResult>
        {
            new() { ChunkId = "1", SourceId = "doc", Success = true },
            new() { ChunkId = "2", SourceId = "doc", Success = true },
            new() { ChunkId = "3", SourceId = "doc", Success = false },
            new() { ChunkId = "4", SourceId = "doc", Success = true }
        };

        var batchResult = new BatchPipelineResult
        {
            Results = results,
            TotalChunks = 4,
            SuccessfulChunks = 3,
            FailedChunks = 1,
            TotalQAPairsGenerated = 6,
            TotalQAPairsEvaluated = 4
        };

        // Assert
        batchResult.SuccessRate.Should().Be(0.75);
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

    private RAGEvaluationService CreateRAGEvaluationService()
    {
        var answerabilityEvaluator = new AnswerabilityEvaluator(_mockCompletionService.Object);
        var faithfulnessEvaluator = new FaithfulnessEvaluator(_mockCompletionService.Object);
        var relevancyEvaluator = new RelevancyEvaluator(_mockCompletionService.Object);
        return new RAGEvaluationService(answerabilityEvaluator, faithfulnessEvaluator, relevancyEvaluator);
    }

    private static FluxIndexChunk CreateMockChunk(string chunkId, string content)
    {
        var mockSource = new Mock<FluxIndexSource>();
        mockSource.Setup(s => s.SourceId).Returns("doc-123");
        mockSource.Setup(s => s.Title).Returns("Test Document");
        mockSource.Setup(s => s.Language).Returns("en");
        mockSource.Setup(s => s.SourceType).Returns("pdf");
        mockSource.Setup(s => s.WordCount).Returns(100);

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
