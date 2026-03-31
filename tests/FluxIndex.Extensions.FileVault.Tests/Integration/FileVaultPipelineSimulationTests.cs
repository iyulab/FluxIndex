using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Enums;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using FluxIndex.Extensions.FileVault.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using Xunit.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace FluxIndex.Extensions.FileVault.Tests.Integration;

/// <summary>
/// Full pipeline simulation tests for FileVault.
/// Tests the complete flow: memorize → search → change → rememorize → search → delete
/// </summary>
public class FileVaultPipelineSimulationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDir;
    private readonly string _vaultDir;
    private readonly InMemoryVectorStore _vectorStore;
    private readonly InMemoryEmbeddingService _embeddingService;
    private readonly VaultManager _vault;
    private readonly VaultPipeline _pipeline;
    private readonly VaultStorageService _storage;
    private readonly ContentHasher _hasher;
    private readonly IGitService _gitMock;
    private readonly IVaultQueueService _queueMock;
    private readonly IFileWatcherService _watcherMock;

    public FileVaultPipelineSimulationTests(ITestOutputHelper output)
    {
        _output = output;
        _testDir = Path.Combine(Path.GetTempPath(), $"FileVaultSim_{Guid.NewGuid():N}");
        _vaultDir = Path.Combine(_testDir, ".vault");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_vaultDir);

        // Real services
        _vectorStore = new InMemoryVectorStore();
        _embeddingService = new InMemoryEmbeddingService();
        _hasher = new ContentHasher();

        // Mocks
        _gitMock = Substitute.For<IGitService>();
        _gitMock.InitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _gitMock.CommitAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("abc123");
        _gitMock.StatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new GitStatus { ModifiedFiles = [] });

        _storage = new VaultStorageService(
            NullLogger<VaultStorageService>.Instance,
            _gitMock,
            MsOptions.Create(new FileVaultOptions { VaultBasePath = _vaultDir }));

        _queueMock = Substitute.For<IVaultQueueService>();
        _queueMock.GetStatisticsAsync(Arg.Any<CancellationToken>()).Returns(new QueueStatistics());

        _watcherMock = Substitute.For<IFileWatcherService>();
        _watcherMock.GetAllWatchers().Returns([]);

        // Pipeline with real vector store and embedding
        _pipeline = new VaultPipeline(
            _gitMock,
            _hasher,
            _storage,
            NullLogger<VaultPipeline>.Instance,
            extractor: null,  // Use fallback extraction
            chunker: null,    // Use fallback chunking
            vectorStore: _vectorStore,
            embeddingService: _embeddingService);

        var options = MsOptions.Create(new FileVaultOptions { VaultBasePath = _vaultDir });

        _vault = new VaultManager(
            _hasher,
            _gitMock,
            _pipeline,
            _queueMock,
            _watcherMock,
            _storage,
            NullLogger<VaultManager>.Instance,
            options);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch { /* ignore cleanup errors */ }
    }

    [Fact]
    public async Task FullPipeline_MemorizeSearchChangeRememorizeSearchDelete_Success()
    {
        _output.WriteLine("=== FileVault Full Pipeline Simulation ===\n");

        // === STEP 1: Create and Memorize a file ===
        _output.WriteLine("STEP 1: Creating and memorizing a document...");

        var docPath = CreateDocument("product_manual.txt", """
            FluxIndex Product Manual

            Chapter 1: Introduction
            FluxIndex is a powerful RAG (Retrieval-Augmented Generation) infrastructure library.
            It provides semantic search capabilities using vector embeddings.

            Chapter 2: Installation
            Install FluxIndex using NuGet: dotnet add package FluxIndex.SDK
            Configure your vector store and embedding service.

            Chapter 3: Basic Usage
            Create a FluxIndexContext and start indexing documents.
            Use the search API to find relevant content.
            """);

        var entry = VaultEntry.Create(docPath, _vaultDir);
        await _storage.InitializeEntryAsync(entry, default);

        var memorizeResult = await _pipeline.MemorizeAsync(entry, new MemorizeOptions
        {
            MaxChunkSize = 200,
            OverlapSize = 20
        });

        _output.WriteLine($"  - File: {Path.GetFileName(docPath)}");
        _output.WriteLine($"  - Chunks created: {memorizeResult.ChunkCount}");
        _output.WriteLine($"  - Content length: {memorizeResult.ContentLength} chars");
        _output.WriteLine($"  - Duration: {memorizeResult.Duration.TotalMilliseconds:F0}ms");
        _output.WriteLine($"  - SyncStatus: {entry.SyncStatus}");
        _output.WriteLine($"  - ProcessingStage: {entry.Stage}");

        memorizeResult.Success.Should().BeTrue();
        memorizeResult.ChunkCount.Should().BeGreaterThan(0);
        entry.SyncStatus.Should().Be(SyncStatus.InSync);
        entry.Stage.Should().Be(ProcessingStage.Memorized);

        // === STEP 2: Search for content ===
        _output.WriteLine("\nSTEP 2: Searching indexed content...");

        var searchResults1 = await SearchAsync("How to install FluxIndex?");
        _output.WriteLine($"  Query: 'How to install FluxIndex?'");
        _output.WriteLine($"  Results found: {searchResults1.Count}");
        foreach (var result in searchResults1.Take(3))
        {
            _output.WriteLine($"    - Score: {result.Score:F4}, Content: {Truncate(result.Content, 60)}");
        }

        searchResults1.Should().NotBeEmpty("파이프라인이 정상 동작하면 검색 결과가 반환되어야 함");
        // 해시 기반 임베딩은 의미적 유사도를 보장하지 않으므로 콘텐츠 존재 여부만 확인
        searchResults1.Should().Contain(r =>
            r.Content.Contains("FluxIndex", StringComparison.OrdinalIgnoreCase) ||
            r.Content.Contains("install", StringComparison.OrdinalIgnoreCase) ||
            r.Content.Contains("search", StringComparison.OrdinalIgnoreCase));

        // === STEP 3: Modify the source file ===
        _output.WriteLine("\nSTEP 3: Modifying source file...");

        await File.WriteAllTextAsync(docPath, """
            FluxIndex Product Manual v2.0

            Chapter 1: Introduction
            FluxIndex is an advanced RAG infrastructure library with hybrid search support.
            It combines vector similarity search with BM25 keyword matching.

            Chapter 2: Installation (Updated)
            Install FluxIndex using NuGet: dotnet add package FluxIndex.SDK
            NEW: Also install the storage provider: dotnet add package FluxIndex.Storage.SQLite
            Configure your vector store, embedding service, and optional reranker.

            Chapter 3: Advanced Features
            - Hybrid search with configurable alpha blending
            - Semantic caching for improved performance
            - Multi-stage retrieval pipelines

            Chapter 4: Best Practices
            Use chunking strategies appropriate for your content type.
            Monitor search quality with the evaluation framework.
            """);

        _output.WriteLine("  - File updated with v2.0 content");
        _output.WriteLine("  - Added new chapters and features");

        // Detect changes
        var changes = await _vault.DetectChangesAsync(docPath);
        _output.WriteLine($"  - Source changed: {changes.SourceChanged}");
        _output.WriteLine($"  - Recommended action: {changes.RecommendedAction}");

        // Reload entry to see updated status
        entry = VaultEntry.LoadByHash(entry.FilepathHash, _vaultDir)!;
        _output.WriteLine($"  - SyncStatus after detection: {entry.SyncStatus}");

        changes.SourceChanged.Should().BeTrue();
        changes.RecommendedAction.Should().Be(ChangeAction.Memorize);
        entry.SyncStatus.Should().Be(SyncStatus.SourceModified);

        // === STEP 4: Re-memorize the changed file ===
        _output.WriteLine("\nSTEP 4: Re-memorizing changed file...");

        // First, remove old chunks
        await _pipeline.RemoveAsync(entry, default);
        var oldChunkCount = _vectorStore.Count;

        // Re-extract and re-index
        entry.ResetToSource();
        var rememorizeResult = await _pipeline.MemorizeAsync(entry, new MemorizeOptions
        {
            MaxChunkSize = 200,
            OverlapSize = 20
        });

        _output.WriteLine($"  - New chunks created: {rememorizeResult.ChunkCount}");
        _output.WriteLine($"  - Vector store size: {_vectorStore.Count}");
        _output.WriteLine($"  - SyncStatus: {entry.SyncStatus}");

        rememorizeResult.Success.Should().BeTrue();
        entry.SyncStatus.Should().Be(SyncStatus.InSync);

        // === STEP 5: Search for new content ===
        _output.WriteLine("\nSTEP 5: Searching for newly added content...");

        var searchResults2 = await SearchAsync("hybrid search alpha blending");
        _output.WriteLine($"  Query: 'hybrid search alpha blending'");
        _output.WriteLine($"  Results found: {searchResults2.Count}");
        foreach (var result in searchResults2.Take(3))
        {
            _output.WriteLine($"    - Score: {result.Score:F4}, Content: {Truncate(result.Content, 60)}");
        }

        searchResults2.Should().NotBeEmpty();
        searchResults2.Should().Contain(r =>
            r.Content.Contains("hybrid", StringComparison.OrdinalIgnoreCase) ||
            r.Content.Contains("alpha", StringComparison.OrdinalIgnoreCase));

        // Search for v2.0 specific content
        var searchResults3 = await SearchAsync("best practices evaluation framework");
        _output.WriteLine($"\n  Query: 'best practices evaluation framework'");
        _output.WriteLine($"  Results found: {searchResults3.Count}");
        foreach (var result in searchResults3.Take(3))
        {
            _output.WriteLine($"    - Score: {result.Score:F4}, Content: {Truncate(result.Content, 60)}");
        }

        // === STEP 6: Delete the file and clean up ===
        _output.WriteLine("\nSTEP 6: Simulating file deletion and cleanup...");

        // Simulate source file deletion
        File.Delete(docPath);
        _output.WriteLine("  - Source file deleted");

        // Detect the deletion
        var deleteChanges = await _vault.DetectChangesAsync(docPath);
        entry = VaultEntry.LoadByHash(entry.FilepathHash, _vaultDir)!;
        _output.WriteLine($"  - Source exists: {deleteChanges.SourceExists}");
        _output.WriteLine($"  - Recommended action: {deleteChanges.RecommendedAction}");
        _output.WriteLine($"  - SyncStatus: {entry.SyncStatus}");

        deleteChanges.SourceExists.Should().BeFalse();
        deleteChanges.RecommendedAction.Should().Be(ChangeAction.Remove);
        entry.SyncStatus.Should().Be(SyncStatus.SourceDeleted);

        // Simulate the remove process (as background service would do)
        _output.WriteLine("\n  Simulating phased removal process...");

        // Phase 1: Remove from vector store
        entry.MarkRemovalPending();
        entry.SaveMetadata();
        _output.WriteLine($"    Phase 1a: SyncStatus = {entry.SyncStatus}");

        await _pipeline.RemoveAsync(entry, default);
        entry.MarkRemovalPartial("Vector");
        entry.SaveMetadata();
        _output.WriteLine($"    Phase 1b: SyncStatus = {entry.SyncStatus}, Phase = {entry.RemovalPhase}");
        _output.WriteLine($"    Vector store count: {_vectorStore.Count}");

        entry.SyncStatus.Should().Be(SyncStatus.RemovalPartial);
        entry.RemovalPhase.Should().Be("Vector");

        // Phase 2: Remove storage
        await _storage.DeleteEntryStorageAsync(entry, default);
        _output.WriteLine("    Phase 2: Storage deleted");

        // Verify cleanup
        var searchResults4 = await SearchAsync("FluxIndex installation");
        _output.WriteLine($"\n  Verification search after deletion:");
        _output.WriteLine($"    Query: 'FluxIndex installation'");
        _output.WriteLine($"    Results for deleted doc: {searchResults4.Count(r => r.DocumentId == entry.FilepathHash)}");

        searchResults4.Where(r => r.DocumentId == entry.FilepathHash).Should().BeEmpty();

        _output.WriteLine("\n=== Simulation Complete ===");
        _output.WriteLine($"Total vector store entries: {_vectorStore.Count}");
    }

    [Fact]
    public async Task SyncStatusTransitions_AllStates_TrackedCorrectly()
    {
        _output.WriteLine("=== SyncStatus State Transition Test ===\n");

        var docPath = CreateDocument("status_test.txt", "Initial content for status testing.");
        var entry = VaultEntry.Create(docPath, _vaultDir);
        await _storage.InitializeEntryAsync(entry, default);

        // Track all state transitions
        var transitions = new List<(string Action, SyncStatus Status, string? Phase)>();

        void LogTransition(string action)
        {
            transitions.Add((action, entry.SyncStatus, entry.RemovalPhase));
            _output.WriteLine($"  {action,-30} -> SyncStatus: {entry.SyncStatus,-15} RemovalPhase: {entry.RemovalPhase ?? "null"}");
        }

        _output.WriteLine("State transitions:");

        // Initial state
        LogTransition("Create");
        entry.SyncStatus.Should().Be(SyncStatus.InSync);

        // After memorize
        await _pipeline.MemorizeAsync(entry, null);
        LogTransition("After Memorize");
        entry.SyncStatus.Should().Be(SyncStatus.InSync);

        // Simulate source modification detection
        entry.UpdateSyncStatus(SyncStatus.SourceModified);
        entry.SaveMetadata();
        LogTransition("Source Modified");

        // After re-memorize
        entry.ResetToSource();
        await _pipeline.MemorizeAsync(entry, null);
        LogTransition("After Re-memorize");
        entry.SyncStatus.Should().Be(SyncStatus.InSync);

        // Simulate vault modification
        entry.UpdateSyncStatus(SyncStatus.VaultModified);
        entry.SaveMetadata();
        LogTransition("Vault Modified");

        // After refresh
        await _pipeline.RemoveAsync(entry, default);
        entry.ResetToSource();
        await _pipeline.MemorizeAsync(entry, null);
        LogTransition("After Refresh");

        // Deletion flow
        entry.MarkSourceDeleted();
        entry.SaveMetadata();
        LogTransition("Source Deleted");

        entry.MarkRemovalPending();
        entry.SaveMetadata();
        LogTransition("Removal Pending");

        entry.MarkRemovalPartial("Vector");
        entry.SaveMetadata();
        LogTransition("Removal Partial (Vector)");

        // Error state
        entry.MarkSyncError("Simulated error");
        entry.SaveMetadata();
        LogTransition("Error State");

        // Recovery
        entry.MarkInSync();
        entry.SaveMetadata();
        LogTransition("Recovered to InSync");

        _output.WriteLine($"\nTotal transitions recorded: {transitions.Count}");

        // Verify persistence
        var reloaded = VaultEntry.LoadByHash(entry.FilepathHash, _vaultDir);
        reloaded.Should().NotBeNull();
        reloaded!.SyncStatus.Should().Be(SyncStatus.InSync);
    }

    [Fact]
    public async Task MultipleDocuments_IndexAndSearch_IsolatedCorrectly()
    {
        _output.WriteLine("=== Multiple Documents Test ===\n");

        // Create multiple documents
        var doc1 = CreateDocument("csharp_guide.txt", """
            C# Programming Guide
            Learn about classes, interfaces, and async/await patterns.
            Use LINQ for elegant data manipulation.
            """);

        var doc2 = CreateDocument("python_guide.txt", """
            Python Programming Guide
            Learn about classes, decorators, and asyncio patterns.
            Use list comprehensions for elegant data manipulation.
            """);

        var doc3 = CreateDocument("rust_guide.txt", """
            Rust Programming Guide
            Learn about ownership, borrowing, and async/await patterns.
            Use iterators for efficient data manipulation.
            """);

        var entries = new List<VaultEntry>();
        foreach (var path in new[] { doc1, doc2, doc3 })
        {
            var entry = VaultEntry.Create(path, _vaultDir);
            await _storage.InitializeEntryAsync(entry, default);
            await _pipeline.MemorizeAsync(entry, new MemorizeOptions { MaxChunkSize = 150 });
            entries.Add(entry);
            _output.WriteLine($"Indexed: {Path.GetFileName(path)} -> {entry.ChunkCount} chunks");
        }

        _output.WriteLine($"\nTotal chunks in vector store: {_vectorStore.Count}");

        // Search for language-specific content
        // Note: InMemory embedding uses text hash, not semantic similarity
        // So we verify that all docs are searchable, not that specific docs rank first
        _output.WriteLine("\nSearching for 'LINQ data manipulation':");
        var linqResults = await SearchAsync("LINQ data manipulation");
        foreach (var r in linqResults.Take(3))
        {
            _output.WriteLine($"  Score: {r.Score:F4}, Doc: {GetDocName(r.DocumentId, entries)}");
        }
        linqResults.Should().NotBeEmpty("Search should return results");
        linqResults.Select(r => r.DocumentId).Should().Contain(entries[0].FilepathHash, "C# doc should be in results");

        _output.WriteLine("\nSearching for 'list comprehensions Python':");
        var pythonResults = await SearchAsync("list comprehensions Python");
        foreach (var r in pythonResults.Take(3))
        {
            _output.WriteLine($"  Score: {r.Score:F4}, Doc: {GetDocName(r.DocumentId, entries)}");
        }
        pythonResults.Should().NotBeEmpty("Search should return results");
        pythonResults.Select(r => r.DocumentId).Should().Contain(entries[1].FilepathHash, "Python doc should be in results");

        _output.WriteLine("\nSearching for 'ownership borrowing Rust':");
        var rustResults = await SearchAsync("ownership borrowing Rust");
        foreach (var r in rustResults.Take(3))
        {
            _output.WriteLine($"  Score: {r.Score:F4}, Doc: {GetDocName(r.DocumentId, entries)}");
        }
        rustResults.Should().NotBeEmpty("Search should return results");
        rustResults.Select(r => r.DocumentId).Should().Contain(entries[2].FilepathHash, "Rust doc should be in results");

        // Delete one document and verify isolation
        _output.WriteLine("\nDeleting Python guide and verifying isolation...");
        await _pipeline.RemoveAsync(entries[1], default);
        await _storage.DeleteEntryStorageAsync(entries[1], default);

        var afterDelete = await SearchAsync("programming guide");
        var pythonChunks = afterDelete.Where(r => r.DocumentId == entries[1].FilepathHash).ToList();
        _output.WriteLine($"Python chunks after deletion: {pythonChunks.Count}");
        pythonChunks.Should().BeEmpty();

        var csharpChunks = afterDelete.Where(r => r.DocumentId == entries[0].FilepathHash).ToList();
        _output.WriteLine($"C# chunks still present: {csharpChunks.Count}");
        csharpChunks.Should().NotBeEmpty();
    }

    [Fact]
    public async Task PartialRemovalRecovery_SimulateInterruption_RecoversProperly()
    {
        _output.WriteLine("=== Partial Removal Recovery Test ===\n");

        var docPath = CreateDocument("recovery_test.txt", "Content for recovery testing scenario.");
        var entry = VaultEntry.Create(docPath, _vaultDir);
        await _storage.InitializeEntryAsync(entry, default);
        await _pipeline.MemorizeAsync(entry, null);

        _output.WriteLine($"Initial state: {_vectorStore.Count} chunks in store");

        // Simulate interrupted removal - vector deleted but storage remains
        _output.WriteLine("\nSimulating interrupted removal (Phase 1 complete, Phase 2 pending)...");

        entry.MarkRemovalPending();
        entry.SaveMetadata();

        await _pipeline.RemoveAsync(entry, default); // Vector removed
        entry.MarkRemovalPartial("Vector");
        entry.SaveMetadata();

        _output.WriteLine($"After Phase 1: SyncStatus={entry.SyncStatus}, RemovalPhase={entry.RemovalPhase}");
        _output.WriteLine($"Vector store count: {_vectorStore.Count}");
        _output.WriteLine($"Entry directory exists: {Directory.Exists(entry.EntryPath)}");

        // Simulate restart - reload entry from disk
        _output.WriteLine("\nSimulating service restart...");
        var recoveredEntry = VaultEntry.LoadByHash(entry.FilepathHash, _vaultDir);

        recoveredEntry.Should().NotBeNull();
        recoveredEntry!.SyncStatus.Should().Be(SyncStatus.RemovalPartial);
        recoveredEntry.RemovalPhase.Should().Be("Vector");

        _output.WriteLine($"Recovered entry state: SyncStatus={recoveredEntry.SyncStatus}, RemovalPhase={recoveredEntry.RemovalPhase}");

        // Complete the removal
        _output.WriteLine("\nCompleting interrupted removal...");
        await _storage.DeleteEntryStorageAsync(recoveredEntry, default);

        _output.WriteLine($"Entry directory exists after completion: {Directory.Exists(entry.EntryPath)}");
        Directory.Exists(entry.EntryPath).Should().BeFalse();

        _output.WriteLine("\nRecovery simulation complete!");
    }

    #region Error Path Tests — Stage Transition Verification

    [Fact]
    public async Task EmbeddingFailure_SetsStageToError()
    {
        // Arrange — pipeline with failing embedding service
        var failingEmbedding = Substitute.For<IEmbeddingService>();
        failingEmbedding
            .GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Embedding service unavailable"));
        failingEmbedding
            .GenerateEmbeddingsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Embedding service unavailable"));

        var failPipeline = new VaultPipeline(
            _gitMock, _hasher, _storage,
            NullLogger<VaultPipeline>.Instance,
            extractor: null, chunker: null,
            vectorStore: _vectorStore,
            embeddingService: failingEmbedding);

        var filePath = CreateDocument("embed_fail.txt", "Content that will fail during embedding.");
        var entry = VaultEntry.Create(filePath, _vaultDir);
        await _storage.InitializeEntryAsync(entry, default);

        // Act
        var result = await failPipeline.MemorizeAsync(entry);

        // Assert
        _output.WriteLine($"Success={result.Success}, Stage={entry.Stage}, LastError={entry.LastError}");
        result.Success.Should().BeFalse();
        entry.Stage.Should().Be(ProcessingStage.Error, "Stage must transition to Error on embedding failure");
        entry.LastError.Should().NotBeNullOrEmpty();
        entry.RetryCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task VectorStoreFailure_SetsStageToError()
    {
        // Arrange — pipeline with failing vector store
        var failingVectorStore = Substitute.For<IVectorStore>();
        failingVectorStore
            .StoreAsync(Arg.Any<DocumentChunk>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("Vector store disk full"));
        failingVectorStore
            .StoreBatchAsync(Arg.Any<IEnumerable<DocumentChunk>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("Vector store disk full"));

        var failPipeline = new VaultPipeline(
            _gitMock, _hasher, _storage,
            NullLogger<VaultPipeline>.Instance,
            extractor: null, chunker: null,
            vectorStore: failingVectorStore,
            embeddingService: _embeddingService);

        var filePath = CreateDocument("store_fail.txt", "Content that will fail during vector storage.");
        var entry = VaultEntry.Create(filePath, _vaultDir);
        await _storage.InitializeEntryAsync(entry, default);

        // Act
        var result = await failPipeline.MemorizeAsync(entry);

        // Assert
        _output.WriteLine($"Success={result.Success}, Stage={entry.Stage}, LastError={entry.LastError}");
        result.Success.Should().BeFalse();
        entry.Stage.Should().Be(ProcessingStage.Error, "Stage must transition to Error on vector store failure");
        entry.LastError.Should().NotBeNullOrEmpty();
        entry.RetryCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ErrorStage_PersistedToMetadata_SurvivesReload()
    {
        // Arrange — cause a failure
        var failingEmbedding = Substitute.For<IEmbeddingService>();
        failingEmbedding
            .GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Dimension mismatch"));
        failingEmbedding
            .GenerateEmbeddingsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Dimension mismatch"));

        var failPipeline = new VaultPipeline(
            _gitMock, _hasher, _storage,
            NullLogger<VaultPipeline>.Instance,
            extractor: null, chunker: null,
            vectorStore: _vectorStore,
            embeddingService: failingEmbedding);

        var filePath = CreateDocument("persist_error.txt", "Content to test error persistence.");
        var entry = VaultEntry.Create(filePath, _vaultDir);
        await _storage.InitializeEntryAsync(entry, default);
        await failPipeline.MemorizeAsync(entry);

        // Act — reload from disk
        var reloaded = VaultEntry.LoadByHash(entry.FilepathHash, _vaultDir);

        // Assert
        _output.WriteLine($"Reloaded: Stage={reloaded?.Stage}, LastError={reloaded?.LastError}, RetryCount={reloaded?.RetryCount}");
        reloaded.Should().NotBeNull();
        reloaded!.Stage.Should().Be(ProcessingStage.Error, "Error stage must survive disk roundtrip");
        reloaded.LastError.Should().Contain("Dimension mismatch");
        reloaded.RetryCount.Should().BeGreaterThan(0);
    }

    #endregion

    #region Helper Methods

    private string CreateDocument(string fileName, string content)
    {
        var path = Path.Combine(_testDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<List<SearchResult>> SearchAsync(string query)
    {
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
        var results = await _vectorStore.SearchAsync(queryEmbedding, topK: 10);
        return results.Select(r => new SearchResult
        {
            DocumentId = r.DocumentId,
            Content = r.Content,
            Score = r.Score ?? 0f
        }).ToList();
    }

    private static string Truncate(string text, int maxLength)
    {
        var cleaned = text.Replace("\n", " ").Replace("\r", "");
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength] + "...";
    }

    private static string GetDocName(string docId, List<VaultEntry> entries)
    {
        var entry = entries.FirstOrDefault(e => e.FilepathHash == docId);
        return entry != null ? Path.GetFileName(entry.SourcePath) : docId[..8];
    }

    #endregion

    #region Helper Classes

    private class SearchResult
    {
        public string DocumentId { get; set; } = "";
        public string Content { get; set; } = "";
        public float Score { get; set; }
    }

    /// <summary>
    /// Simple in-memory vector store for testing.
    /// </summary>
    private class InMemoryVectorStore : IVectorStore
    {
        private readonly List<DocumentChunk> _chunks = [];
        private readonly object _lock = new();

        public int Count
        {
            get { lock (_lock) return _chunks.Count; }
        }

        public Task<string> StoreAsync(DocumentChunk chunk, CancellationToken ct = default)
        {
            lock (_lock)
            {
                _chunks.Add(chunk);
            }
            return Task.FromResult(chunk.Id.ToString());
        }

        public Task<IEnumerable<string>> StoreBatchAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct = default)
        {
            var ids = new List<string>();
            lock (_lock)
            {
                foreach (var chunk in chunks)
                {
                    _chunks.Add(chunk);
                    ids.Add(chunk.Id.ToString());
                }
            }
            return Task.FromResult<IEnumerable<string>>(ids);
        }

        public Task<DocumentChunk?> GetAsync(string id, CancellationToken ct = default)
        {
            lock (_lock)
            {
                var chunk = _chunks.FirstOrDefault(c => c.Id.ToString() == id);
                return Task.FromResult(chunk);
            }
        }

        public Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken ct = default) => GetAsync(id, ct);

        public Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(string documentId, CancellationToken ct = default)
        {
            lock (_lock)
            {
                var results = _chunks.Where(c => c.DocumentId == documentId).ToList();
                return Task.FromResult<IEnumerable<DocumentChunk>>(results);
            }
        }

        public Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(IEnumerable<string> ids, CancellationToken ct = default)
        {
            var idSet = ids.ToHashSet();
            lock (_lock)
            {
                var results = _chunks.Where(c => idSet.Contains(c.Id.ToString())).ToList();
                return Task.FromResult<IEnumerable<DocumentChunk>>(results);
            }
        }

        public Task<IEnumerable<DocumentChunk>> SearchAsync(float[] queryVector, int topK = 10, float minScore = 0, Dictionary<string, object>? filters = null, CancellationToken ct = default)
        {
            lock (_lock)
            {
                var results = _chunks
                    .Select(c => (Chunk: c, Score: CosineSimilarity(queryVector, c.Embedding!)))
                    .Where(x => x.Score >= minScore)
                    .OrderByDescending(x => x.Score)
                    .Take(topK)
                    .Select(x =>
                    {
                        var chunk = x.Chunk;
                        chunk.Score = x.Score;
                        return chunk;
                    })
                    .ToList();
                return Task.FromResult<IEnumerable<DocumentChunk>>(results);
            }
        }

        public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        {
            lock (_lock)
            {
                var removed = _chunks.RemoveAll(c => c.Id.ToString() == id);
                return Task.FromResult(removed > 0);
            }
        }

        public Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken ct = default)
        {
            lock (_lock)
            {
                var removed = _chunks.RemoveAll(c => c.DocumentId == documentId);
                return Task.FromResult(removed > 0);
            }
        }

        public Task<bool> ExistsAsync(string id, CancellationToken ct = default)
        {
            lock (_lock)
            {
                return Task.FromResult(_chunks.Any(c => c.Id.ToString() == id));
            }
        }

        public Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken ct = default)
        {
            lock (_lock)
            {
                var index = _chunks.FindIndex(c => c.Id == chunk.Id);
                if (index >= 0)
                {
                    _chunks[index] = chunk;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
        }

        public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(Count);
        public Task<int> GetCountAsync(CancellationToken ct = default) => Task.FromResult(Count);

        public Task ClearAsync(CancellationToken ct = default)
        {
            lock (_lock)
            {
                _chunks.Clear();
            }
            return Task.CompletedTask;
        }

        private static float CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length != b.Length) return 0;
            float dot = 0, normA = 0, normB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }
            var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
            return denom > 0 ? dot / denom : 0;
        }
    }

    /// <summary>
    /// Simple deterministic embedding service for testing.
    /// Uses hash-based embeddings for reproducible results.
    /// </summary>
    private class InMemoryEmbeddingService : IEmbeddingService
    {
        private const int Dimension = 128;
        private const int MaxTokens = 8192;

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
        {
            // Generate deterministic embedding based on text content
            var embedding = new float[Dimension];
            var words = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in words)
            {
                var hash = word.GetHashCode();
                for (int i = 0; i < Dimension; i++)
                {
                    embedding[i] += MathF.Sin(hash * (i + 1) * 0.1f) * 0.1f;
                }
            }

            // Normalize
            var norm = MathF.Sqrt(embedding.Sum(x => x * x));
            if (norm > 0)
            {
                for (int i = 0; i < Dimension; i++)
                    embedding[i] /= norm;
            }

            return Task.FromResult(embedding);
        }

        public async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)
        {
            var results = new List<float[]>();
            foreach (var text in texts)
            {
                results.Add(await GenerateEmbeddingAsync(text, ct));
            }
            return results;
        }

        public int GetEmbeddingDimension() => Dimension;
        public string GetModelName() => "InMemoryTestEmbedding";
        public int GetMaxTokens() => MaxTokens;

        public FluxIndex.Core.Domain.ValueObjects.EmbeddingIdentity GetIdentity() => new()
        {
            Provider = "InMemory",
            Model = GetModelName(),
            Dimension = Dimension
        };

        public Task<int> CountTokensAsync(string text, CancellationToken ct = default)
        {
            // Simple approximation: ~4 characters per token
            return Task.FromResult(text.Length / 4);
        }
    }

    #endregion
}
