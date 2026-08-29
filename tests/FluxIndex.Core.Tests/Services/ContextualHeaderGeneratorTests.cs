using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using ITextCompletionService = Flux.Abstractions.ITextCompletionService;
using FluxIndex.Core.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

using MsOptions = Microsoft.Extensions.Options.Options;

namespace FluxIndex.Core.Tests.Services;

public class ContextualHeaderGeneratorTests
{
    private readonly ILogger<HybridContextualHeaderGenerator> _loggerMock;
    private readonly ContextualHeaderOptions _options;

    public ContextualHeaderGeneratorTests()
    {
        _loggerMock = Substitute.For<ILogger<HybridContextualHeaderGenerator>>();
        _options = new ContextualHeaderOptions
        {
            LlmThreshold = 0.7,
            MaxHeaderLength = 200,
            IncludeDocumentTitle = true,
            IncludeHeadingPath = true,
            IncludePageInfo = true
        };
    }

    [Fact]
    public async Task GenerateAsync_WithLowContextDependency_ReturnsRuleBasedHeader()
    {
        // Arrange
        var generator = CreateGenerator();
        var chunk = CreateTestChunk(contextDependency: 0.3);

        // Act
        var header = await generator.GenerateAsync(chunk, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(header);
        Assert.Contains("[Test Document]", header);
        Assert.Contains("[Chapter 1 > Section 1.1]", header);
    }

    [Fact]
    public async Task GenerateAsync_WithHighContextDependency_NoLlm_FallsBackToRuleBased()
    {
        // Arrange
        var generator = CreateGenerator(withLlm: false);
        var chunk = CreateTestChunk(contextDependency: 0.9);

        // Act
        var header = await generator.GenerateAsync(chunk, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(header);
        Assert.Contains("[Test Document]", header);
    }

    [Fact]
    public async Task GenerateAsync_WithHighContextDependency_WithLlm_UsesLlm()
    {
        // Arrange
        var textCompletionMock = Substitute.For<ITextCompletionService>();
        textCompletionMock.CompleteAsync(
                Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("This chunk discusses the implementation details of the authentication system.");

        var generator = CreateGenerator(textCompletionMock);
        var chunk = CreateTestChunk(contextDependency: 0.9);

        // Act
        var header = await generator.GenerateAsync(chunk, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("authentication", header);
        await textCompletionMock.Received(1).CompleteAsync(
            Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateAsync_WithPageInfo_IncludesPageNumber()
    {
        // Arrange
        var generator = CreateGenerator();
        var chunk = CreateTestChunk(startPage: 42);

        // Act
        var header = await generator.GenerateAsync(chunk, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("[p.42]", header);
    }

    [Fact]
    public async Task GenerateAsync_WithPageRange_IncludesPageRange()
    {
        // Arrange
        var generator = CreateGenerator();
        var chunk = CreateTestChunk(startPage: 10, endPage: 12);

        // Act
        var header = await generator.GenerateAsync(chunk, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("[pp.10-12]", header);
    }

    [Fact]
    public async Task GenerateBatchAsync_ProcessesMixedContextDependency()
    {
        // Arrange
        var textCompletionMock = Substitute.For<ITextCompletionService>();
        textCompletionMock.CompleteAsync(
                Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("LLM generated header");

        var generator = CreateGenerator(textCompletionMock);

        var chunks = new[]
        {
            CreateTestChunk("chunk1", contextDependency: 0.3),
            CreateTestChunk("chunk2", contextDependency: 0.9),
            CreateTestChunk("chunk3", contextDependency: 0.5)
        };

        // Act
        var headers = await generator.GenerateBatchAsync(chunks, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, headers.Count);
        Assert.Contains("chunk1", headers.Keys);
        Assert.Contains("chunk2", headers.Keys);
        Assert.Contains("chunk3", headers.Keys);

        // Only chunk2 should use LLM
        await textCompletionMock.Received(1).CompleteAsync(
            Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateAsync_ExceedsMaxLength_Truncates()
    {
        // Arrange
        var shortOptions = new ContextualHeaderOptions
        {
            MaxHeaderLength = 30,
            IncludeDocumentTitle = true,
            IncludeHeadingPath = true
        };
        var generator = new HybridContextualHeaderGenerator(
            MsOptions.Create(shortOptions),
            _loggerMock);

        var chunk = CreateTestChunk();

        // Act
        var header = await generator.GenerateAsync(chunk, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(header.Length <= 33); // 30 + "..."
        Assert.EndsWith("...", header);
    }

    [Fact]
    public async Task GenerateAsync_WithoutHeadingPath_OmitsHeadingPath()
    {
        // Arrange
        var options = new ContextualHeaderOptions
        {
            IncludeDocumentTitle = true,
            IncludeHeadingPath = false
        };
        var generator = new HybridContextualHeaderGenerator(
            MsOptions.Create(options),
            _loggerMock);

        var chunk = CreateTestChunk();

        // Act
        var header = await generator.GenerateAsync(chunk, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain("Chapter 1", header);
        Assert.Contains("[Test Document]", header);
    }

    private HybridContextualHeaderGenerator CreateGenerator(
        ITextCompletionService? textCompletion = null,
        bool withLlm = false)
    {
        if (withLlm && textCompletion == null)
        {
            var mock = Substitute.For<ITextCompletionService>();
            mock.CompleteAsync(
                    Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("Mock LLM response");
            textCompletion = mock;
        }

        return new HybridContextualHeaderGenerator(
            MsOptions.Create(_options),
            _loggerMock,
            textCompletion);
    }

    private static TestEnrichedChunk CreateTestChunk(
        string? chunkId = null,
        double contextDependency = 0.5,
        int? startPage = null,
        int? endPage = null)
    {
        return new TestEnrichedChunk
        {
            Content = "This is test content for the chunk.",
            ChunkId = chunkId ?? Guid.NewGuid().ToString(),
            ChunkIndex = 0,
            HeadingPath = new List<string> { "Chapter 1", "Section 1.1" },
            SectionTitle = "Section 1.1",
            StartPage = startPage,
            EndPage = endPage,
            Quality = 0.8,
            ContextDependency = contextDependency,
            TokenCount = 100,
            Source = new TestSourceMetadata
            {
                SourceId = "test-doc-1",
                SourceType = "pdf",
                Title = "Test Document",
                Language = "en",
                WordCount = 1000,
                ChunkCount = 10
            }
        };
    }
}

// Test implementations of interfaces
public class TestEnrichedChunk : IEnrichedChunk
{
    public string Content { get; set; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public IReadOnlyList<string> HeadingPath { get; set; } = new List<string>();
    public string? SectionTitle { get; set; }
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }
    public double Quality { get; set; }
    public double ContextDependency { get; set; }
    public int? TokenCount { get; set; }
    public ISourceMetadata Source { get; set; } = new TestSourceMetadata();
}

public class TestSourceMetadata : ISourceMetadata
{
    public string SourceId { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public string? Url { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Language { get; set; } = "en";
    public double? LanguageConfidence { get; set; }
    public int WordCount { get; set; }
    public int ChunkCount { get; set; }
    public int? PageCount { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? Author { get; set; }
    public IReadOnlyList<string>? Keywords { get; set; }
}
