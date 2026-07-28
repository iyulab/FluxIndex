using FileFlux.Core;
using FluxIndex.Integrations.FileFlux.Processing;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace FluxIndex.SDK.Tests.Processing;

/// <summary>
/// Tests for DocumentProcessingPipeline image extraction functionality.
/// FileFlux v0.8.5+ supports image extraction from HTML, DOCX, PDF, PPTX, XLSX.
/// </summary>
public class DocumentProcessingPipelineImageTests : IDisposable
{
    private readonly IDocumentProcessorFactory _mockProcessorFactory;
    private readonly ILogger<DocumentProcessingPipeline> _mockLogger;
    private readonly DocumentProcessingPipeline _pipeline;
    private readonly string _testDir;

    public DocumentProcessingPipelineImageTests()
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

        _testDir = Path.Combine(Path.GetTempPath(), $"fluxindex_image_test_{Guid.NewGuid()}");
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

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".docx")]
    [InlineData(".pptx")]
    [InlineData(".xlsx")]
    [InlineData(".html")]
    public async Task ExtractImages_SupportedFormat_ShouldExtractImages(string extension)
    {
        // Arrange
        var testFile = CreateTestFile(extension);
        var testImageData = CreateTestImageData();

        var rawContent = new RawContent
        {
            Text = "Test document content",
            Images = new List<ImageInfo>
            {
                new()
                {
                    Id = "img_000",
                    Data = testImageData,
                    MimeType = "image/png",
                    Position = 1
                },
                new()
                {
                    Id = "img_001",
                    Data = testImageData,
                    MimeType = "image/jpeg",
                    Position = 2
                }
            }
        };

        SetupMockProcessor(testFile, rawContent);

        var options = new DocumentProcessingOptions
        {
            ExtractImages = true,
            GenerateEmbeddings = false
        };

        // Act
        var result = await _pipeline.ProcessAsync(testFile, options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Images.Count);
        Assert.True(result.Images.ContainsKey("img_000.png"));
        Assert.True(result.Images.ContainsKey("img_001.jpg"));
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    [InlineData(".json")]
    [InlineData(".csv")]
    public async Task ExtractImages_UnsupportedFormat_ShouldReturnEmptyImages(string extension)
    {
        // Arrange
        var testFile = CreateTestFile(extension);

        var rawContent = new RawContent
        {
            Text = "Test content"
        };

        SetupMockProcessor(testFile, rawContent);

        var options = new DocumentProcessingOptions
        {
            ExtractImages = true,
            GenerateEmbeddings = false
        };

        // Act
        var result = await _pipeline.ProcessAsync(testFile, options);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Images);
    }

    [Fact]
    public async Task ExtractImages_WithNullImageData_ShouldSkipNullImages()
    {
        // Arrange
        var testFile = CreateTestFile(".pdf");
        var testImageData = CreateTestImageData();

        var rawContent = new RawContent
        {
            Text = "Test document",
            Images = new List<ImageInfo>
            {
                new() { Id = "img_000", Data = testImageData, MimeType = "image/png" },
                new() { Id = "img_001", Data = null, MimeType = "image/png" }, // Null data
                new() { Id = "img_002", Data = Array.Empty<byte>(), MimeType = "image/png" }, // Empty data
                new() { Id = "img_003", Data = testImageData, MimeType = "image/jpeg" }
            }
        };

        SetupMockProcessor(testFile, rawContent);

        var options = new DocumentProcessingOptions
        {
            ExtractImages = true,
            GenerateEmbeddings = false
        };

        // Act
        var result = await _pipeline.ProcessAsync(testFile, options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Images.Count); // Only 2 valid images
        Assert.True(result.Images.ContainsKey("img_000.png"));
        Assert.True(result.Images.ContainsKey("img_003.jpg"));
    }

    [Fact]
    public async Task ExtractImages_WithDifferentMimeTypes_ShouldUseCorrectExtensions()
    {
        // Arrange
        var testFile = CreateTestFile(".docx");
        var testImageData = CreateTestImageData();

        var rawContent = new RawContent
        {
            Text = "Test document",
            Images = new List<ImageInfo>
            {
                new() { Id = "img_png", Data = testImageData, MimeType = "image/png" },
                new() { Id = "img_jpeg", Data = testImageData, MimeType = "image/jpeg" },
                new() { Id = "img_gif", Data = testImageData, MimeType = "image/gif" },
                new() { Id = "img_webp", Data = testImageData, MimeType = "image/webp" },
                new() { Id = "img_svg", Data = testImageData, MimeType = "image/svg+xml" },
                new() { Id = "img_unknown", Data = testImageData, MimeType = "image/unknown" }
            }
        };

        SetupMockProcessor(testFile, rawContent);

        var options = new DocumentProcessingOptions
        {
            ExtractImages = true,
            GenerateEmbeddings = false
        };

        // Act
        var result = await _pipeline.ProcessAsync(testFile, options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(6, result.Images.Count);
        Assert.True(result.Images.ContainsKey("img_png.png"));
        Assert.True(result.Images.ContainsKey("img_jpeg.jpg"));
        Assert.True(result.Images.ContainsKey("img_gif.gif"));
        Assert.True(result.Images.ContainsKey("img_webp.webp"));
        Assert.True(result.Images.ContainsKey("img_svg.svg"));
        Assert.True(result.Images.ContainsKey("img_unknown.jpg")); // Default to .jpg
    }

    [Fact]
    public async Task ExtractImages_Disabled_ShouldNotExtractImages()
    {
        // Arrange
        var testFile = CreateTestFile(".pdf");

        var rawContent = new RawContent
        {
            Text = "Test document"
        };

        SetupMockProcessor(testFile, rawContent);

        var options = new DocumentProcessingOptions
        {
            ExtractImages = false, // Disabled
            GenerateEmbeddings = false
        };

        // Act
        var result = await _pipeline.ProcessAsync(testFile, options);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Images);
    }

    [Fact]
    public async Task ExtractImages_WhenExtractionFails_ShouldContinueProcessing()
    {
        // Arrange
        var testFile = CreateTestFile(".pdf");

        // Setup mock processor that throws on ExtractAsync
        var mockProcessor = Substitute.For<IDocumentProcessor>();
        var chunks = new[] { new DocumentChunk { Content = "Test chunk" } };

        var failResult = new ProcessingResult { Chunks = chunks.ToList(), Raw = null };
        mockProcessor.Result.Returns(failResult);
        mockProcessor.ProcessAsync(Arg.Any<global::FileFlux.Core.ProcessingOptions>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        mockProcessor.ExtractAsync(Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("Extraction failed"));
        mockProcessor.DisposeAsync().Returns(ValueTask.CompletedTask);

        _mockProcessorFactory.Create(testFile).Returns(mockProcessor);

        var options = new DocumentProcessingOptions
        {
            ExtractImages = true,
            GenerateEmbeddings = false
        };

        // Act
        var result = await _pipeline.ProcessAsync(testFile, options);

        // Assert - Pipeline should still succeed, just with empty images
        Assert.True(result.Success);
        Assert.Empty(result.Images);
    }

    #region Helper Methods

    private string CreateTestFile(string extension)
    {
        var filePath = Path.Combine(_testDir, $"test_document{extension}");
        File.WriteAllText(filePath, "Test content");
        return filePath;
    }

    private static byte[] CreateTestImageData()
    {
        // Simple PNG header + minimal data for testing
        return new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR chunk
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, // 1x1 pixel
            0x08, 0x02, 0x00, 0x00, 0x00                    // 8-bit RGB
        };
    }

    private void SetupMockProcessor(string filePath, RawContent rawContent)
    {
        var mockProcessor = Substitute.For<IDocumentProcessor>();

        // Create real ProcessingResult instance (not mockable - concrete class)
        var chunks = new[]
        {
            new DocumentChunk
            {
                Content = rawContent.Text,
                Quality = 0.8,
                Strategy = "test"
            }
        };

        var result = new ProcessingResult
        {
            Chunks = chunks.ToList(),
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
    }

    #endregion
}
