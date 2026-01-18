using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Services;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Extensions.FileVault.Adapters;

/// <summary>
/// FluxIndex adapter for chunk memorization.
/// Bridges IMemorizer to FluxIndex's IVectorStore and IEmbeddingService.
/// </summary>
public sealed class FluxIndexMemorizer : IMemorizer
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<FluxIndexMemorizer> _logger;

    public FluxIndexMemorizer(
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        ILogger<FluxIndexMemorizer> logger)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task MemorizeAsync(VaultEntry entry, IReadOnlyList<string> chunks, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Memorizing {ChunkCount} chunks for {SourcePath}",
            chunks.Count,
            entry.SourcePath);

        var documentId = entry.SourceHash.Value;
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

            // Add metadata from vault entry using Metadata dictionary
            chunk.Metadata ??= new Dictionary<string, object>();
            chunk.Metadata["source_path"] = entry.SourcePath;
            chunk.Metadata["source_hash"] = entry.SourceHash.Value;
            chunk.Metadata["file_name"] = entry.FileName;

            documentChunks.Add(chunk);
        }

        // Store in vector store (batch operation)
        var storedIds = await _vectorStore.StoreBatchAsync(documentChunks, ct);
        var storedCount = storedIds.Count();

        _logger.LogInformation(
            "Memorized {StoredCount}/{TotalCount} chunks for document {DocumentId}",
            storedCount,
            chunks.Count,
            documentId);
    }
}
