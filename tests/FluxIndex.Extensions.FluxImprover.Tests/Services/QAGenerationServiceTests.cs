using FluentAssertions;
using FluxIndex.Extensions.FluxImprover.Services;
using FluxImprover.Options;
using FluxImprover.Services;
using FluxImprover.Evaluation;
using FluxImprover.QAGeneration;
using Moq;
using Xunit;
using FluxIndexChunk = FluxIndex.Core.Application.Interfaces.IEnrichedChunk;
using FluxIndexSource = FluxIndex.Core.Application.Interfaces.ISourceMetadata;

namespace FluxIndex.Extensions.FluxImprover.Tests.Services;

/// <summary>
/// QAGenerationService 테스트 - FluxIndex 청크에서 Q&amp;A 쌍 생성
/// </summary>
public class QAGenerationServiceTests
{
    private readonly Mock<ITextCompletionService> _mockCompletionService;
    private readonly QAGeneratorService _generatorService;
    private readonly QAFilterService _filterService;
    private readonly QAPipeline _pipeline;
    private readonly QAGenerationService _service;

    public QAGenerationServiceTests()
    {
        _mockCompletionService = new Mock<ITextCompletionService>();
        SetupDefaultCompletionResponse();

        _generatorService = new QAGeneratorService(_mockCompletionService.Object);

        // QAFilterService requires evaluators
        var faithfulnessEvaluator = new FaithfulnessEvaluator(_mockCompletionService.Object);
        var relevancyEvaluator = new RelevancyEvaluator(_mockCompletionService.Object);
        var answerabilityEvaluator = new AnswerabilityEvaluator(_mockCompletionService.Object);
        _filterService = new QAFilterService(faithfulnessEvaluator, relevancyEvaluator, answerabilityEvaluator);

        _pipeline = new QAPipeline(_generatorService, _filterService);

        _service = new QAGenerationService(_generatorService, _filterService, _pipeline);
    }

    private void SetupDefaultCompletionResponse()
    {
        // QA 생성용 응답
        var qaGenerationResponse = """
        [
            {"question": "What is FluxIndex?", "answer": "FluxIndex is a RAG infrastructure library."},
            {"question": "What does FluxIndex support?", "answer": "FluxIndex supports hybrid search."}
        ]
        """;

        // 평가용 응답
        var evaluationResponse = """{"score": 0.85, "details": {}}""";

        _mockCompletionService
            .Setup(s => s.CompleteAsync(
                It.Is<string>(p => p.Contains("question") || p.Contains("QA")),
                It.IsAny<CompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(qaGenerationResponse);

        _mockCompletionService
            .Setup(s => s.CompleteAsync(
                It.Is<string>(p => p.Contains("score") || p.Contains("evaluate")),
                It.IsAny<CompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluationResponse);
    }

    [Fact]
    public async Task GenerateFromChunkAsync_ReturnsQAPairs()
    {
        // Arrange
        var chunk = CreateMockFluxIndexChunk("chunk-001", "FluxIndex is a RAG infrastructure library that supports hybrid search.");

        // Act
        var result = await _service.GenerateFromChunkAsync(chunk);

        // Assert
        result.Should().NotBeNull();
        // 실제 생성 결과는 LLM 응답에 따라 달라짐
    }

    [Fact]
    public async Task GenerateFromChunkAsync_WithNullChunk_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.GenerateFromChunkAsync(null!));
    }

    [Fact]
    public async Task GenerateFromChunksAsync_ProcessesAllChunks()
    {
        // Arrange
        var chunks = new List<FluxIndexChunk>
        {
            CreateMockFluxIndexChunk("chunk-001", "Content 1"),
            CreateMockFluxIndexChunk("chunk-002", "Content 2"),
            CreateMockFluxIndexChunk("chunk-003", "Content 3")
        };

        // Act
        var results = await _service.GenerateFromChunksAsync(chunks);

        // Assert
        results.Should().HaveCount(3);
        results.Select(r => r.ChunkId).Should().BeEquivalentTo(new[] { "chunk-001", "chunk-002", "chunk-003" });
    }

    [Fact]
    public async Task FilterAsync_WithNullPairs_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.FilterAsync(null!));
    }

    [Fact]
    public async Task ExecutePipelineAsync_ReturnsResult()
    {
        // Arrange
        var chunk = CreateMockFluxIndexChunk("chunk-001", "Test content for QA generation.");

        // Act
        var result = await _service.ExecutePipelineAsync(chunk);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecutePipelineAsync_WithNullChunk_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.ExecutePipelineAsync(null!));
    }

    [Fact]
    public async Task ExecutePipelineBatchAsync_ProcessesAllChunks()
    {
        // Arrange
        var chunks = new List<FluxIndexChunk>
        {
            CreateMockFluxIndexChunk("chunk-001", "Content 1"),
            CreateMockFluxIndexChunk("chunk-002", "Content 2")
        };

        // Act
        var results = await _service.ExecutePipelineBatchAsync(chunks);

        // Assert
        results.Should().HaveCount(2);
        results.Select(r => r.ChunkId).Should().BeEquivalentTo(new[] { "chunk-001", "chunk-002" });
    }

    [Fact]
    public async Task GenerateDatasetAsync_CreatesComprehensiveDataset()
    {
        // Arrange
        var chunks = new List<FluxIndexChunk>
        {
            CreateMockFluxIndexChunk("chunk-001", "Content about FluxIndex features."),
            CreateMockFluxIndexChunk("chunk-002", "Content about hybrid search.")
        };

        // Act
        var dataset = await _service.GenerateDatasetAsync(chunks);

        // Assert
        dataset.Should().NotBeNull();
        dataset.ChunkCount.Should().Be(2);
    }

    [Fact]
    public void DocumentQADataset_PassRate_CalculatesCorrectly()
    {
        // Arrange
        var dataset = new DocumentQADataset
        {
            QAPairs = new List<DatasetQAPair>(),
            TotalGenerated = 100,
            TotalFiltered = 75,
            ChunkCount = 10
        };

        // Act & Assert
        dataset.PassRate.Should().Be(0.75);
    }

    [Fact]
    public void DocumentQADataset_PassRate_ReturnsZeroWhenNoGenerated()
    {
        // Arrange
        var dataset = new DocumentQADataset
        {
            QAPairs = new List<DatasetQAPair>(),
            TotalGenerated = 0,
            TotalFiltered = 0,
            ChunkCount = 0
        };

        // Act & Assert
        dataset.PassRate.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithNullGeneratorService_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new QAGenerationService(null!, _filterService, _pipeline);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("generatorService");
    }

    [Fact]
    public void Constructor_WithNullFilterService_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new QAGenerationService(_generatorService, null!, _pipeline);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("filterService");
    }

    [Fact]
    public void Constructor_WithNullPipeline_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new QAGenerationService(_generatorService, _filterService, null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("pipeline");
    }

    [Fact]
    public void ChunkQAPairs_Properties_SetCorrectly()
    {
        // Arrange & Act
        var result = new ChunkQAPairs
        {
            ChunkId = "test-chunk",
            SourceId = "test-source",
            QAPairs = new List<GeneratedQAPair>()
        };

        // Assert
        result.ChunkId.Should().Be("test-chunk");
        result.SourceId.Should().Be("test-source");
        result.QAPairs.Should().NotBeNull();
    }

    [Fact]
    public void DatasetQAPair_Properties_SetCorrectly()
    {
        // Arrange & Act
        var pair = new DatasetQAPair
        {
            ChunkId = "chunk-001",
            SourceId = "doc-001",
            Question = "What is FluxIndex?",
            Answer = "A RAG library.",
            Context = "FluxIndex is a RAG infrastructure library."
        };

        // Assert
        pair.ChunkId.Should().Be("chunk-001");
        pair.SourceId.Should().Be("doc-001");
        pair.Question.Should().Be("What is FluxIndex?");
        pair.Answer.Should().Be("A RAG library.");
        pair.Context.Should().Be("FluxIndex is a RAG infrastructure library.");
        pair.Evaluation.Should().BeNull();
    }

    private static FluxIndexChunk CreateMockFluxIndexChunk(string chunkId, string content)
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
