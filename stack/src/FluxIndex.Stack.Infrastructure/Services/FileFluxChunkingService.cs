using System.Text;
using FileFlux;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
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
/// </summary>
public class FileFluxChunkingService : IChunkingService
{
    private readonly IDocumentProcessor _documentProcessor;
    private readonly ILanguageProfileProvider _languageProfileProvider;
    private readonly ILogger<FileFluxChunkingService> _logger;
    private readonly FileFluxChunkingOptions _defaultOptions;
    private readonly HtmlDocumentReader _htmlReader;

    public FileFluxChunkingService(
        IDocumentProcessor documentProcessor,
        ILanguageProfileProvider languageProfileProvider,
        ILogger<FileFluxChunkingService> logger,
        IOptions<FileFluxChunkingConfiguration>? options = null)
    {
        _documentProcessor = documentProcessor;
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
        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<StackDocumentChunk>();
        }

        options ??= new StackChunkingOptions();

        // Pre-process HTML content to extract clean text
        var processedContent = await PreprocessContentAsync(content, documentId, cancellationToken);

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

                var chunks = new List<StackDocumentChunk>();
                var chunkIndex = 0;

                // Use FileFlux streaming API for memory-efficient processing
                await foreach (var fileFluxChunk in _documentProcessor.ProcessStreamAsync(
                    tempPath, fileFluxOptions, cancellationToken))
                {
                    var chunk = ConvertToStackChunk(fileFluxChunk, documentId, chunkIndex++, language);
                    chunks.Add(chunk);
                }

                _logger.LogInformation("Document {DocumentId} chunked into {ChunkCount} segments using {Strategy} strategy (FileFlux)",
                    documentId, chunks.Count, options.Strategy);

                return chunks;
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
            return FallbackChunking(processedContent, documentId, options);
        }
    }

    /// <summary>
    /// Pre-process content based on detected content type.
    /// HTML content is converted to clean Markdown text.
    /// FileFlux v0.7+ automatically extracts base64 images and replaces them with placeholders.
    /// </summary>
    private async Task<string> PreprocessContentAsync(string content, Guid documentId, CancellationToken cancellationToken)
    {
        // Check if content looks like HTML
        if (!IsHtmlContent(content))
        {
            return content;
        }

        _logger.LogInformation("Document {DocumentId} detected as HTML, preprocessing with HtmlDocumentReader", documentId);

        try
        {
            // Use FileFlux HtmlDocumentReader to extract clean text
            // FileFlux v0.7+ automatically extracts base64 images and replaces with placeholders (embedded:img_xxx)
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            var rawContent = await _htmlReader.ExtractAsync(stream, "document.html", cancellationToken);

            var extractedText = rawContent.Text;

            var originalLength = content.Length;
            var extractedLength = extractedText.Length;
            var reductionPercent = (1 - (double)extractedLength / originalLength) * 100;

            // Log image extraction info if any embedded images were found (FileFlux v0.7+)
            if (rawContent.Images.Count > 0)
            {
                var embeddedCount = rawContent.Images.Count(i => i.Data != null);
                var totalImageSize = rawContent.Images.Where(i => i.Data != null).Sum(i => i.Data!.Length);
                _logger.LogInformation(
                    "Document {DocumentId}: Extracted {EmbeddedCount} embedded images ({TotalSize:N0} bytes)",
                    documentId, embeddedCount, totalImageSize);
            }

            _logger.LogInformation(
                "HTML preprocessing complete for {DocumentId}: {OriginalLength} -> {ExtractedLength} chars ({ReductionPercent:F1}% reduction)",
                documentId, originalLength, extractedLength, reductionPercent);

            return extractedText;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HTML preprocessing failed for {DocumentId}, using original content", documentId);
            return content;
        }
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

            // Find natural break point
            if (endPosition < content.Length)
            {
                var breakPoint = FindBreakPoint(content, position, endPosition);
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

    private static int FindBreakPoint(string content, int start, int end)
    {
        var sentenceEnds = new[] { ". ", "! ", "? ", ".\n", "!\n", "?\n" };

        foreach (var ending in sentenceEnds)
        {
            var pos = content.LastIndexOf(ending, end - 1, end - start);
            if (pos > start)
            {
                return pos + ending.Length;
            }
        }

        // Try paragraph break
        var paraBreak = content.LastIndexOf("\n\n", end - 1, end - start);
        if (paraBreak > start)
        {
            return paraBreak + 2;
        }

        // Try word boundary
        var spacePos = content.LastIndexOf(' ', end - 1, end - start);
        if (spacePos > start)
        {
            return spacePos + 1;
        }

        return end;
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
