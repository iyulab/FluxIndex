using FileFlux.Core;
using FluxIndex.Integrations.FileFlux.Processing;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

// Resolve ambiguity between FileFlux.Core and FluxIndex.Integrations.FileFlux.Processing
using ProcessingProgress = FluxIndex.Integrations.FileFlux.Processing.ProcessingProgress;
using ProcessingStage = FluxIndex.Integrations.FileFlux.Processing.ProcessingStage;

namespace FluxIndex.SDK.Tests.Processing;

/// <summary>
/// Tests for DocumentProcessingPipeline stage-specific methods.
/// These methods support two-stage processing with user edit capability.
/// </summary>
public class DocumentProcessingPipelineStageTests : IDisposable
{
    private readonly IDocumentProcessorFactory _mockProcessorFactory;
    private readonly ILogger<DocumentProcessingPipeline> _mockLogger;
    private readonly DocumentProcessingPipeline _pipeline;
    private readonly string _testDir;

    public DocumentProcessingPipelineStageTests()
    {
        _mockProcessorFactory = Substitute.For<IDocumentProcessorFactory>();
        _mockLogger = Substitute.For<ILogger<DocumentProcessingPipeline>>();

        _pipeline = new DocumentProcessingPipeline(
            _mockProcessorFactory,
            embeddingService: null,
            textCompletionService: null,
            contextualEnrichmentService: null,
            qaGenerationService: null,
            _mockLogger);

        _testDir = Path.Combine(Path.GetTempPath(), $"fluxindex_stage_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch (IOException)
        {
            // Ignore cleanup errors
        }
    }

    #region Helper Methods for Factory Pattern

    private IDocumentProcessor SetupMockProcessor(
        string filePath,
        IEnumerable<DocumentChunk>? chunks = null,
        RawContent? rawContent = null)
    {
        var mockProcessor = Substitute.For<IDocumentProcessor>();

        // Create real ProcessingResult instance (not mockable - concrete class)
        var result = new ProcessingResult
        {
            Chunks = (chunks ?? Array.Empty<DocumentChunk>()).ToList(),
            Raw = rawContent
        };

        mockProcessor.Result.Returns(result);

        // Setup async methods
        mockProcessor.ExtractAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        mockProcessor.ProcessAsync(Arg.Any<global::FileFlux.Core.ProcessingOptions>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        // Setup IAsyncDisposable
        mockProcessor.DisposeAsync().Returns(ValueTask.CompletedTask);

        // Setup factory to return this processor
        _mockProcessorFactory.Create(filePath).Returns(mockProcessor);

        return mockProcessor;
    }

    #endregion

    #region ExtractOnlyAsync Tests

    [Fact]
    public async Task ExtractOnlyAsync_WithValidFile_ShouldExtractText()
    {
        // Arrange
        var testFile = CreateTestFile(".txt", "Sample document content for extraction.");
        var chunks = new[] { new DocumentChunk { Content = "Sample document content for extraction." } };
        var rawContent = new RawContent { Text = "Sample document content for extraction." };

        SetupMockProcessor(testFile, chunks: chunks, rawContent: rawContent);

        // Act
        var result = await _pipeline.ExtractOnlyAsync(testFile);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Sample document content for extraction.", result.ExtractedText);
        Assert.Equal(testFile, result.SourcePath);
        Assert.NotEmpty(result.SourceHash);
        Assert.NotEmpty(result.DocumentId);
    }

    [Fact]
    public async Task ExtractOnlyAsync_WithImages_ShouldExtractImagesWhenEnabled()
    {
        // Arrange
        var testFile = CreateTestFile(".pdf", "Document with images");
        var testImageData = CreateTestImageData();

        var rawContent = new RawContent
        {
            Text = "Document with images",
            Images = new List<ImageInfo>
            {
                new() { Id = "img_000", Data = testImageData, MimeType = "image/png" },
                new() { Id = "img_001", Data = testImageData, MimeType = "image/jpeg" }
            }
        };
        var chunks = new[] { new DocumentChunk { Content = "Document with images" } };

        SetupMockProcessor(testFile, chunks: chunks, rawContent: rawContent);

        // Act
        var result = await _pipeline.ExtractOnlyAsync(testFile, extractImages: true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Images.Count);
        Assert.True(result.Images.ContainsKey("img_000.png"));
        Assert.True(result.Images.ContainsKey("img_001.jpg"));
    }

    [Fact]
    public async Task ExtractOnlyAsync_WithImagesDisabled_ShouldNotExtractImages()
    {
        // Arrange
        var testFile = CreateTestFile(".pdf", "Document with images");
        var chunks = new[] { new DocumentChunk { Content = "Document with images" } };

        SetupMockProcessor(testFile, chunks: chunks);

        // Act
        var result = await _pipeline.ExtractOnlyAsync(testFile, extractImages: false);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Images);
    }

    [Fact]
    public async Task ExtractOnlyAsync_WhenExtractionFails_ShouldReturnFailedResult()
    {
        // Arrange
        var testFile = CreateTestFile(".pdf", "Test");
        var mockProcessor = Substitute.For<IDocumentProcessor>();

        mockProcessor.ProcessAsync(Arg.Any<global::FileFlux.Core.ProcessingOptions>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("Extraction failed"));
        mockProcessor.DisposeAsync().Returns(ValueTask.CompletedTask);

        _mockProcessorFactory.Create(testFile).Returns(mockProcessor);

        // Act
        var result = await _pipeline.ExtractOnlyAsync(testFile);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Extraction failed", result.ErrorMessage);
    }

    [Fact]
    public async Task ExtractOnlyAsync_ShouldComputeSourceHash()
    {
        // Arrange
        var testFile = CreateTestFile(".txt", "Content for hash verification");
        var chunks = new[] { new DocumentChunk { Content = "Content for hash verification" } };

        SetupMockProcessor(testFile, chunks: chunks);

        // Act
        var result = await _pipeline.ExtractOnlyAsync(testFile);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.SourceHash);
        Assert.Equal(64, result.SourceHash.Length); // SHA-256 hex string
    }

    #endregion

    #region ProcessFromContentAsync Tests

    [Fact]
    public async Task ProcessFromContentAsync_WithValidContent_ShouldChunkAndProcess()
    {
        // Arrange
        var content = "This is test content. It has multiple sentences. Each sentence is a potential chunk boundary.";

        // For content-based processing, we need to mock with any file path since a temp file is used
        var mockProcessor = Substitute.For<IDocumentProcessor>();
        var chunks = new[]
        {
            new DocumentChunk { Content = "This is test content.", Quality = 0.8, Strategy = "paragraph" },
            new DocumentChunk { Content = "It has multiple sentences.", Quality = 0.7, Strategy = "paragraph" }
        };

        var result1 = new ProcessingResult { Chunks = chunks.ToList() };
        mockProcessor.Result.Returns(result1);
        mockProcessor.ProcessAsync(Arg.Any<global::FileFlux.Core.ProcessingOptions>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        mockProcessor.DisposeAsync().Returns(ValueTask.CompletedTask);

        _mockProcessorFactory.Create(Arg.Any<string>()).Returns(mockProcessor);

        // Act
        var result = await _pipeline.ProcessFromContentAsync(content);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Chunks.Count);
        Assert.Equal("This is test content.", result.Chunks[0].Content);
        Assert.Equal(content, result.ExtractedText);
    }

    [Fact]
    public async Task ProcessFromContentAsync_WithCustomOptions_ShouldApplyOptions()
    {
        // Arrange
        var content = "Test content for custom options";
        var options = new ContentProcessingOptions
        {
            DocumentId = "custom-doc-id",
            Language = "ko",
            MaxChunkSize = 512,
            OverlapSize = 64
        };

        var mockProcessor = Substitute.For<IDocumentProcessor>();
        var chunks = new[] { new DocumentChunk { Content = content, Quality = 0.9, Strategy = "custom" } };

        var result2 = new ProcessingResult { Chunks = chunks.ToList() };
        mockProcessor.Result.Returns(result2);
        mockProcessor.ProcessAsync(
            Arg.Is<global::FileFlux.Core.ProcessingOptions>(o => o.Chunking != null && o.Chunking.MaxChunkSize == 512 && o.Chunking.OverlapSize == 64),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        mockProcessor.DisposeAsync().Returns(ValueTask.CompletedTask);

        _mockProcessorFactory.Create(Arg.Any<string>()).Returns(mockProcessor);

        // Act
        var result = await _pipeline.ProcessFromContentAsync(content, options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("custom-doc-id", result.DocumentId);
        Assert.Single(result.Chunks);
    }

    [Fact]
    public async Task ProcessFromContentAsync_WithEmptyContent_ShouldHandleGracefully()
    {
        // Arrange
        var content = "";

        var mockProcessor = Substitute.For<IDocumentProcessor>();
        var emptyResult = new ProcessingResult { Chunks = new List<DocumentChunk>() };
        mockProcessor.Result.Returns(emptyResult);
        mockProcessor.ProcessAsync(Arg.Any<global::FileFlux.Core.ProcessingOptions>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        mockProcessor.DisposeAsync().Returns(ValueTask.CompletedTask);

        _mockProcessorFactory.Create(Arg.Any<string>()).Returns(mockProcessor);

        // Act
        var result = await _pipeline.ProcessFromContentAsync(content);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Chunks);
    }

    [Fact]
    public async Task ProcessFromContentAsync_ShouldReportProgress()
    {
        // Arrange
        var content = "Test content";
        var progressReports = new List<ProcessingProgress>();
        var options = new ContentProcessingOptions
        {
            OnProgress = p => progressReports.Add(p)
        };

        var mockProcessor = Substitute.For<IDocumentProcessor>();
        var chunks = new[] { new DocumentChunk { Content = content, Quality = 0.8, Strategy = "test" } };

        var progressResult = new ProcessingResult { Chunks = chunks.ToList() };
        mockProcessor.Result.Returns(progressResult);
        mockProcessor.ProcessAsync(Arg.Any<global::FileFlux.Core.ProcessingOptions>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        mockProcessor.DisposeAsync().Returns(ValueTask.CompletedTask);

        _mockProcessorFactory.Create(Arg.Any<string>()).Returns(mockProcessor);

        // Act
        var result = await _pipeline.ProcessFromContentAsync(content, options);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, p => p.Stage == ProcessingStage.Initializing);
        Assert.Contains(progressReports, p => p.Stage == ProcessingStage.Chunking);
    }

    #endregion

    #region ProcessFromExtractionAsync Tests

    [Fact]
    public async Task ProcessFromExtractionAsync_WithValidExtractionResult_ShouldProcess()
    {
        // Arrange
        var extractionResult = new ExtractionResult
        {
            DocumentId = "doc-123",
            SourcePath = "/path/to/original.pdf",
            SourceHash = "abc123hash",
            ExtractedText = "Original extracted text",
            Success = true,
            ExtractedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        var mockProcessor = Substitute.For<IDocumentProcessor>();
        var chunks = new[] { new DocumentChunk { Content = "Original extracted text", Quality = 0.8, Strategy = "test" } };

        var extractResult = new ProcessingResult { Chunks = chunks.ToList() };
        mockProcessor.Result.Returns(extractResult);
        mockProcessor.ProcessAsync(Arg.Any<global::FileFlux.Core.ProcessingOptions>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        mockProcessor.DisposeAsync().Returns(ValueTask.CompletedTask);

        _mockProcessorFactory.Create(Arg.Any<string>()).Returns(mockProcessor);

        // Act
        var result = await _pipeline.ProcessFromExtractionAsync(extractionResult);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("doc-123", result.DocumentId);
        Assert.Equal("/path/to/original.pdf", result.SourcePath);
        Assert.Single(result.Chunks);
    }

    [Fact]
    public async Task ProcessFromExtractionAsync_WithModifiedContent_ShouldUseModifiedContent()
    {
        // Arrange
        var extractionResult = new ExtractionResult
        {
            DocumentId = "doc-123",
            SourcePath = "/path/to/original.pdf",
            ExtractedText = "Original text",
            Success = true
        };

        var modifiedContent = "User edited and modified text";

        var mockProcessor = Substitute.For<IDocumentProcessor>();
        var chunks = new[] { new DocumentChunk { Content = modifiedContent, Quality = 0.85, Strategy = "test" } };

        var modifiedResult = new ProcessingResult { Chunks = chunks.ToList() };
        mockProcessor.Result.Returns(modifiedResult);
        mockProcessor.ProcessAsync(Arg.Any<global::FileFlux.Core.ProcessingOptions>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        mockProcessor.DisposeAsync().Returns(ValueTask.CompletedTask);

        _mockProcessorFactory.Create(Arg.Any<string>()).Returns(mockProcessor);

        // Act
        var result = await _pipeline.ProcessFromExtractionAsync(extractionResult, modifiedContent);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(modifiedContent, result.ExtractedText);
        Assert.Single(result.Chunks);
        Assert.Equal(modifiedContent, result.Chunks[0].Content);
    }

    [Fact]
    public async Task ProcessFromExtractionAsync_WithImages_ShouldPreserveImages()
    {
        // Arrange
        var testImageData = CreateTestImageData();
        var extractionResult = new ExtractionResult
        {
            DocumentId = "doc-with-images",
            ExtractedText = "Document with images",
            Images = new Dictionary<string, byte[]>
            {
                ["img_000.png"] = testImageData,
                ["img_001.jpg"] = testImageData
            },
            Success = true
        };

        var mockProcessor = Substitute.For<IDocumentProcessor>();
        var chunks = new[] { new DocumentChunk { Content = "Document with images", Quality = 0.8, Strategy = "test" } };

        var imagesResult = new ProcessingResult { Chunks = chunks.ToList() };
        mockProcessor.Result.Returns(imagesResult);
        mockProcessor.ProcessAsync(Arg.Any<global::FileFlux.Core.ProcessingOptions>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        mockProcessor.DisposeAsync().Returns(ValueTask.CompletedTask);

        _mockProcessorFactory.Create(Arg.Any<string>()).Returns(mockProcessor);

        // Act
        var result = await _pipeline.ProcessFromExtractionAsync(extractionResult);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Images.Count);
        Assert.True(result.Images.ContainsKey("img_000.png"));
        Assert.True(result.Images.ContainsKey("img_001.jpg"));
    }

    [Fact]
    public async Task ProcessFromExtractionAsync_WithFailedExtraction_ShouldReturnError()
    {
        // Arrange
        var extractionResult = new ExtractionResult
        {
            DocumentId = "failed-doc",
            Success = false,
            ErrorMessage = "Original extraction failed"
        };

        // Act
        var result = await _pipeline.ProcessFromExtractionAsync(extractionResult);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Original extraction failed", result.ErrorMessage);
    }

    #endregion

    #region HasSourceChanged Tests

    [Fact]
    public void HasSourceChanged_WhenFileUnchanged_ShouldReturnFalse()
    {
        // Arrange
        var testFile = CreateTestFile(".txt", "Unchanged content");
        var originalHash = ExtractionResult.ComputeFileHash(testFile);

        // Act
        var hasChanged = DocumentProcessingPipeline.HasSourceChanged(testFile, originalHash);

        // Assert
        Assert.False(hasChanged);
    }

    [Fact]
    public void HasSourceChanged_WhenFileModified_ShouldReturnTrue()
    {
        // Arrange
        var testFile = CreateTestFile(".txt", "Original content");
        var originalHash = ExtractionResult.ComputeFileHash(testFile);

        // Modify the file
        File.WriteAllText(testFile, "Modified content");

        // Act
        var hasChanged = DocumentProcessingPipeline.HasSourceChanged(testFile, originalHash);

        // Assert
        Assert.True(hasChanged);
    }

    [Fact]
    public void HasSourceChanged_WhenFileDeleted_ShouldReturnTrue()
    {
        // Arrange
        var testFile = CreateTestFile(".txt", "Content to be deleted");
        var originalHash = ExtractionResult.ComputeFileHash(testFile);

        // Delete the file
        File.Delete(testFile);

        // Act
        var hasChanged = DocumentProcessingPipeline.HasSourceChanged(testFile, originalHash);

        // Assert
        Assert.True(hasChanged);
    }

    #endregion

    #region ExtractionResult Tests

    [Fact]
    public void ExtractionResult_ComputeFileHash_ShouldReturnConsistentHash()
    {
        // Arrange
        var testFile = CreateTestFile(".txt", "Content for hash test");

        // Act
        var hash1 = ExtractionResult.ComputeFileHash(testFile);
        var hash2 = ExtractionResult.ComputeFileHash(testFile);

        // Assert
        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // SHA-256 produces 64 hex chars
    }

    [Fact]
    public void ExtractionResult_HasSourceChanged_WithMatchingHash_ShouldReturnFalse()
    {
        // Arrange
        var testFile = CreateTestFile(".txt", "Test content");
        var extraction = new ExtractionResult
        {
            SourcePath = testFile,
            SourceHash = ExtractionResult.ComputeFileHash(testFile)
        };

        // Act
        var hasChanged = extraction.HasSourceChanged(testFile);

        // Assert
        Assert.False(hasChanged);
    }

    [Fact]
    public void ExtractionResult_HasSourceChanged_WithDifferentContent_ShouldReturnTrue()
    {
        // Arrange
        var testFile = CreateTestFile(".txt", "Original content");
        var extraction = new ExtractionResult
        {
            SourcePath = testFile,
            SourceHash = ExtractionResult.ComputeFileHash(testFile)
        };

        // Modify the file
        File.WriteAllText(testFile, "Modified content");

        // Act
        var hasChanged = extraction.HasSourceChanged(testFile);

        // Assert
        Assert.True(hasChanged);
    }

    #endregion

    #region Markdown Conversion Tests

    [Fact]
    public async Task ExtractOnlyAsync_WithMarkdownConversion_ShouldConvertToMarkdown()
    {
        // Arrange
        var testFile = CreateTestFile(".txt", "# Heading\n\nThis is paragraph text.\n\n- List item 1\n- List item 2");

        var rawContent = new RawContent
        {
            Text = "# Heading\n\nThis is paragraph text.\n\n- List item 1\n- List item 2"
        };

        SetupMockProcessor(testFile, rawContent: rawContent);

        var options = new ExtractionOptions
        {
            ExtractImages = false,
            ConvertToMarkdown = true
        };

        // Act
        var result = await _pipeline.ExtractOnlyAsync(testFile, options);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.MarkdownText);
        Assert.NotNull(result.MarkdownStatistics);
        Assert.True(result.MarkdownStatistics!.HeadingCount >= 1);
    }

    [Fact]
    public async Task ExtractOnlyAsync_WithMarkdownConversion_ShouldDetectLists()
    {
        // Arrange
        var testFile = CreateTestFile(".txt", "Shopping List\n\n- Apples\n- Oranges\n- Bananas");

        var rawContent = new RawContent
        {
            Text = "Shopping List\n\n- Apples\n- Oranges\n- Bananas"
        };

        SetupMockProcessor(testFile, rawContent: rawContent);

        var options = new ExtractionOptions
        {
            ExtractImages = false,
            ConvertToMarkdown = true
        };

        // Act
        var result = await _pipeline.ExtractOnlyAsync(testFile, options);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.MarkdownStatistics);
        Assert.True(result.MarkdownStatistics!.ListCount >= 3);
    }

    [Fact]
    public async Task ExtractOnlyAsync_WithMarkdownOptions_ShouldApplyOptions()
    {
        // Arrange
        var testFile = CreateTestFile(".txt", "INTRODUCTION\n\nSome text here.");

        var rawContent = new RawContent
        {
            Text = "INTRODUCTION\n\nSome text here."
        };

        SetupMockProcessor(testFile, rawContent: rawContent);

        var options = new ExtractionOptions
        {
            ExtractImages = false,
            ConvertToMarkdown = true,
            MarkdownOptions = new MarkdownOptions
            {
                PreserveHeadings = true,
                NormalizeWhitespace = true
            }
        };

        // Act
        var result = await _pipeline.ExtractOnlyAsync(testFile, options);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.MarkdownText);
        Assert.Equal("Heuristic", result.MarkdownStatistics?.Method);
    }

    [Fact]
    public async Task ConvertToMarkdownAsync_WithValidText_ShouldConvert()
    {
        // Arrange
        var text = "# Title\n\nParagraph content.\n\n- Item 1\n- Item 2";

        // Act
        var (markdown, statistics) = await _pipeline.ConvertToMarkdownAsync(text);

        // Assert
        Assert.NotEmpty(markdown);
        Assert.NotNull(statistics);
        Assert.True(statistics.HeadingCount >= 1);
    }

    [Fact]
    public async Task ConvertToMarkdownAsync_WithEmptyText_ShouldReturnEmpty()
    {
        // Arrange
        var text = "";

        // Act
        var (markdown, statistics) = await _pipeline.ConvertToMarkdownAsync(text);

        // Assert
        Assert.NotNull(statistics);
        Assert.Equal(0, statistics.OriginalLength);
    }

    [Fact]
    public async Task ConvertToMarkdownAsync_WithTableContent_ShouldDetectTables()
    {
        // Arrange
        var text = "| Column A | Column B |\n|----------|----------|\n| Data 1   | Data 2   |";

        // Act
        var (markdown, statistics) = await _pipeline.ConvertToMarkdownAsync(text);

        // Assert
        Assert.NotNull(statistics);
        Assert.True(statistics.TableCount >= 1);
    }

    [Fact]
    public async Task ConvertToMarkdownAsync_WithCodeBlock_ShouldPreserve()
    {
        // Arrange
        var text = "```python\ndef hello():\n    print('Hello')\n```";

        // Act
        var (markdown, statistics) = await _pipeline.ConvertToMarkdownAsync(text);

        // Assert
        Assert.Contains("```", markdown);
        Assert.NotNull(statistics);
    }

    [Fact]
    public void ExtractionOptions_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var options = new ExtractionOptions();

        // Assert
        Assert.True(options.ExtractImages);
        Assert.False(options.ConvertToMarkdown);
        Assert.Null(options.MarkdownOptions);
    }

    [Fact]
    public void MarkdownOptions_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var options = new MarkdownOptions();

        // Assert
        Assert.True(options.PreserveHeadings);
        Assert.True(options.ConvertTables);
        Assert.True(options.PreserveLists);
        Assert.True(options.IncludeImagePlaceholders);
        Assert.False(options.UseLLMInference);
        Assert.True(options.DetectCodeBlocks);
        Assert.True(options.NormalizeWhitespace);
    }

    #endregion

    #region ContentProcessingOptions Tests

    [Fact]
    public void ContentProcessingOptions_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var options = new ContentProcessingOptions();

        // Assert
        Assert.Null(options.DocumentId);
        Assert.Null(options.Language);
        Assert.Equal("Auto", options.ChunkingStrategy);
        Assert.Equal(1024, options.MaxChunkSize);
        Assert.Equal(128, options.OverlapSize);
        Assert.True(options.GenerateEmbeddings);
        Assert.False(options.EnableContextualEnrichment);
        Assert.False(options.EnableQAGeneration);
        Assert.Equal(3, options.MaxQAPairsPerChunk);
    }

    #endregion

    #region Helper Methods

    private string CreateTestFile(string extension, string content = "Test content")
    {
        var filePath = Path.Combine(_testDir, $"test_document_{Guid.NewGuid()}{extension}");
        File.WriteAllText(filePath, content);
        return filePath;
    }

    private static byte[] CreateTestImageData()
    {
        return new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR chunk
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, // 1x1 pixel
            0x08, 0x02, 0x00, 0x00, 0x00                    // 8-bit RGB
        };
    }

    #endregion
}
