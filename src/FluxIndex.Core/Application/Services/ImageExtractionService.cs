using System.Text.RegularExpressions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Service for extracting embedded base64 images from markdown/HTML content.
/// </summary>
public partial class ImageExtractionService : IImageExtractionService
{
    private readonly ILogger<ImageExtractionService>? _logger;

    // Regex pattern for markdown images with base64 data
    // Matches: ![alt text](data:image/type;base64,...)
    [GeneratedRegex(@"!\[([^\]]*)\]\(data:image/([^;]+);base64,([A-Za-z0-9+/=]+)\)", RegexOptions.Compiled)]
    private static partial Regex Base64MarkdownImageRegex();

    // Regex pattern for HTML img tags with base64 data
    // Matches: <img src="data:image/type;base64,..." alt="..." />
    [GeneratedRegex(@"<img\s+[^>]*src\s*=\s*[""']data:image/([^;]+);base64,([A-Za-z0-9+/=]+)[""'][^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex Base64HtmlImageRegex();

    // Regex pattern for bare data URLs (not in markdown or HTML)
    // Matches: data:image/type;base64,... (standalone or within parentheses)
    [GeneratedRegex(@"(?<!\]\()(?<!\[)(?<![""'])data:image/([^;]+);base64,([A-Za-z0-9+/=]+)", RegexOptions.Compiled)]
    private static partial Regex BareBase64ImageRegex();

    // Combined pattern for quick detection
    [GeneratedRegex(@"data:image/[^;]+;base64,", RegexOptions.Compiled)]
    private static partial Regex QuickDetectionRegex();

    public ImageExtractionService(ILogger<ImageExtractionService>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<ImageExtractionResult> ExtractImagesAsync(
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var result = new ImageExtractionResult
        {
            CleanedContent = content,
            ExtractedImages = new List<ExtractedImageData>()
        };

        if (string.IsNullOrWhiteSpace(content))
        {
            return Task.FromResult(result);
        }

        // Collect all matches with their positions and types
        var allMatches = new List<(Match Match, string Type, int OriginalIndex)>();

        // Collect markdown image matches
        foreach (Match match in Base64MarkdownImageRegex().Matches(content))
        {
            allMatches.Add((match, "markdown", match.Index));
        }

        // Collect HTML image matches
        foreach (Match match in Base64HtmlImageRegex().Matches(content))
        {
            // Check if this HTML match overlaps with any markdown match
            var overlapsWithMarkdown = allMatches.Any(m =>
                m.Type == "markdown" &&
                match.Index >= m.Match.Index &&
                match.Index < m.Match.Index + m.Match.Length);

            if (!overlapsWithMarkdown)
            {
                allMatches.Add((match, "html", match.Index));
            }
        }

        // Collect bare base64 matches (checking against already collected matches)
        foreach (Match match in BareBase64ImageRegex().Matches(content))
        {
            // Check if this bare match overlaps with any existing match
            var overlapsWithExisting = allMatches.Any(m =>
                match.Index >= m.Match.Index &&
                match.Index < m.Match.Index + m.Match.Length);

            if (!overlapsWithExisting)
            {
                allMatches.Add((match, "bare", match.Index));
            }
        }

        // Sort by position (ascending) for correct image indexing
        allMatches = allMatches.OrderBy(m => m.OriginalIndex).ToList();

        // Process matches in reverse order to preserve positions
        var processedContent = content;
        var imageIndex = 0;

        // First pass: extract image data and assign indices in order
        var processedMatches = new List<(Match Match, string Type, int ImageIndex, ExtractedImageData? Data)>();

        foreach (var (match, type, _) in allMatches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                string? altText = null;
                string imageType;
                string base64Data;

                switch (type)
                {
                    case "markdown":
                        altText = match.Groups[1].Value;
                        if (string.IsNullOrEmpty(altText)) altText = null;
                        imageType = match.Groups[2].Value;
                        base64Data = match.Groups[3].Value;
                        break;
                    case "html":
                        imageType = match.Groups[1].Value;
                        base64Data = match.Groups[2].Value;
                        var altMatch = Regex.Match(match.Value, @"alt\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
                        altText = altMatch.Success ? altMatch.Groups[1].Value : null;
                        break;
                    case "bare":
                    default:
                        imageType = match.Groups[1].Value;
                        base64Data = match.Groups[2].Value;
                        break;
                }

                var imageData = Convert.FromBase64String(base64Data);
                var mimeType = $"image/{imageType}";
                var placeholder = $"[Image {imageIndex + 1}]";

                var extractedData = new ExtractedImageData
                {
                    PositionIndex = imageIndex,
                    MimeType = mimeType,
                    Data = imageData,
                    AltText = altText,
                    OriginalMarkdown = match.Value,
                    Placeholder = placeholder
                };

                result.ExtractedImages.Add(extractedData);
                processedMatches.Add((match, type, imageIndex, extractedData));
                imageIndex++;

                if (_logger is not null) LogExtractedImage(_logger, type, imageIndex, mimeType, imageData.Length);
            }
            catch (FormatException ex)
            {
                if (_logger is not null) LogDecodeFailure(_logger, ex, type, match.Index);
                processedMatches.Add((match, type, -1, null));
            }
        }

        // Second pass: replace in reverse order to preserve positions
        foreach (var (match, _, idx, data) in processedMatches.OrderByDescending(m => m.Match.Index))
        {
            if (data != null)
            {
                processedContent = processedContent.Remove(match.Index, match.Length)
                    .Insert(match.Index, data.Placeholder);
            }
        }

        result.CleanedContent = processedContent;

        if (_logger is not null)
            LogExtractionComplete(_logger, result.ExtractedImages.Count, result.TotalImageBytes, content.Length, processedContent.Length);

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public async Task<ImageProcessingResult> ExtractAndStoreAsync(
        string documentId,
        string content,
        IImageStore imageStore,
        string placeholderFormat = "[Image: {id}]",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(imageStore);

        var result = new ImageProcessingResult
        {
            OriginalContent = content,
            ProcessedContent = content,
            StoredImages = new List<ExtractedImage>(),
            Errors = new List<ImageProcessingError>()
        };

        // First, extract all images
        var extractionResult = await ExtractImagesAsync(content, cancellationToken);

        if (!extractionResult.HasImages)
        {
            return result;
        }

        var processedContent = content;

        foreach (var extractedData in extractionResult.ExtractedImages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Create the extracted image entity
                var extractedImage = ExtractedImage.Create(
                    documentId,
                    extractedData.MimeType,
                    extractedData.Data,
                    extractedData.PositionIndex,
                    chunkId: null,
                    altText: extractedData.AltText);

                // Store the image
                var storagePath = await imageStore.StoreAsync(
                    extractedImage.Id,
                    documentId,
                    extractedData.Data,
                    extractedData.MimeType,
                    cancellationToken);

                extractedImage.StoragePath = storagePath;

                // Generate placeholder with ID
                var placeholder = placeholderFormat
                    .Replace("{id}", extractedImage.Id)
                    .Replace("{url}", imageStore.GetPublicUrl(storagePath) ?? storagePath)
                    .Replace("{index}", (extractedData.PositionIndex + 1).ToString(CultureInfo.InvariantCulture))
                    .Replace("{alt}", extractedData.AltText ?? "Image");

                // Replace original base64 with placeholder
                processedContent = processedContent.Replace(extractedData.OriginalMarkdown, placeholder);

                result.StoredImages.Add(extractedImage);

                if (_logger is not null) LogStoredImage(_logger, extractedImage.Id, documentId, storagePath);
            }
            catch (Exception ex)
            {
                result.Errors.Add(new ImageProcessingError
                {
                    ImageIndex = extractedData.PositionIndex,
                    Message = $"Failed to store image: {ex.Message}",
                    Exception = ex,
                    ContentPosition = content.IndexOf(extractedData.OriginalMarkdown, StringComparison.Ordinal)
                });

                if (_logger is not null) LogStoreImageFailed(_logger, ex, extractedData.PositionIndex, documentId);
            }
        }

        result.ProcessedContent = processedContent;

        if (_logger is not null) LogProcessingComplete(_logger, result.StoredImages.Count, documentId, result.Errors.Count);

        return result;
    }

    /// <inheritdoc />
    public bool HasEmbeddedImages(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        return QuickDetectionRegex().IsMatch(content);
    }

    /// <inheritdoc />
    public int CountEmbeddedImages(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }

        var count = 0;
        count += Base64MarkdownImageRegex().Count(content);
        count += Base64HtmlImageRegex().Count(content);

        // Count bare base64 images, but exclude those already matched by markdown regex
        var processedContent = content;
        foreach (Match match in Base64MarkdownImageRegex().Matches(content))
        {
            processedContent = processedContent.Replace(match.Value, "");
        }
        count += BareBase64ImageRegex().Count(processedContent);

        return count;
    }

    /// <inheritdoc />
    public long EstimateEmbeddedImageSize(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }

        long totalSize = 0;

        // Process markdown images
        foreach (Match match in Base64MarkdownImageRegex().Matches(content))
        {
            var base64Data = match.Groups[3].Value;
            // Base64 encoding increases size by ~33%, so actual size is approximately base64_length * 3/4
            totalSize += (long)(base64Data.Length * 0.75);
        }

        // Process HTML images
        foreach (Match match in Base64HtmlImageRegex().Matches(content))
        {
            var base64Data = match.Groups[2].Value;
            totalSize += (long)(base64Data.Length * 0.75);
        }

        // Process bare base64 images (after removing markdown matches to avoid double counting)
        var processedContent = content;
        foreach (Match match in Base64MarkdownImageRegex().Matches(content))
        {
            processedContent = processedContent.Replace(match.Value, "");
        }
        foreach (Match match in BareBase64ImageRegex().Matches(processedContent))
        {
            var base64Data = match.Groups[2].Value;
            totalSize += (long)(base64Data.Length * 0.75);
        }

        return totalSize;
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Extracted {Type} image {Index}: {MimeType}, {Size} bytes")]
    private static partial void LogExtractedImage(ILogger logger, string type, int index, string mimeType, int size);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to decode base64 {Type} image at position {Position}")]
    private static partial void LogDecodeFailure(ILogger logger, Exception exception, string type, int position);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted {Count} images, total size: {Size} bytes, content reduced from {Original} to {Cleaned} chars")]
    private static partial void LogExtractionComplete(ILogger logger, int count, long size, int original, int cleaned);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored image {ImageId} for document {DocumentId} at {StoragePath}")]
    private static partial void LogStoredImage(ILogger logger, string imageId, string documentId, string storagePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to store image {Index} for document {DocumentId}")]
    private static partial void LogStoreImageFailed(ILogger logger, Exception exception, int index, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processed {Count} images for document {DocumentId}, {Errors} errors")]
    private static partial void LogProcessingComplete(ILogger logger, int count, string documentId, int errors);

    #endregion
}
