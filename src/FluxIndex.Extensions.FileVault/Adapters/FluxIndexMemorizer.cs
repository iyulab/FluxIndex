using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Extensions.FileVault.Adapters;

/// <summary>
/// FluxIndex adapter for standalone chunk memorization.
/// Note: VaultPipeline now handles indexing internally. This adapter is for custom scenarios.
/// </summary>
public sealed partial class FluxIndexMemorizer
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVaultStorageService _storage;
    private readonly ILogger<FluxIndexMemorizer> _logger;

    public FluxIndexMemorizer(
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        IVaultStorageService storage,
        ILogger<FluxIndexMemorizer> logger)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Memorizes all vault content (refined.md + append-text.md + qa.md) for an entry.
    /// </summary>
    public async Task<int> MemorizeFromVaultAsync(VaultEntry entry, IReadOnlyList<string> chunks, CancellationToken ct = default)
    {
        LogMemorizing(_logger, chunks.Count, entry.SourcePath);

        // Use filepath hash as document ID
        var documentId = entry.FilepathHash;
        var documentChunks = new List<DocumentChunk>();

        // Generate embeddings for all chunks
        var embeddings = await _embeddingService.GenerateEmbeddingsBatchAsync(chunks, ct);
        var embeddingList = embeddings.ToList();

        if (embeddingList.Count != chunks.Count)
        {
            throw new InvalidOperationException(
                $"Embedding count mismatch: expected {chunks.Count}, got {embeddingList.Count}");
        }

        // Create DocumentChunk entities
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = DocumentChunk.Create(
                documentId: documentId,
                content: chunks[i],
                chunkIndex: i,
                totalChunks: chunks.Count);

            chunk.SetEmbedding(embeddingList[i]);

            // Add metadata from vault entry
            chunk.Metadata ??= new Dictionary<string, object>();
            chunk.Metadata["source_path"] = entry.SourcePath;
            chunk.Metadata["filepath_hash"] = entry.FilepathHash;
            chunk.Metadata["file_name"] = entry.FileName;

            documentChunks.Add(chunk);
        }

        // Store in vector store (batch operation)
        var storedIds = await _vectorStore.StoreBatchAsync(documentChunks, ct);
        var storedCount = storedIds.Count();

        LogMemorized(_logger, storedCount, chunks.Count, documentId);

        return storedCount;
    }

    /// <summary>
    /// Removes all chunks for an entry from the vector store.
    /// </summary>
    public async Task RemoveAsync(VaultEntry entry, CancellationToken ct = default)
    {
        var documentId = entry.FilepathHash;
        await _vectorStore.DeleteByDocumentIdAsync(documentId, ct);
        LogRemovedChunks(_logger, documentId);
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Memorizing {ChunkCount} chunks for {SourcePath}")]
    private static partial void LogMemorizing(ILogger logger, int chunkCount, string sourcePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Memorized {StoredCount}/{TotalCount} chunks for document {DocumentId}")]
    private static partial void LogMemorized(ILogger logger, int storedCount, int totalCount, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed chunks for document {DocumentId}")]
    private static partial void LogRemovedChunks(ILogger logger, string documentId);

    #endregion
}
