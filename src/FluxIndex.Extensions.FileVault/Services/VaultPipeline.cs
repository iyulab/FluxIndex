using System.Diagnostics;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Enums;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// Pipeline service for processing vault entries.
/// Simplified flow: Source → Extracted → Memorized (chunks stored in DB only).
/// </summary>
public sealed partial class VaultPipeline : IVaultPipeline
{
    /// <summary>
    /// Document file extensions suitable for vector embedding (Memorize).
    /// These formats contain natural language text that benefits from semantic search.
    /// </summary>
    /// <remarks>
    /// For code files, use file-read instead of Memorize.
    /// Code requires AST-based chunking for effective RAG, which is not yet supported.
    /// </remarks>
    public static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Text and documentation
        ".txt", ".md", ".markdown", ".rst", ".rtf", ".log",
        // Web content (natural language)
        ".html", ".htm",
        // Data formats (structured but searchable)
        ".json", ".xml", ".yaml", ".yml", ".csv", ".tsv",
        // Markup and templates
        ".tex", ".bib"
    };

    /// <summary>
    /// Source code and config file extensions that can be read but are NOT recommended for Memorize.
    /// These files should be accessed via file-read when needed, not vector embedding.
    /// </summary>
    /// <remarks>
    /// Reason: Standard text chunking breaks code semantics (functions split across chunks).
    /// Effective code RAG requires AST-based chunking (Tree-sitter) and code-specific embeddings.
    /// See: https://blog.lancedb.com/rag-codebase-1/
    /// </remarks>
    public static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Source code - C family
        ".cs", ".c", ".cpp", ".cc", ".cxx", ".h", ".hpp", ".hxx", ".m", ".mm",
        // Source code - JVM
        ".java", ".kt", ".kts", ".scala", ".groovy", ".gradle",
        // Source code - Web
        ".js", ".ts", ".jsx", ".tsx", ".mjs", ".cjs", ".vue", ".svelte",
        ".css", ".scss", ".sass", ".less",
        // Source code - Scripting
        ".py", ".pyw", ".rb", ".php", ".pl", ".pm", ".lua", ".r", ".jl",
        // Source code - Systems
        ".go", ".rs", ".swift", ".dart", ".zig", ".nim", ".v", ".odin",
        // Source code - Functional
        ".hs", ".fs", ".fsx", ".ml", ".mli", ".clj", ".cljs", ".ex", ".exs", ".erl", ".elm",
        // Shell and scripts
        ".sh", ".bash", ".zsh", ".fish", ".ps1", ".psm1", ".bat", ".cmd",
        // Build and config
        ".makefile", ".dockerfile", ".cmake", ".meson", ".ninja",
        ".toml", ".ini", ".cfg", ".conf",
        ".editorconfig", ".gitignore", ".gitattributes", ".dockerignore",
        // SQL and query
        ".sql", ".graphql", ".gql",
        // Schema and protocol
        ".proto", ".thrift", ".avsc", ".fbs",
        // Templates
        ".sty", ".cls", ".njk", ".ejs", ".hbs", ".mustache", ".liquid", ".pug", ".jade"
    };

    private readonly IGitService _git;
    private readonly IContentHasher _hasher;
    private readonly IVaultStorageService _storage;
    private readonly ILogger<VaultPipeline> _logger;
    private readonly FileVaultOptions _options;

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
        IOptions<FileVaultOptions>? options = null,
        IExtractor? extractor = null,
        IChunker? chunker = null,
        IVectorStore? vectorStore = null,
        IEmbeddingService? embeddingService = null)
    {
        _git = git ?? throw new ArgumentNullException(nameof(git));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new FileVaultOptions();
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
            LogStartingMemorize(_logger, entry.SourcePath);

            // Step 1: Backup user content if preserving (for re-memorize scenarios)
            string? existingQaContent = null;
            string? existingAppendText = null;

            if (_storage.EntryStorageExists(entry))
            {
                var vaultContent = await _storage.GetAllVaultContentAsync(entry, ct);

                if (options.PreserveQaContent && !string.IsNullOrWhiteSpace(vaultContent.QaContent))
                {
                    existingQaContent = vaultContent.QaContent;
                    LogBackupQaContent(_logger, existingQaContent.Length);
                }

                if (options.PreserveAppendText && !string.IsNullOrWhiteSpace(vaultContent.AppendText))
                {
                    existingAppendText = vaultContent.AppendText;
                    LogBackupAppendText(_logger, existingAppendText.Length);
                }
            }

            // Step 2: Initialize entry storage if needed
            if (!_storage.EntryStorageExists(entry))
            {
                await _storage.InitializeEntryAsync(entry, ct);
            }

            // Step 3: Extract content from source file → extracted.md
            await ExtractAsync(entry, ct);

            // Step 3.5: Check for empty content — skip pipeline for empty/whitespace files
            var extractedContent = await _storage.GetExtractedContentAsync(entry, ct);
            if (string.IsNullOrWhiteSpace(extractedContent))
            {
                LogNoContentToIndex(_logger, entry.SourcePath);

                string? emptyCommitHash = null;
                if (!options.SkipCommit)
                {
                    emptyCommitHash = await _git.CommitAsync(entry.VaultPath, "memorize: empty content (0 chunks)", ct);
                }

                MarkMemorizedWithIdentity(entry, 0);
                entry.MarkInSync();
                entry.SaveMetadata();

                sw.Stop();
                LogMemorizeCompleted(_logger, entry.SourcePath, 0, sw.Elapsed.TotalSeconds);
                return MemorizeResult.Succeeded(0, 0, sw.Elapsed, emptyCommitHash);
            }

            // Step 4: Refine content → vault/refined.md
            await RefineAsync(entry, ct);

            // Step 5: Restore preserved user content
            if (!string.IsNullOrWhiteSpace(existingQaContent))
            {
                await _storage.StoreQaContentAsync(entry, existingQaContent, ct);
                LogRestoredQaContent(_logger);
            }

            if (!string.IsNullOrWhiteSpace(existingAppendText))
            {
                await _storage.StoreAppendTextAsync(entry, existingAppendText, ct);
                LogRestoredAppendText(_logger);
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
            MarkMemorizedWithIdentity(entry, result.ChunkCount);
            entry.MarkInSync(); // Set sync status to InSync after successful memorize
            entry.SaveMetadata();

            sw.Stop();
            LogMemorizeCompleted(_logger, entry.SourcePath, result.ChunkCount, sw.Elapsed.TotalSeconds);

            return MemorizeResult.Succeeded(result.ChunkCount, result.ContentLength, sw.Elapsed, commitHash);
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogMemorizeFailed(_logger, ex, entry.SourcePath);
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
            LogStartingRefresh(_logger, entry.SourcePath);

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
            MarkMemorizedWithIdentity(entry, result.ChunkCount);
            entry.MarkInSync(); // Set sync status to InSync after successful refresh
            entry.SaveMetadata();

            sw.Stop();
            LogRefreshCompleted(_logger, entry.SourcePath, result.ChunkCount, sw.Elapsed.TotalSeconds);

            return MemorizeResult.Succeeded(result.ChunkCount, result.ContentLength, sw.Elapsed, commitHash);
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogRefreshFailed(_logger, ex, entry.SourcePath);
            entry.MarkError(ex.Message);
            entry.SaveMetadata();
            return MemorizeResult.Failed(ex.Message, sw.Elapsed);
        }
    }

    public async Task ExtractAsync(VaultEntry entry, CancellationToken ct = default)
    {
        LogExtractingContent(_logger, entry.SourcePath);

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

        LogExtracted(_logger, extractedContent.Length, entry.ExtractedMdPath);
    }

    public async Task RefineAsync(VaultEntry entry, CancellationToken ct = default)
    {
        LogRefiningContent(_logger, entry.SourcePath);

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

        LogRefined(_logger, refinedContent.Length, entry.RefinedMdPath);
    }

    public async Task RemoveAsync(VaultEntry entry, CancellationToken ct = default)
    {
        if (_vectorStore == null)
        {
            LogNoVectorStoreSkipRemoval(_logger);
            return;
        }

        // Delete by document ID (filepath hash)
        var documentId = entry.FilepathHash;
        await _vectorStore.DeleteByDocumentIdAsync(documentId, ct);

        LogRemovedChunks(_logger, documentId);
    }

    public async Task<IReadOnlyList<PipelineSearchResult>> SearchAsync(
        string query,
        IEnumerable<string>? documentIds = null,
        int topK = 10,
        float minScore = 0.0f,
        CancellationToken ct = default)
    {
        if (_vectorStore == null || _embeddingService == null)
        {
            LogNoVectorStoreCannotSearch(_logger);
            return [];
        }

        // Generate query embedding
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, ct);

        // Search vector store
        var searchResults = await _vectorStore.SearchAsync(queryEmbedding, topK * 2, minScore, filters: null, ct);

        // Filter by document IDs if specified
        var filteredResults = searchResults;
        if (documentIds != null)
        {
            var docIdSet = documentIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            filteredResults = searchResults.Where(r => docIdSet.Contains(r.DocumentId)).ToList();
        }

        // Map to PipelineSearchResult
        var results = filteredResults
            .Take(topK)
            .Select(r => new PipelineSearchResult
            {
                DocumentId = r.DocumentId,
                ChunkId = r.Id.ToString(),
                ChunkIndex = r.ChunkIndex,
                Content = r.Content,
                Score = r.Score ?? 0f,
                Metadata = r.Metadata
            })
            .ToList();

        LogSearchResults(_logger, query, results.Count);
        return results;
    }

    /// <summary>
    /// Marks entry as memorized with identity if available, falling back to dimension-only.
    /// </summary>
    private void MarkMemorizedWithIdentity(VaultEntry entry, int chunkCount)
    {
        if (_embeddingService is not null)
        {
            entry.MarkMemorized(chunkCount, _embeddingService.GetIdentity());
        }
        else
        {
            entry.MarkMemorized(chunkCount);
        }
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
            LogNoContentToIndex(_logger, entry.SourcePath);
            return (0, 0);
        }

        // Chunk the content
        IReadOnlyList<string> chunks;

        if (_chunker != null)
        {
            // Resolve per-format strategy override from FormatStrategies
            var effectiveStrategy = options.Strategy;
            if (_options.Chunking.FormatStrategies.Count > 0)
            {
                var ext = Path.GetExtension(entry.SourcePath)?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(ext) &&
                    _options.Chunking.FormatStrategies.TryGetValue(ext, out var formatStrategy))
                {
                    effectiveStrategy = formatStrategy;
                }
            }

            var chunkingOptions = new ChunkingOptions
            {
                MaxChunkSize = options.MaxChunkSize,
                OverlapSize = options.OverlapSize,
                Strategy = effectiveStrategy,
                Language = options.Language
            };
            chunks = await _chunker.ChunkAsync(combinedContent, chunkingOptions, ct);
        }
        else
        {
            chunks = ChunkFallback(combinedContent, options.MaxChunkSize);
        }

        LogCreatedChunks(_logger, chunks.Count, combinedContent.Length);

        // Index to vector store
        if (_vectorStore != null && _embeddingService != null)
        {
            await IndexChunksAsync(entry, chunks, options, ct);
        }
        else
        {
            LogNoVectorStoreSkipIndexing(_logger);
        }

        return (chunks.Count, combinedContent.Length);
    }

    private async Task IndexChunksAsync(VaultEntry entry, IReadOnlyList<string> chunks, MemorizeOptions options, CancellationToken ct)
    {
        // Branch: when CheckpointCallback is set (job-queue path), use per-chunk processing
        // for crash-resilient resume. Otherwise, use the existing batch path (faster for the
        // common case of one-shot direct API calls).
        if (options.CheckpointCallback != null)
        {
            await IndexChunksResumableAsync(entry, chunks, options.StartFromChunkIndex, options.CheckpointCallback, ct);
        }
        else
        {
            await IndexChunksBatchAsync(entry, chunks, ct);
        }
    }

    /// <summary>
    /// Batch indexing path: one embedding call + one store call for all chunks.
    /// Used by direct callers of MemorizeAsync (no checkpoint hooks).
    /// </summary>
    private async Task IndexChunksBatchAsync(VaultEntry entry, IReadOnlyList<string> chunks, CancellationToken ct)
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
        var storedCount = storedIds.Count();
        LogIndexedChunks(_logger, storedCount, documentId);
    }

    /// <summary>
    /// Resumable indexing path: per-chunk embed + store + checkpoint.
    /// Used by VaultBackgroundService so that host restarts can recover stuck jobs
    /// from the last committed chunk instead of restarting from chunk 0.
    /// Trade-off: ~5-10% slower than batch for the common no-crash case, but recovery
    /// becomes O(remaining_chunks) instead of O(total_chunks).
    /// </summary>
    private async Task IndexChunksResumableAsync(
        VaultEntry entry,
        IReadOnlyList<string> chunks,
        int startFromChunk,
        Func<int, CancellationToken, Task> checkpointCallback,
        CancellationToken ct)
    {
        var documentId = entry.FilepathHash;
        var processedCount = 0;
        var skippedCount = 0;

        for (int i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (i <= startFromChunk)
            {
                // Already committed in a previous run; the corresponding row exists in vector_chunks.
                skippedCount++;
                continue;
            }

            // Embed single chunk
            var embedding = await _embeddingService!.GenerateEmbeddingAsync(chunks[i], ct);

            // Build DocumentChunk
            var chunk = DocumentChunk.Create(
                documentId: documentId,
                content: chunks[i],
                chunkIndex: i,
                totalChunks: chunks.Count);

            chunk.SetEmbedding(embedding);

            chunk.Metadata ??= new Dictionary<string, object>();
            chunk.Metadata["source_path"] = entry.SourcePath;
            chunk.Metadata["filepath_hash"] = entry.FilepathHash;
            chunk.Metadata["file_name"] = entry.FileName;

            // Store single chunk (1-element batch — uses the same transactional path).
            // On commit success, the chunk row is durably in vector_chunks before we update the checkpoint.
            await _vectorStore!.StoreBatchAsync([chunk], ct);

            // Persist checkpoint AFTER the store transaction commits.
            // If the host crashes between StoreBatchAsync returning and UpdateCheckpointAsync
            // succeeding, recovery sees stale checkpoint (chunk i in DB but checkpoint = i-1)
            // and will redo chunk i — at most 1 chunk wasted, acceptable.
            await checkpointCallback(i, ct);
            processedCount++;
        }

        if (skippedCount > 0)
            LogIndexedChunksResumable(_logger, processedCount, skippedCount, documentId);
        else
            LogIndexedChunks(_logger, processedCount, documentId);
    }

    private async Task<string> ExtractFallbackAsync(string sourcePath, CancellationToken ct)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();

        // Check if file is readable as text (documents + code + custom)
        if (IsReadableTextExtension(extension))
        {
            return await File.ReadAllTextAsync(sourcePath, ct);
        }

        return $"[Content extraction required for {extension} files. Install FileFlux for full support.]";
    }

    /// <summary>
    /// Determines if a file extension is a readable text file (documents + code).
    /// Use this for fallback extraction.
    /// </summary>
    /// <param name="extension">File extension with leading dot (e.g., ".cs")</param>
    /// <returns>True if the file can be read as text</returns>
    public bool IsReadableTextExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;

        // Check documents and code extensions
        if (DocumentExtensions.Contains(extension) || CodeExtensions.Contains(extension))
            return true;

        // Check user-configured additional extensions
        if (_options.AdditionalTextExtensions.Count > 0 &&
            _options.AdditionalTextExtensions.Contains(extension))
            return true;

        return false;
    }

    /// <summary>
    /// Determines if a file extension is suitable for Memorize (vector embedding).
    /// Only document files are recommended; code files should use file-read instead.
    /// </summary>
    /// <param name="extension">File extension with leading dot (e.g., ".md")</param>
    /// <returns>True if the file is recommended for vector embedding</returns>
    public static bool IsDocumentExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;

        return DocumentExtensions.Contains(extension);
    }

    /// <summary>
    /// Determines if a file extension is a code/config file (read-only, not for Memorize).
    /// </summary>
    /// <param name="extension">File extension with leading dot (e.g., ".cs")</param>
    /// <returns>True if the file is a code/config file</returns>
    public static bool IsCodeExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;

        return CodeExtensions.Contains(extension);
    }

    /// <summary>
    /// Gets all readable text extensions (documents + code + additional).
    /// </summary>
    public IReadOnlySet<string> GetAllReadableExtensions()
    {
        var all = new HashSet<string>(DocumentExtensions, StringComparer.OrdinalIgnoreCase);
        foreach (var ext in CodeExtensions)
            all.Add(ext);
        foreach (var ext in _options.AdditionalTextExtensions)
            all.Add(ext);
        return all;
    }

    private static List<string> ChunkFallback(string content, int maxChunkSize)
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

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting memorize for {SourcePath}")]
    private static partial void LogStartingMemorize(ILogger logger, string sourcePath);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Backing up existing QA content ({Length} chars)")]
    private static partial void LogBackupQaContent(ILogger logger, int length);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Backing up existing append-text ({Length} chars)")]
    private static partial void LogBackupAppendText(ILogger logger, int length);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Restored QA content")]
    private static partial void LogRestoredQaContent(ILogger logger);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Restored append-text content")]
    private static partial void LogRestoredAppendText(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Memorize completed for {SourcePath}: {ChunkCount} chunks in {Duration:F2}s")]
    private static partial void LogMemorizeCompleted(ILogger logger, string sourcePath, int chunkCount, double duration);
    [LoggerMessage(Level = LogLevel.Error, Message = "Memorize failed for {SourcePath}")]
    private static partial void LogMemorizeFailed(ILogger logger, Exception exception, string sourcePath);
    [LoggerMessage(Level = LogLevel.Information, Message = "Starting refresh for {SourcePath}")]
    private static partial void LogStartingRefresh(ILogger logger, string sourcePath);
    [LoggerMessage(Level = LogLevel.Information, Message = "Refresh completed for {SourcePath}: {ChunkCount} chunks in {Duration:F2}s")]
    private static partial void LogRefreshCompleted(ILogger logger, string sourcePath, int chunkCount, double duration);
    [LoggerMessage(Level = LogLevel.Error, Message = "Refresh failed for {SourcePath}")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception, string sourcePath);
    [LoggerMessage(Level = LogLevel.Information, Message = "Extracting content from {SourcePath}")]
    private static partial void LogExtractingContent(ILogger logger, string sourcePath);
    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted {Length} chars to {Path}")]
    private static partial void LogExtracted(ILogger logger, int length, string path);
    [LoggerMessage(Level = LogLevel.Information, Message = "Refining content for {SourcePath}")]
    private static partial void LogRefiningContent(ILogger logger, string sourcePath);
    [LoggerMessage(Level = LogLevel.Information, Message = "Refined {Length} chars to {Path}")]
    private static partial void LogRefined(ILogger logger, int length, string path);
    [LoggerMessage(Level = LogLevel.Warning, Message = "No vector store configured, skipping removal")]
    private static partial void LogNoVectorStoreSkipRemoval(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Removed chunks for document {DocumentId}")]
    private static partial void LogRemovedChunks(ILogger logger, string documentId);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Vector store or embedding service not configured, cannot search")]
    private static partial void LogNoVectorStoreCannotSearch(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Search for '{Query}' returned {Count} results")]
    private static partial void LogSearchResults(ILogger logger, string query, int count);
    [LoggerMessage(Level = LogLevel.Warning, Message = "No content to index for {SourcePath}")]
    private static partial void LogNoContentToIndex(ILogger logger, string sourcePath);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Created {ChunkCount} chunks from {ContentLength} chars")]
    private static partial void LogCreatedChunks(ILogger logger, int chunkCount, int contentLength);
    [LoggerMessage(Level = LogLevel.Warning, Message = "No vector store or embedding service configured, skipping indexing")]
    private static partial void LogNoVectorStoreSkipIndexing(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Indexed {Count} chunks for {DocumentId}")]
    private static partial void LogIndexedChunks(ILogger logger, int count, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexed {Processed} chunks (resumed; skipped {Skipped} already-committed) for {DocumentId}")]
    private static partial void LogIndexedChunksResumable(ILogger logger, int processed, int skipped, string documentId);

    #endregion
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
