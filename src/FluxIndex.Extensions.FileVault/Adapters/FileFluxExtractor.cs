using FileFlux;
using FileFlux.Core;
using FluxIndex.Extensions.FileVault.Services;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Extensions.FileVault.Adapters;

/// <summary>
/// FileFlux adapter for content extraction.
/// Bridges IExtractor to FileFlux's IDocumentProcessorFactory.
/// </summary>
public sealed class FileFluxExtractor : IExtractor
{
    private readonly IDocumentProcessorFactory _processorFactory;
    private readonly ILogger<FileFluxExtractor> _logger;

    public FileFluxExtractor(
        IDocumentProcessorFactory processorFactory,
        ILogger<FileFluxExtractor> logger)
    {
        _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ExtractionResult> ExtractAsync(string sourcePath, CancellationToken ct = default)
    {
        _logger.LogDebug("Extracting content from {SourcePath}", sourcePath);

        try
        {
            await using var processor = _processorFactory.Create(sourcePath);

            // Process with minimal chunking to get raw extracted text
            // FileFlux requires a strategy - use Auto with large chunk size to get full content
            var options = new ProcessingOptions
            {
                Chunking = new FileFlux.Core.ChunkingOptions
                {
                    Strategy = ChunkingStrategies.Auto,
                    MaxChunkSize = int.MaxValue // Get full content as single chunk
                }
            };

            await processor.ProcessAsync(options, ct);

            var result = processor.Result;

            // Get content from chunks (FileFlux stores extracted text in chunks)
            var content = result.Chunks?.Count > 0
                ? string.Join("\n\n", result.Chunks.Select(c => c.Content))
                : string.Empty;

            // Extract images from RawContent if available
            Dictionary<string, byte[]>? images = null;
            if (result.Raw?.Images?.Count > 0)
            {
                images = [];
                foreach (var (img, idx) in result.Raw.Images.Select((img, idx) => (img, idx)))
                {
                    if (img.Data is { Length: > 0 })
                    {
                        var key = !string.IsNullOrEmpty(img.Id) ? img.Id : $"img_{idx:D3}";
                        images[key] = img.Data;
                    }
                }
            }

            _logger.LogInformation(
                "Extracted {ContentLength} chars and {ImageCount} images from {SourcePath}",
                content.Length,
                images?.Count ?? 0,
                sourcePath);

            return new ExtractionResult
            {
                Content = content,
                Images = images?.Count > 0 ? images : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract content from {SourcePath}", sourcePath);
            throw;
        }
    }
}
