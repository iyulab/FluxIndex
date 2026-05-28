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
using Xunit;
using Xunit.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace FluxIndex.Extensions.FileVault.Tests.Integration;

/// <summary>
/// Tests for FileVault subfolder scenarios including:
/// - Memorize files in subfolders
/// - Changed file detection and rememorize
/// - Unmemorize from subfolders
/// - Path-based search scoping
/// </summary>
[Trait("Category", "Integration")]
public class VaultSubfolderScenariosTests : IDisposable
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

    public VaultSubfolderScenariosTests(ITestOutputHelper output)
    {
        _output = output;
        _testDir = Path.Combine(Path.GetTempPath(), $"VaultSubfolder_{Guid.NewGuid():N}");
        _vaultDir = Path.Combine(_testDir, ".vault");

        // Create directory structure
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_vaultDir);
        Directory.CreateDirectory(Path.Combine(_testDir, "main-folder"));
        Directory.CreateDirectory(Path.Combine(_testDir, "main-folder", "sub-folder"));
        Directory.CreateDirectory(Path.Combine(_testDir, "other-folder"));

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
            extractor: null,
            chunker: null,
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

    #region Test Scenarios

    [Fact]
    public async Task Scenario1_MemorizeFilesInSubfolders_Success()
    {
        _output.WriteLine("=== Scenario 1: Memorize files in subfolders ===\n");

        // Arrange - Create files in different folders
        var mainA = CreateDocument("main-folder/a.pdf", "This is document A about artificial intelligence and machine learning.");
        var mainB = CreateDocument("main-folder/b.txt", "This is document B about cloud computing and serverless architecture.");
        var subC = CreateDocument("main-folder/sub-folder/c.pdf", "This is document C about deep learning and neural networks.");
        var subD = CreateDocument("main-folder/sub-folder/d.txt", "This is document D about microservices and container orchestration.");
        var otherE = CreateDocument("other-folder/e.md", "This is document E about database design and SQL optimization.");

        _output.WriteLine($"Created files:");
        _output.WriteLine($"  - main-folder/a.pdf");
        _output.WriteLine($"  - main-folder/b.txt");
        _output.WriteLine($"  - main-folder/sub-folder/c.pdf");
        _output.WriteLine($"  - main-folder/sub-folder/d.txt");
        _output.WriteLine($"  - other-folder/e.md\n");

        // Act - Memorize all files
        var entries = new List<VaultEntry>();
        foreach (var path in new[] { mainA, mainB, subC, subD, otherE })
        {
            var entry = VaultEntry.Create(path, _vaultDir);
            await _storage.InitializeEntryAsync(entry, default);
            await _pipeline.MemorizeAsync(entry, new MemorizeOptions { MaxChunkSize = 200 });
            entries.Add(entry);
            _output.WriteLine($"Memorized: {Path.GetRelativePath(_testDir, path)} -> {entry.ChunkCount} chunks");
        }

        // Assert
        _output.WriteLine($"\nTotal chunks in vector store: {_vectorStore.Count}");
        _vectorStore.Count.Should().BeGreaterThan(0);
        entries.Should().HaveCount(5);
        entries.All(e => e.Stage == ProcessingStage.Memorized).Should().BeTrue();
    }

    [Fact]
    public async Task Scenario2_SearchWithFolderScope_ReturnsOnlyFolderFiles()
    {
        _output.WriteLine("=== Scenario 2: Search with folder scope ===\n");

        // Arrange
        await SetupTestFilesAsync();

        // Act - Search with folder scope
        _output.WriteLine("Test: Search scope = 'main-folder/' (should search all files in main-folder)");
        var mainFolderResult = await _vault.SearchAsync("learning", VaultSearchOptions.ForFolder(Path.Combine(_testDir, "main-folder")));

        _output.WriteLine($"  Query: 'learning'");
        _output.WriteLine($"  Documents searched: {mainFolderResult.DocumentsSearched}");
        _output.WriteLine($"  Results: {mainFolderResult.Items.Count}");
        foreach (var item in mainFolderResult.Items)
        {
            _output.WriteLine($"    - {Path.GetFileName(item.SourcePath)}: {item.Score:F4}");
        }

        // Assert - Should include files from main-folder and sub-folder, but not other-folder
        mainFolderResult.DocumentsSearched.Should().Be(4); // a.pdf, b.txt, c.pdf, d.txt
        mainFolderResult.Items.All(i => i.SourcePath.Contains("main-folder")).Should().BeTrue();
        mainFolderResult.Items.Any(i => i.SourcePath.Contains("other-folder")).Should().BeFalse();
    }

    [Fact]
    public async Task Scenario3_SearchWithSubfolderScope_ReturnsOnlySubfolderFiles()
    {
        _output.WriteLine("=== Scenario 3: Search with subfolder scope ===\n");

        // Arrange
        await SetupTestFilesAsync();

        // Act - Search with subfolder scope
        _output.WriteLine("Test: Search scope = 'main-folder/sub-folder/' (should search only sub-folder files)");
        var subFolderResult = await _vault.SearchAsync("learning", VaultSearchOptions.ForFolder(Path.Combine(_testDir, "main-folder", "sub-folder")));

        _output.WriteLine($"  Query: 'learning'");
        _output.WriteLine($"  Documents searched: {subFolderResult.DocumentsSearched}");
        _output.WriteLine($"  Results: {subFolderResult.Items.Count}");
        foreach (var item in subFolderResult.Items)
        {
            _output.WriteLine($"    - {Path.GetFileName(item.SourcePath)}: {item.Score:F4}");
        }

        // Assert - Should only include files from sub-folder
        subFolderResult.DocumentsSearched.Should().Be(2); // c.pdf, d.txt
        subFolderResult.Items.All(i => i.SourcePath.Contains("sub-folder")).Should().BeTrue();
    }

    [Fact]
    public async Task Scenario4_SearchWithSingleFileScope_ReturnsOnlyThatFile()
    {
        _output.WriteLine("=== Scenario 4: Search with single file scope ===\n");

        // Arrange
        await SetupTestFilesAsync();

        // Act - Search with single file scope
        var targetFile = Path.Combine(_testDir, "main-folder", "a.pdf");
        _output.WriteLine($"Test: Search scope = '{Path.GetRelativePath(_testDir, targetFile)}'");

        var singleFileResult = await _vault.SearchAsync("artificial", VaultSearchOptions.ForFile(targetFile));

        _output.WriteLine($"  Query: 'artificial'");
        _output.WriteLine($"  Documents searched: {singleFileResult.DocumentsSearched}");
        _output.WriteLine($"  Results: {singleFileResult.Items.Count}");
        foreach (var item in singleFileResult.Items)
        {
            _output.WriteLine($"    - {Path.GetFileName(item.SourcePath)}: {item.Score:F4}");
        }

        // Assert - Should only include the specified file
        singleFileResult.DocumentsSearched.Should().Be(1);
        singleFileResult.Items.All(i => i.SourcePath == targetFile).Should().BeTrue();
    }

    [Fact]
    public async Task Scenario5_SearchWithMultiplePaths_ReturnsMatchingFiles()
    {
        _output.WriteLine("=== Scenario 5: Search with multiple paths scope ===\n");

        // Arrange
        await SetupTestFilesAsync();

        // Act - Search with multiple paths
        var file1 = Path.Combine(_testDir, "main-folder", "a.pdf");
        var folder1 = Path.Combine(_testDir, "main-folder", "sub-folder");

        _output.WriteLine($"Test: Search scope = ['{Path.GetRelativePath(_testDir, file1)}', '{Path.GetRelativePath(_testDir, folder1)}/']");

        var multiPathResult = await _vault.SearchAsync("learning", new VaultSearchOptions
        {
            PathScope = [file1, folder1],
            TopK = 10
        });

        _output.WriteLine($"  Query: 'learning'");
        _output.WriteLine($"  Documents searched: {multiPathResult.DocumentsSearched}");
        _output.WriteLine($"  Results: {multiPathResult.Items.Count}");
        foreach (var item in multiPathResult.Items)
        {
            _output.WriteLine($"    - {Path.GetRelativePath(_testDir, item.SourcePath)}: {item.Score:F4}");
        }

        // Assert - Should include a.pdf and files from sub-folder
        multiPathResult.DocumentsSearched.Should().Be(3); // a.pdf, c.pdf, d.txt
    }

    [Fact]
    public async Task Scenario6_ChangeDetectionAndRememorize_Success()
    {
        _output.WriteLine("=== Scenario 6: Change detection and rememorize ===\n");

        // Arrange - Create and memorize a file
        var filePath = CreateDocument("main-folder/changeable.txt", "Original content about machine learning basics.");
        var entry = VaultEntry.Create(filePath, _vaultDir);
        await _storage.InitializeEntryAsync(entry, default);
        await _pipeline.MemorizeAsync(entry, null);

        _output.WriteLine($"Original file memorized: {entry.ChunkCount} chunks");
        var originalChunkCount = _vectorStore.Count;

        // Search for original content
        var searchBefore = await _vault.SearchAsync("machine learning", VaultSearchOptions.ForFile(filePath));
        _output.WriteLine($"Search 'machine learning' before change: {searchBefore.Items.Count} results");

        // Act - Modify the file
        await File.WriteAllTextAsync(filePath, "Updated content about artificial intelligence and deep learning neural networks advanced concepts.");
        _output.WriteLine("\nFile content changed.");

        // Detect changes
        var changes = await _vault.DetectChangesAsync(filePath);
        _output.WriteLine($"  Source changed: {changes.SourceChanged}");
        _output.WriteLine($"  Recommended action: {changes.RecommendedAction}");

        changes.SourceChanged.Should().BeTrue();
        changes.RecommendedAction.Should().Be(ChangeAction.Memorize);

        // Rememorize
        entry = VaultEntry.LoadByHash(entry.FilepathHash, _vaultDir)!;
        await _pipeline.RemoveAsync(entry, default);
        entry.ResetToSource();
        await _pipeline.MemorizeAsync(entry, null);

        _output.WriteLine($"\nRememorized: {entry.ChunkCount} chunks");
        _output.WriteLine($"Total chunks: {_vectorStore.Count}");

        // Search for new content
        var searchAfter = await _vault.SearchAsync("artificial intelligence deep learning", VaultSearchOptions.ForFile(filePath));
        _output.WriteLine($"Search 'artificial intelligence deep learning' after change: {searchAfter.Items.Count} results");

        // Assert
        searchAfter.Items.Should().NotBeEmpty();
        entry.SyncStatus.Should().Be(SyncStatus.InSync);
    }

    [Fact]
    public async Task Scenario7_UnmemorizeFromSubfolder_RemovesOnlyTargetFile()
    {
        _output.WriteLine("=== Scenario 7: Unmemorize from subfolder ===\n");

        // Arrange
        await SetupTestFilesAsync();
        var initialCount = _vectorStore.Count;
        _output.WriteLine($"Initial chunk count: {initialCount}");

        // Get entry to remove
        var fileToRemove = Path.Combine(_testDir, "main-folder", "sub-folder", "c.pdf");
        var entry = await _vault.GetAsync(fileToRemove);
        entry.Should().NotBeNull();

        // Act - Remove the file
        _output.WriteLine($"\nRemoving: {Path.GetRelativePath(_testDir, fileToRemove)}");
        await _pipeline.RemoveAsync(entry!, default);
        await _storage.DeleteEntryStorageAsync(entry!, default);

        var afterCount = _vectorStore.Count;
        _output.WriteLine($"Chunk count after removal: {afterCount}");

        // Search should no longer find this file
        var searchResult = await _vault.SearchAsync("deep learning", VaultSearchOptions.ForFolder(Path.Combine(_testDir, "main-folder", "sub-folder")));
        _output.WriteLine($"\nSearch 'deep learning' in sub-folder:");
        _output.WriteLine($"  Documents searched: {searchResult.DocumentsSearched}");
        _output.WriteLine($"  Results: {searchResult.Items.Count}");
        foreach (var item in searchResult.Items)
        {
            _output.WriteLine($"    - {Path.GetFileName(item.SourcePath)}");
        }

        // Assert
        afterCount.Should().BeLessThan(initialCount);
        searchResult.Items.Any(i => i.SourcePath.EndsWith("c.pdf")).Should().BeFalse();
    }

    [Fact]
    public async Task Scenario8_SearchAllWithNoScope_ReturnsAllFiles()
    {
        _output.WriteLine("=== Scenario 8: Search all (no scope) ===\n");

        // Arrange
        await SetupTestFilesAsync();

        // Act - Search with no scope (all files)
        var allResult = await _vault.SearchAsync("document", VaultSearchOptions.All(20));

        _output.WriteLine($"Query: 'document'");
        _output.WriteLine($"Documents searched: {allResult.DocumentsSearched}");
        _output.WriteLine($"Results: {allResult.Items.Count}");
        foreach (var item in allResult.Items)
        {
            _output.WriteLine($"  - {Path.GetRelativePath(_testDir, item.SourcePath)}: {item.Score:F4}");
        }

        // Assert - Should search all 5 files
        allResult.DocumentsSearched.Should().Be(5);
        allResult.Items.Should().NotBeEmpty();
    }

    #endregion

    #region Helper Methods

    private string CreateDocument(string relativePath, string content)
    {
        var fullPath = Path.Combine(_testDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private async Task SetupTestFilesAsync()
    {
        // Create files in different folders
        var files = new[]
        {
            ("main-folder/a.pdf", "This is document A about artificial intelligence and machine learning fundamentals."),
            ("main-folder/b.txt", "This is document B about cloud computing and serverless architecture patterns."),
            ("main-folder/sub-folder/c.pdf", "This is document C about deep learning and neural networks advanced topics."),
            ("main-folder/sub-folder/d.txt", "This is document D about microservices and container orchestration with Kubernetes."),
            ("other-folder/e.md", "This is document E about database design and SQL query optimization techniques.")
        };

        foreach (var (path, content) in files)
        {
            var fullPath = CreateDocument(path, content);
            var entry = VaultEntry.Create(fullPath, _vaultDir);
            await _storage.InitializeEntryAsync(entry, default);
            await _pipeline.MemorizeAsync(entry, new MemorizeOptions { MaxChunkSize = 500 });
        }

        _output.WriteLine($"Setup complete: {_vectorStore.Count} chunks indexed\n");
    }

    #endregion

    #region Helper Classes

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
    /// </summary>
    private class InMemoryEmbeddingService : IEmbeddingService
    {
        private const int Dimension = 128;
        private const int MaxTokens = 8192;

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
        {
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
            return Task.FromResult(text.Length / 4);
        }
    }

    #endregion
}
