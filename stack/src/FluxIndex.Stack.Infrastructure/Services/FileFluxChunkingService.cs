using System.Text;
using FileFlux;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using FluxIndex.Core.Services;
using FluxIndex.Stack.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackChunkingOptions = FluxIndex.Stack.Application.Interfaces.Services.ChunkingOptions;
using FileFluxChunkingOptions = FileFlux.Core.ChunkingOptions;
using StackDocumentChunk = FluxIndex.Stack.Domain.Entities.DocumentChunk;
using FileFluxChunk = FileFlux.Core.DocumentChunk;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// FileFlux-based implementation of IChunkingService.
/// Provides intelligent language-aware chunking using FileFlux library.
/// Uses FileFlux 0.9.x factory pattern for stateful document processing.
/// </summary>
public class FileFluxChunkingService : IChunkingService
{
    private readonly IDocumentProcessorFactory _processorFactory;
    private readonly ILanguageProfileProvider _languageProfileProvider;
    private readonly ILogger<FileFluxChunkingService> _logger;
    private readonly FileFluxChunkingOptions _defaultOptions;
    private readonly HtmlDocumentReader _htmlReader;

    public FileFluxChunkingService(
        IDocumentProcessorFactory processorFactory,
        ILanguageProfileProvider languageProfileProvider,
        ILogger<FileFluxChunkingService> logger,
        IOptions<FileFluxChunkingConfiguration>? options = null)
    {
        _processorFactory = processorFactory;
        _languageProfileProvider = languageProfileProvider;
        _logger = logger;
        _htmlReader = new HtmlDocumentReader();

        var config = options?.Value ?? new FileFluxChunkingConfiguration();
        _defaultOptions = new FileFluxChunkingOptions
        {
            Strategy = config.DefaultStrategy,
            MaxChunkSize = config.DefaultMaxChunkSize,
            OverlapSize = config.DefaultOverlapSize
        };
    }

    public async Task<List<StackDocumentChunk>> ChunkContentAsync(
        string content,
        Guid documentId,
        StackChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ChunkContentWithImagesAsync(content, documentId, options, cancellationToken);
        return result.Chunks;
    }

    public async Task<ChunkingResult> ChunkContentWithImagesAsync(
        string content,
        Guid documentId,
        StackChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ChunkingResult();

        if (string.IsNullOrWhiteSpace(content))
        {
            return result;
        }

        options ??= new StackChunkingOptions();

        // Pre-process HTML content to extract clean text and images
        var (processedContent, extractedImages) = await PreprocessContentWithImagesAsync(content, documentId, cancellationToken);
        result.Images = extractedImages;

        // Detect language if not specified
        var language = options.Language ?? DetectLanguage(processedContent);

        _logger.LogDebug("Chunking document {DocumentId} with strategy {Strategy}, language {Language}",
            documentId, options.Strategy, language ?? "auto");

        // Configure FileFlux chunking options
        var fileFluxOptions = new FileFluxChunkingOptions
        {
            Strategy = options.Strategy,
            MaxChunkSize = options.MaxChunkSize,
            OverlapSize = options.OverlapSize
        };

        // Add language configuration
        if (!string.IsNullOrEmpty(language))
        {
            fileFluxOptions.CustomProperties["language"] = language;
        }

        try
        {
            // FileFlux works with file paths - create temp file for processing
            var tempPath = Path.Combine(Path.GetTempPath(), $"fluxindex_{documentId}_{Guid.NewGuid():N}.txt");

            try
            {
                // Sanitize null bytes (0x00) which are invalid in PostgreSQL TEXT columns
                var sanitizedContent = SanitizeContent(processedContent);
                await File.WriteAllTextAsync(tempPath, sanitizedContent, cancellationToken);

                var chunkIndex = 0;

                // Use FileFlux 0.9.x factory pattern with streaming API for memory-efficient processing
                await using var processor = _processorFactory.Create(tempPath);
                var processingOptions = new FileFlux.Core.ProcessingOptions { Chunking = fileFluxOptions };
                await foreach (var fileFluxChunk in processor.ProcessStreamAsync(processingOptions, cancellationToken))
                {
                    var chunk = ConvertToStackChunk(fileFluxChunk, documentId, chunkIndex++, language);
                    result.Chunks.Add(chunk);
                }

                _logger.LogInformation("Document {DocumentId} chunked into {ChunkCount} segments with {ImageCount} images using {Strategy} strategy (FileFlux)",
                    documentId, result.Chunks.Count, result.Images.Count, options.Strategy);

                return result;
            }
            finally
            {
                // Clean up temp file
                try { File.Delete(tempPath); } catch { /* ignore cleanup errors */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FileFlux chunking failed for document {DocumentId}, falling back to simple chunking",
                documentId);

            // Fallback to simple chunking
            result.Chunks = FallbackChunking(processedContent, documentId, options);
            return result;
        }
    }

    /// <summary>
    /// Pre-process content based on detected content type.
    /// HTML content is converted to clean Markdown text and images are extracted.
    /// FileFlux v0.7+ automatically extracts base64 images and replaces them with placeholders.
    /// </summary>
    private async Task<(string Content, Dictionary<string, ExtractedImage> Images)> PreprocessContentWithImagesAsync(
        string content,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var images = new Dictionary<string, ExtractedImage>();

        // Check if content looks like HTML
        if (!IsHtmlContent(content))
        {
            return (content, images);
        }

        _logger.LogInformation("Document {DocumentId} detected as HTML, preprocessing with HtmlDocumentReader", documentId);

        try
        {
            // Use FileFlux HtmlDocumentReader to extract clean text
            // FileFlux v0.9+ automatically extracts base64 images and replaces with placeholders (embedded:img_xxx)
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            var rawContent = await _htmlReader.ExtractAsync(stream, "document.html", null, cancellationToken);

            var extractedText = rawContent.Text;

            var originalLength = content.Length;
            var extractedLength = extractedText.Length;
            var reductionPercent = (1 - (double)extractedLength / originalLength) * 100;

            // Extract images from RawContent.Images (FileFlux v0.7+)
            if (rawContent.Images.Count > 0)
            {
                var embeddedCount = 0;
                var totalImageSize = 0L;

                foreach (var img in rawContent.Images)
                {
                    if (img.Data != null && img.Data.Length > 0)
                    {
                        // Use FileFlux ImageInfo.Id (e.g., "img_001")
                        var imageId = !string.IsNullOrEmpty(img.Id) ? img.Id : $"img_{embeddedCount:D3}";

                        // Use MimeType from ImageInfo, or detect from binary data
                        var contentType = !string.IsNullOrEmpty(img.MimeType)
                            ? img.MimeType
                            : DetermineImageContentType(img.Data, null);

                        images[imageId] = new ExtractedImage
                        {
                            Data = img.Data,
                            ContentType = contentType,
                            Description = img.Caption
                        };

                        totalImageSize += img.Data.Length;
                        embeddedCount++;
                    }
                }

                _logger.LogInformation(
                    "Document {DocumentId}: Extracted {EmbeddedCount} embedded images ({TotalSize:N0} bytes)",
                    documentId, embeddedCount, totalImageSize);
            }

            _logger.LogInformation(
                "HTML preprocessing complete for {DocumentId}: {OriginalLength} -> {ExtractedLength} chars ({ReductionPercent:F1}% reduction)",
                documentId, originalLength, extractedLength, reductionPercent);

            return (extractedText, images);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HTML preprocessing failed for {DocumentId}, using original content", documentId);
            return (content, images);
        }
    }

    /// <summary>
    /// Determines the image content type from binary data or filename.
    /// </summary>
    private static string DetermineImageContentType(byte[] data, string? name)
    {
        // Check magic bytes first
        if (data.Length >= 8)
        {
            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                return "image/png";

            // JPEG: FF D8 FF
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return "image/jpeg";

            // GIF: GIF87a or GIF89a
            if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46)
                return "image/gif";

            // WebP: RIFF....WEBP
            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
                data.Length >= 12 && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
                return "image/webp";

            // BMP: BM
            if (data[0] == 0x42 && data[1] == 0x4D)
                return "image/bmp";
        }

        // Fallback to extension if available
        if (!string.IsNullOrEmpty(name))
        {
            var ext = Path.GetExtension(name)?.ToLowerInvariant();
            return ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                ".svg" => "image/svg+xml",
                _ => "image/png"
            };
        }

        return "image/png"; // default
    }

    /// <summary>
    /// Check if content appears to be HTML based on common patterns.
    /// </summary>
    private static bool IsHtmlContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var trimmed = content.TrimStart();

        // Check for DOCTYPE or opening HTML tag
        if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check for common HTML structure patterns
        return trimmed.Contains("<head", StringComparison.OrdinalIgnoreCase) &&
               trimmed.Contains("<body", StringComparison.OrdinalIgnoreCase);
    }

    public string? DetectLanguage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            // Use FileFlux language profile provider for detection
            var profile = _languageProfileProvider.DetectAndGetProfile(content);
            _logger.LogDebug("Detected language: {Language} ({Script})",
                profile.LanguageCode, profile.ScriptCode);
            return profile.LanguageCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Language detection failed, using default");
            return _languageProfileProvider.DefaultProfile.LanguageCode;
        }
    }

    private StackDocumentChunk ConvertToStackChunk(
        FileFluxChunk fileFluxChunk,
        Guid documentId,
        int chunkIndex,
        string? language)
    {
        // Use FileFlux location info for positions
        var startPosition = fileFluxChunk.Location?.StartChar ?? 0;
        var endPosition = fileFluxChunk.Location?.EndChar ?? startPosition + fileFluxChunk.Content.Length;

        // Sanitize chunk content as additional safety measure
        var sanitizedChunkContent = SanitizeContent(fileFluxChunk.Content);

        var chunk = StackDocumentChunk.Create(
            documentId,
            chunkIndex,
            sanitizedChunkContent,
            startPosition,
            endPosition,
            fileFluxChunk.Tokens);

        // Build metadata from FileFlux chunk
        var metadata = new Dictionary<string, object>
        {
            ["ff_chunk_id"] = fileFluxChunk.Id,
            ["ff_strategy"] = fileFluxChunk.Strategy,
            ["ff_quality"] = fileFluxChunk.Quality,
            ["ff_importance"] = fileFluxChunk.Importance,
            ["ff_density"] = fileFluxChunk.Density
        };

        if (!string.IsNullOrEmpty(language))
        {
            metadata["language"] = language;
        }

        // Add heading path for hierarchical context
        if (fileFluxChunk.Location?.HeadingPath?.Count > 0)
        {
            metadata["heading_path"] = string.Join(" > ", fileFluxChunk.Location.HeadingPath);
        }

        // Add section info
        if (!string.IsNullOrEmpty(fileFluxChunk.Location?.Section))
        {
            metadata["section"] = fileFluxChunk.Location.Section;
        }

        // Add source metadata
        if (fileFluxChunk.SourceInfo != null)
        {
            if (fileFluxChunk.SourceInfo.WordCount > 0)
                metadata["word_count"] = fileFluxChunk.SourceInfo.WordCount;
            if (fileFluxChunk.SourceInfo.ChunkCount > 0)
                metadata["total_chunks"] = fileFluxChunk.SourceInfo.ChunkCount;
        }

        chunk.SetMetadata(metadata);

        return chunk;
    }

    private List<StackDocumentChunk> FallbackChunking(
        string content,
        Guid documentId,
        StackChunkingOptions options)
    {
        var chunks = new List<StackDocumentChunk>();
        var chunkIndex = 0;
        var position = 0;
        var chunkSize = options.MaxChunkSize;
        var overlap = options.OverlapSize;

        while (position < content.Length)
        {
            var endPosition = Math.Min(position + chunkSize, content.Length);

            // Find natural break point using Core utility
            if (endPosition < content.Length)
            {
                var breakPoint = SimpleChunkingService.FindNaturalBreakPoint(content, position, endPosition);
                if (breakPoint > position)
                {
                    endPosition = breakPoint;
                }
            }

            var chunkContent = content.Substring(position, endPosition - position).Trim();

            if (!string.IsNullOrWhiteSpace(chunkContent))
            {
                var chunk = StackDocumentChunk.Create(
                    documentId,
                    chunkIndex++,
                    chunkContent,
                    position,
                    endPosition,
                    EstimateTokenCount(chunkContent));

                chunk.SetMetadata(new Dictionary<string, object>
                {
                    ["strategy"] = "fallback",
                    ["language"] = options.Language ?? "unknown"
                });

                chunks.Add(chunk);
            }

            // Move with overlap
            var nextPosition = endPosition - overlap;
            position = nextPosition <= position ? endPosition : nextPosition;
        }

        return chunks;
    }

    private static int EstimateTokenCount(string text)
    {
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <summary>
    /// Removes null bytes (0x00) from text content.
    /// PostgreSQL TEXT columns reject null bytes as invalid UTF-8.
    /// </summary>
    private static string SanitizeContent(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        // Fast path: check if sanitization is needed
        if (!content.Contains('\0'))
            return content;

        return content.Replace("\0", string.Empty);
    }
}

/// <summary>
/// Configuration for FileFlux chunking service.
/// </summary>
public class FileFluxChunkingConfiguration
{
    /// <summary>
    /// Default chunking strategy.
    /// </summary>
    public string DefaultStrategy { get; set; } = ChunkingStrategies.Auto;

    /// <summary>
    /// Default maximum chunk size in characters.
    /// </summary>
    public int DefaultMaxChunkSize { get; set; } = 1024;

    /// <summary>
    /// Default overlap size in characters.
    /// </summary>
    public int DefaultOverlapSize { get; set; } = 128;

    /// <summary>
    /// Enable automatic language detection.
    /// </summary>
    public bool EnableLanguageDetection { get; set; } = true;
}
