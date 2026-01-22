using System.Diagnostics;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Enums;
using FluxIndex.Extensions.FileVault.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// Pipeline service for processing vault entries.
/// Simplified flow: Source → Extracted → Memorized (chunks stored in DB only).
/// </summary>
public sealed class VaultPipeline : IVaultPipeline
{
    private readonly IGitService _git;
    private readonly IContentHasher _hasher;
    private readonly IVaultStorageService _storage;
    private readonly ILogger<VaultPipeline> _logger;

    // Integration services (optional)
    private readonly IExtractor? _extractor;
    private readonly IChunker? _chunker;
    private readonly IVectorStore? _vectorStore;
    private readonly IEmbeddingService? _embeddingService;

    public VaultPipeline(
        IGitService git,
        IContentHasher hasher,
        IVaultStorageService storage,
        ILogger<VaultPipeline> logger,
        IExtractor? extractor = null,
        IChunker? chunker = null,
        IVectorStore? vectorStore = null,
        IEmbeddingService? embeddingService = null)
    {
        _git = git ?? throw new ArgumentNullException(nameof(git));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _extractor = extractor;
        _chunker = chunker;
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
    }

    public async Task<MemorizeResult> MemorizeAsync(VaultEntry entry, MemorizeOptions? options = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        options ??= new MemorizeOptions();

        try
        {
            _logger.LogInformation("Starting memorize for {SourcePath}", entry.SourcePath);

            // Step 1: Backup user content if preserving (for re-memorize scenarios)
            string? existingQaContent = null;
            string? existingAppendText = null;

            if (_storage.EntryStorageExists(entry))
            {
                var vaultContent = await _storage.GetAllVaultContentAsync(entry, ct);

                if (options.PreserveQaContent && !string.IsNullOrWhiteSpace(vaultContent.QaContent))
                {
                    existingQaContent = vaultContent.QaContent;
                    _logger.LogDebug("Backing up existing QA content ({Length} chars)", existingQaContent.Length);
                }

                if (options.PreserveAppendText && !string.IsNullOrWhiteSpace(vaultContent.AppendText))
                {
                    existingAppendText = vaultContent.AppendText;
                    _logger.LogDebug("Backing up existing append-text ({Length} chars)", existingAppendText.Length);
                }
            }

            // Step 2: Initialize entry storage if needed
            if (!_storage.EntryStorageExists(entry))
            {
                await _storage.InitializeEntryAsync(entry, ct);
            }

            // Step 3: Extract content from source file → extracted.md
            await ExtractAsync(entry, ct);

            // Step 4: Refine content → vault/refined.md
            await RefineAsync(entry, ct);

            // Step 5: Restore preserved user content
            if (!string.IsNullOrWhiteSpace(existingQaContent))
            {
                await _storage.StoreQaContentAsync(entry, existingQaContent, ct);
                _logger.LogDebug("Restored QA content");
            }

            if (!string.IsNullOrWhiteSpace(existingAppendText))
            {
                await _storage.StoreAppendTextAsync(entry, existingAppendText, ct);
                _logger.LogDebug("Restored append-text content");
            }

            // Step 6: Chunk and index (shared with RefreshAsync)
            var result = await ChunkAndIndexAsync(entry, options, ct);

            // Step 7: Git commit
            string? commitHash = null;
            if (!options.SkipCommit)
            {
                var message = options.CommitMessage ?? $"memorize: {result.ChunkCount} chunks indexed";
                commitHash = await _git.CommitAsync(entry.VaultPath, message, ct);
            }

            // Step 8: Update entry state
            entry.MarkMemorized(result.ChunkCount);
            entry.MarkInSync(); // Set sync status to InSync after successful memorize
            entry.SaveMetadata();

            sw.Stop();
            _logger.LogInformation(
                "Memorize completed for {SourcePath}: {ChunkCount} chunks in {Duration:F2}s",
                entry.SourcePath,
                result.ChunkCount,
                sw.Elapsed.TotalSeconds);

            return MemorizeResult.Succeeded(result.ChunkCount, result.ContentLength, sw.Elapsed, commitHash);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Memorize failed for {SourcePath}", entry.SourcePath);
            entry.MarkError(ex.Message);
            entry.SaveMetadata();
            return MemorizeResult.Failed(ex.Message, sw.Elapsed);
        }
    }

    public async Task<MemorizeResult> RefreshAsync(VaultEntry entry, MemorizeOptions? options = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        options ??= new MemorizeOptions();

        try
        {
            _logger.LogInformation("Starting refresh for {SourcePath}", entry.SourcePath);

            // Verify that extracted content exists
            if (!entry.RefinedExists)
            {
                throw new InvalidOperationException($"No refined content found at {entry.RefinedMdPath}. Run memorize first.");
            }

            // Remove existing chunks from vector store before re-indexing
            await RemoveAsync(entry, ct);

            // Chunk and index vault content
            var result = await ChunkAndIndexAsync(entry, options, ct);

            // Git commit
            string? commitHash = null;
            if (!options.SkipCommit)
            {
                var message = options.CommitMessage ?? $"refresh: {result.ChunkCount} chunks re-indexed";
                commitHash = await _git.CommitAsync(entry.VaultPath, message, ct);
            }

            // Update entry state
            entry.MarkMemorized(result.ChunkCount);
            entry.MarkInSync(); // Set sync status to InSync after successful refresh
            entry.SaveMetadata();

            sw.Stop();
            _logger.LogInformation(
                "Refresh completed for {SourcePath}: {ChunkCount} chunks in {Duration:F2}s",
                entry.SourcePath,
                result.ChunkCount,
                sw.Elapsed.TotalSeconds);

            return MemorizeResult.Succeeded(result.ChunkCount, result.ContentLength, sw.Elapsed, commitHash);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Refresh failed for {SourcePath}", entry.SourcePath);
            entry.MarkError(ex.Message);
            entry.SaveMetadata();
            return MemorizeResult.Failed(ex.Message, sw.Elapsed);
        }
    }

    public async Task ExtractAsync(VaultEntry entry, CancellationToken ct = default)
    {
        _logger.LogInformation("Extracting content from {SourcePath}", entry.SourcePath);

        // Calculate source content hash
        var contentHash = await _hasher.ComputeHashAsync(entry.SourcePath, ct);

        // Extract content
        string extractedContent;

        if (_extractor != null)
        {
            var result = await _extractor.ExtractAsync(entry.SourcePath, ct);
            extractedContent = result.Content;

            // Store images if any - preserve original IDs from FileFlux
            if (result.Images?.Count > 0)
            {
                var images = result.Images.Select(kvp => new ImageArtifact
                {
                    // Extract ID from key (e.g., "img_000.png" → "img_000")
                    Id = Path.GetFileNameWithoutExtension(kvp.Key),
                    Data = kvp.Value,
                    ContentType = GuessContentType(kvp.Key)
                });
                await _storage.StoreImagesAsync(entry, images, ct);
            }
        }
        else
        {
            extractedContent = await ExtractFallbackAsync(entry.SourcePath, ct);
        }

        // Store raw extracted content (not git-tracked)
        await _storage.StoreExtractedContentAsync(entry, extractedContent, ct);

        // Update entry to Extracted stage
        entry.MarkExtracted(contentHash);
        entry.SaveMetadata();

        _logger.LogInformation("Extracted {Length} chars to {Path}", extractedContent.Length, entry.ExtractedMdPath);
    }

    public async Task RefineAsync(VaultEntry entry, CancellationToken ct = default)
    {
        _logger.LogInformation("Refining content for {SourcePath}", entry.SourcePath);

        // Get extracted content
        var extractedContent = await _storage.GetExtractedContentAsync(entry, ct);
        if (string.IsNullOrWhiteSpace(extractedContent))
        {
            throw new InvalidOperationException($"No extracted content found at {entry.ExtractedMdPath}. Run extract first.");
        }

        // For now, refined content is the same as extracted content
        // In the future, this is where LLM refinement and image description injection happens
        // via IImageDescriptionService (implemented by consumer apps)
        var refinedContent = extractedContent;

        // Store refined content (git-tracked)
        await _storage.StoreRefinedContentAsync(entry, refinedContent, ct);

        // Update entry to Refined stage
        entry.MarkRefined();
        entry.SaveMetadata();

        _logger.LogInformation("Refined {Length} chars to {Path}", refinedContent.Length, entry.RefinedMdPath);
    }

    public async Task RemoveAsync(VaultEntry entry, CancellationToken ct = default)
    {
        if (_vectorStore == null)
        {
            _logger.LogWarning("No vector store configured, skipping removal");
            return;
        }

        // Delete by document ID (filepath hash)
        var documentId = entry.FilepathHash;
        await _vectorStore.DeleteByDocumentIdAsync(documentId, ct);

        _logger.LogInformation("Removed chunks for document {DocumentId}", documentId);
    }

    private async Task<(int ChunkCount, int ContentLength)> ChunkAndIndexAsync(
        VaultEntry entry,
        MemorizeOptions options,
        CancellationToken ct)
    {
        // Get all vault content (refined.md + append-text.md + qa.md)
        var vaultContent = await _storage.GetAllVaultContentAsync(entry, ct);
        var combinedContent = vaultContent.GetCombinedContent();

        if (string.IsNullOrWhiteSpace(combinedContent))
        {
            _logger.LogWarning("No content to index for {SourcePath}", entry.SourcePath);
            return (0, 0);
        }

        // Chunk the content
        IReadOnlyList<string> chunks;

        if (_chunker != null)
        {
            var chunkingOptions = new ChunkingOptions
            {
                MaxChunkSize = options.MaxChunkSize,
                OverlapSize = options.OverlapSize,
                Strategy = options.Strategy,
                Language = options.Language
            };
            chunks = await _chunker.ChunkAsync(combinedContent, chunkingOptions, ct);
        }
        else
        {
            chunks = ChunkFallback(combinedContent, options.MaxChunkSize);
        }

        _logger.LogDebug("Created {ChunkCount} chunks from {ContentLength} chars", chunks.Count, combinedContent.Length);

        // Index to vector store
        if (_vectorStore != null && _embeddingService != null)
        {
            await IndexChunksAsync(entry, chunks, ct);
        }
        else
        {
            _logger.LogWarning("No vector store or embedding service configured, skipping indexing");
        }

        return (chunks.Count, combinedContent.Length);
    }

    private async Task IndexChunksAsync(VaultEntry entry, IReadOnlyList<string> chunks, CancellationToken ct)
    {
        var documentId = entry.FilepathHash;

        // Generate embeddings
        var embeddings = await _embeddingService!.GenerateEmbeddingsBatchAsync(chunks, ct);
        var embeddingList = embeddings.ToList();

        if (embeddingList.Count != chunks.Count)
        {
            throw new InvalidOperationException(
                $"Embedding count mismatch: expected {chunks.Count}, got {embeddingList.Count}");
        }

        // Create document chunks
        var documentChunks = new List<DocumentChunk>();
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = DocumentChunk.Create(
                documentId: documentId,
                content: chunks[i],
                chunkIndex: i,
                totalChunks: chunks.Count);

            chunk.SetEmbedding(embeddingList[i]);

            // Add metadata
            chunk.Metadata ??= new Dictionary<string, object>();
            chunk.Metadata["source_path"] = entry.SourcePath;
            chunk.Metadata["filepath_hash"] = entry.FilepathHash;
            chunk.Metadata["file_name"] = entry.FileName;

            documentChunks.Add(chunk);
        }

        // Store in vector store
        var storedIds = await _vectorStore!.StoreBatchAsync(documentChunks, ct);
        _logger.LogInformation("Indexed {Count} chunks for {DocumentId}", storedIds.Count(), documentId);
    }

    private static async Task<string> ExtractFallbackAsync(string sourcePath, CancellationToken ct)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();

        // Simple text extraction for supported formats
        if (extension is ".txt" or ".md" or ".json" or ".xml" or ".yaml" or ".yml" or ".csv")
        {
            return await File.ReadAllTextAsync(sourcePath, ct);
        }

        return $"[Content extraction required for {extension} files. Install FileFlux for full support.]";
    }

    private static IReadOnlyList<string> ChunkFallback(string content, int maxChunkSize)
    {
        var chunks = new List<string>();
        var lines = content.Split('\n');
        var currentChunk = new List<string>();
        var currentLength = 0;

        foreach (var line in lines)
        {
            var lineLength = line.Length;

            if (currentLength + lineLength > maxChunkSize && currentChunk.Count > 0)
            {
                chunks.Add(string.Join('\n', currentChunk));
                currentChunk.Clear();
                currentLength = 0;
            }

            currentChunk.Add(line);
            currentLength += lineLength + 1;
        }

        if (currentChunk.Count > 0)
        {
            chunks.Add(string.Join('\n', currentChunk));
        }

        return chunks;
    }

    private static string GuessContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }
}

/// <summary>
/// Interface for content extraction (FileFlux integration).
/// </summary>
public interface IExtractor
{
    Task<ExtractionResult> ExtractAsync(string sourcePath, CancellationToken ct = default);
}

/// <summary>
/// Result of content extraction.
/// </summary>
public sealed class ExtractionResult
{
    public string Content { get; init; } = "";
    public Dictionary<string, byte[]>? Images { get; init; }
}

/// <summary>
/// Interface for content chunking (FileFlux integration).
/// </summary>
public interface IChunker
{
    Task<IReadOnlyList<string>> ChunkAsync(string content, ChunkingOptions options, CancellationToken ct = default);
}

/// <summary>
/// Options for chunking.
/// </summary>
public sealed class ChunkingOptions
{
    public int MaxChunkSize { get; set; } = 1024;
    public int OverlapSize { get; set; } = 128;
    public string Strategy { get; set; } = "Auto";
    public string? Language { get; set; }
}
