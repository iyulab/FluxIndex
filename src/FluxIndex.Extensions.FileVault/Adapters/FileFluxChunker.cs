using FileFlux;
using FileFlux.Core;
using FluxIndex.Extensions.FileVault.Services;
using Microsoft.Extensions.Logging;
using FileFluxChunkingOptions = FileFlux.Core.ChunkingOptions;
using VaultChunkingOptions = FluxIndex.Extensions.FileVault.Services.ChunkingOptions;

namespace FluxIndex.Extensions.FileVault.Adapters;

/// <summary>
/// FileFlux adapter for content chunking.
/// Bridges IChunker to FileFlux's chunking capabilities.
/// </summary>
public sealed class FileFluxChunker : IChunker
{
    private readonly IDocumentProcessorFactory _processorFactory;
    private readonly ILogger<FileFluxChunker> _logger;

    public FileFluxChunker(
        IDocumentProcessorFactory processorFactory,
        ILogger<FileFluxChunker> logger)
    {
        _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<string>> ChunkAsync(
        string content,
        VaultChunkingOptions options,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Chunking content: {ContentLength} chars, Strategy={Strategy}, MaxSize={MaxSize}",
            content.Length,
            options.Strategy,
            options.MaxChunkSize);

        try
        {
            // Create a temporary in-memory document for chunking
            var tempPath = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempPath, content, ct);

                await using var processor = _processorFactory.Create(tempPath);

                var chunkingOptions = new FileFluxChunkingOptions
                {
                    Strategy = MapStrategy(options.Strategy),
                    MaxChunkSize = options.MaxChunkSize,
                    OverlapSize = options.OverlapSize
                };

                // Apply language if specified via CustomProperties
                if (!string.IsNullOrEmpty(options.Language))
                {
                    chunkingOptions.CustomProperties["language"] = options.Language;
                }

                var processingOptions = new ProcessingOptions
                {
                    Chunking = chunkingOptions
                };

                await processor.ProcessAsync(processingOptions, ct);

                var chunks = processor.Result.Chunks
                    .Select(c => c.Content)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();

                _logger.LogInformation(
                    "Created {ChunkCount} chunks from {ContentLength} chars",
                    chunks.Count,
                    content.Length);

                return chunks;
            }
            finally
            {
                // Cleanup temp file
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to chunk content");
            throw;
        }
    }

    private static string MapStrategy(string vaultStrategy) => vaultStrategy.ToLowerInvariant() switch
    {
        "auto" => ChunkingStrategies.Auto,
        "semantic" => ChunkingStrategies.Semantic,
        "paragraph" => ChunkingStrategies.Paragraph,
        "sentence" => ChunkingStrategies.Sentence,
        "token" => ChunkingStrategies.Token,
        "hierarchical" => ChunkingStrategies.Hierarchical,
        // Legacy aliases
        "intelligent" => ChunkingStrategies.Auto,
        "smart" => ChunkingStrategies.Semantic,
        "fixed" => ChunkingStrategies.Token,
        _ => ChunkingStrategies.Auto
    };
}
