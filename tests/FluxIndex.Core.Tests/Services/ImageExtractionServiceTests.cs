using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Xunit;
using NSubstitute;

namespace FluxIndex.Core.Tests.Services;

public class ImageExtractionServiceTests
{
    private readonly ImageExtractionService _service;

    public ImageExtractionServiceTests()
    {
        _service = new ImageExtractionService();
    }

    [Fact]
    public void HasEmbeddedImages_WithBase64Image_ReturnsTrue()
    {
        // Arrange
        var content = "Some text ![alt](data:image/png;base64,iVBORw0KGgo=) more text";

        // Act
        var result = _service.HasEmbeddedImages(content);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasEmbeddedImages_WithoutBase64Image_ReturnsFalse()
    {
        // Arrange
        var content = "Some text ![alt](https://example.com/image.png) more text";

        // Act
        var result = _service.HasEmbeddedImages(content);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasEmbeddedImages_EmptyContent_ReturnsFalse()
    {
        // Arrange
        var content = "";

        // Act
        var result = _service.HasEmbeddedImages(content);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CountEmbeddedImages_WithMultipleImages_ReturnsCorrectCount()
    {
        // Arrange - Use valid base64 data (minimal PNG header)
        var pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var jpegBase64 = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/";
        var content = $"Text ![img1](data:image/png;base64,{pngBase64}) middle ![img2](data:image/jpeg;base64,{jpegBase64}) end";

        // Act
        var result = _service.CountEmbeddedImages(content);

        // Assert
        Assert.Equal(2, result);
    }

    [Fact]
    public async Task ExtractImagesAsync_WithValidPng_ExtractsCorrectly()
    {
        // Arrange - minimal 1x1 transparent PNG
        var pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var altText = "test image";
        var content = $"Before ![{altText}](data:image/png;base64,{pngBase64}) After";

        // Act
        var result = await _service.ExtractImagesAsync(content);

        // Assert
        Assert.True(result.HasImages);
        Assert.Single(result.ExtractedImages);
        Assert.Equal("image/png", result.ExtractedImages[0].MimeType);
        Assert.Equal(altText, result.ExtractedImages[0].AltText);
        Assert.Contains("[Image 1]", result.CleanedContent);
        Assert.DoesNotContain("data:image", result.CleanedContent);
    }

    [Fact]
    public async Task ExtractImagesAsync_WithEmptyAltText_ExtractsCorrectly()
    {
        // Arrange
        var pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var content = $"Before ![](data:image/png;base64,{pngBase64}) After";

        // Act
        var result = await _service.ExtractImagesAsync(content);

        // Assert
        Assert.True(result.HasImages);
        Assert.Null(result.ExtractedImages[0].AltText);
    }

    [Fact]
    public async Task ExtractImagesAsync_WithNoImages_ReturnsCleanedContent()
    {
        // Arrange
        var content = "Just plain text with no images";

        // Act
        var result = await _service.ExtractImagesAsync(content);

        // Assert
        Assert.False(result.HasImages);
        Assert.Empty(result.ExtractedImages);
        Assert.Equal(content, result.CleanedContent);
    }

    [Fact]
    public async Task ExtractImagesAsync_WithMultipleImages_ExtractsAllInOrder()
    {
        // Arrange
        var pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var content = $"Start ![first](data:image/png;base64,{pngBase64}) middle ![second](data:image/png;base64,{pngBase64}) end";

        // Act
        var result = await _service.ExtractImagesAsync(content);

        // Assert
        Assert.Equal(2, result.ExtractedImages.Count);
        Assert.Equal("first", result.ExtractedImages[0].AltText);
        Assert.Equal("second", result.ExtractedImages[1].AltText);
        Assert.Equal(0, result.ExtractedImages[0].PositionIndex);
        Assert.Equal(1, result.ExtractedImages[1].PositionIndex);
    }

    [Fact]
    public async Task ExtractAndStoreAsync_WithMockStore_StoresImages()
    {
        // Arrange
        var pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var content = $"Text ![img](data:image/png;base64,{pngBase64}) more";
        var documentId = "test-doc-123";

        var mockStore = Substitute.For<IImageStore>();
        mockStore.StoreAsync(
                Arg.Any<string>(), // imageId
                Arg.Any<string>(), // documentId
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()).Returns(callInfo => { var imgId = callInfo.ArgAt<string>(0); var docId = callInfo.ArgAt<string>(1); return $"{docId}/{imgId}.png"; });

        mockStore.GetPublicUrl(Arg.Any<string>()).Returns(callInfo => { var path = callInfo.ArgAt<string>(0); return $"http://localhost/images/{path}"; });

        // Act
        var result = await _service.ExtractAndStoreAsync(documentId, content, mockStore);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.HasImages);
        Assert.Single(result.StoredImages);
        Assert.Equal(documentId, result.StoredImages[0].DocumentId);
        Assert.Contains("[Image:", result.ProcessedContent);

        await mockStore.Received(1).StoreAsync(
            Arg.Any<string>(),
            documentId,
            Arg.Any<byte[]>(),
            "image/png",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EstimateEmbeddedImageSize_ReturnsApproximateSize()
    {
        // Arrange - minimal PNG is about 67 bytes when decoded
        var pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var content = $"![img](data:image/png;base64,{pngBase64})";

        // Act
        var estimatedSize = _service.EstimateEmbeddedImageSize(content);

        // Assert
        // Base64 string length * 0.75 gives approximate decoded size
        var expectedSize = (long)(pngBase64.Length * 0.75);
        Assert.True(Math.Abs(estimatedSize - expectedSize) <= 10, // Allow small variance
            $"Expected ~{expectedSize} bytes, got {estimatedSize}");
    }

    [Fact]
    public async Task ExtractImagesAsync_WithInvalidBase64_HandlesGracefully()
    {
        // Arrange - invalid base64 (missing characters)
        var content = "![img](data:image/png;base64,notvalidbase64!!!) some text";

        // Act
        var result = await _service.ExtractImagesAsync(content);

        // Assert - should not extract invalid image but not throw
        Assert.False(result.HasImages);
        Assert.Contains("notvalidbase64", result.CleanedContent); // Original content preserved
    }

    [Fact]
    public async Task ExtractImagesAsync_PreservesContentSizeReduction()
    {
        // Arrange - large base64 image should reduce content size significantly
        var pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var content = $"Text ![img](data:image/png;base64,{pngBase64}) end";

        // Act
        var result = await _service.ExtractImagesAsync(content);

        // Assert
        Assert.True(result.CleanedContent.Length < content.Length,
            "Cleaned content should be shorter than original");
    }

    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/gif", ".gif")]
    [InlineData("image/webp", ".webp")]
    [InlineData("image/svg+xml", ".svg")]
    [InlineData("image/bmp", ".bmp")]
    [InlineData("image/unknown", ".bin")]
    public void GetExtensionFromMimeType_ReturnsCorrectExtension(string mimeType, string expectedExtension)
    {
        // Act
        var result = ExtractedImage.GetExtensionFromMimeType(mimeType);

        // Assert
        Assert.Equal(expectedExtension, result);
    }

    #region Bare Base64 Image Tests (FileFlux transformed content)

    [Fact]
    public async Task ExtractImagesAsync_WithBareBase64Image_ExtractsCorrectly()
    {
        // Arrange - bare data URL without markdown syntax (as FileFlux transforms content)
        var pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var content = $"Here is an image: data:image/png;base64,{pngBase64} and some text after";

        // Act
        var result = await _service.ExtractImagesAsync(content);

        // Assert
        Assert.True(result.HasImages);
        Assert.Single(result.ExtractedImages);
        Assert.Equal("image/png", result.ExtractedImages[0].MimeType);
        Assert.Null(result.ExtractedImages[0].AltText); // Bare URLs have no alt text
        Assert.Contains("[Image 1]", result.CleanedContent);
        Assert.DoesNotContain("data:image", result.CleanedContent);
    }

    [Fact]
    public async Task ExtractImagesAsync_WithMultipleBareBase64Images_ExtractsAllInOrder()
    {
        // Arrange - multiple bare data URLs
        var pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var content = $"First: data:image/png;base64,{pngBase64} Second: data:image/png;base64,{pngBase64} End";

        // Act
        var result = await _service.ExtractImagesAsync(content);

        // Assert
        Assert.Equal(2, result.ExtractedImages.Count);
        Assert.Equal(0, result.ExtractedImages[0].PositionIndex);
        Assert.Equal(1, result.ExtractedImages[1].PositionIndex);
        Assert.Contains("[Image 1]", result.CleanedContent);
        Assert.Contains("[Image 2]", result.CleanedContent);
    }

    [Fact]
    public void CountEmbeddedImages_WithBareBase64Images_ReturnsCorrectCount()
    {
        // Arrange
        var pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var content = $"First: data:image/png;base64,{pngBase64} Second: data:image/png;base64,{pngBase64}";

        // Act
        var result = _service.CountEmbeddedImages(content);

        // Assert
        Assert.Equal(2, result);
    }

    [Fact]
    public void EstimateEmbeddedImageSize_WithBareBase64Images_ReturnsApproximateSize()
    {
        // Arrange
        var pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var content = $"data:image/png;base64,{pngBase64}";

        // Act
        var estimatedSize = _service.EstimateEmbeddedImageSize(content);

        // Assert
        var expectedSize = (long)(pngBase64.Length * 0.75);
        Assert.True(Math.Abs(estimatedSize - expectedSize) <= 10,
            $"Expected ~{expectedSize} bytes, got {estimatedSize}");
    }

    [Fact]
    public async Task ExtractImagesAsync_WithMixedMarkdownAndBareImages_ExtractsAll()
    {
        // Arrange - mix of markdown and bare base64 images
        var pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var content = $"![markdown](data:image/png;base64,{pngBase64}) and bare: data:image/png;base64,{pngBase64}";

        // Act
        var result = await _service.ExtractImagesAsync(content);

        // Assert
        Assert.Equal(2, result.ExtractedImages.Count);
        Assert.Equal("markdown", result.ExtractedImages[0].AltText); // Markdown image has alt text
        Assert.Null(result.ExtractedImages[1].AltText); // Bare image has no alt text
    }

    [Fact]
    public async Task ExtractImagesAsync_BareBase64DoesNotMatchMarkdown_NoDoubleCounting()
    {
        // Arrange - markdown image should not be double-counted as bare
        var pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var content = $"![test](data:image/png;base64,{pngBase64})";

        // Act
        var result = await _service.ExtractImagesAsync(content);

        // Assert - should only have 1 image, not 2
        Assert.Single(result.ExtractedImages);
        Assert.Equal("test", result.ExtractedImages[0].AltText);
    }

    #endregion
}
