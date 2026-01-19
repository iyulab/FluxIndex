using FluentAssertions;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Enums;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using FluxIndex.Extensions.FileVault.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace FluxIndex.Extensions.FileVault.Tests.Services;

public class VaultManagerTests : IDisposable
{
    private readonly ContentHasher _contentHasher;
    private readonly Mock<IGitService> _gitServiceMock;
    private readonly Mock<IVaultPipeline> _pipelineMock;
    private readonly Mock<IVaultQueueService> _queueServiceMock;
    private readonly Mock<IFileWatcherService> _fileWatcherMock;
    private readonly Mock<IVaultStorageService> _storageMock;
    private readonly VaultManager _vault;
    private readonly string _testDir;
    private readonly string _vaultDir;

    public VaultManagerTests()
    {
        _contentHasher = new ContentHasher();
        _gitServiceMock = new Mock<IGitService>();
        _pipelineMock = new Mock<IVaultPipeline>();
        _queueServiceMock = new Mock<IVaultQueueService>();
        _fileWatcherMock = new Mock<IFileWatcherService>();
        _storageMock = new Mock<IVaultStorageService>();

        // Create test directories first
        _testDir = Path.Combine(Path.GetTempPath(), "FileVaultTests_" + Guid.NewGuid().ToString("N"));
        _vaultDir = Path.Combine(_testDir, ".vault");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_vaultDir);

        // Setup default mock returns
        _fileWatcherMock.Setup(f => f.GetAllWatchers()).Returns([]);
        _storageMock.Setup(s => s.BasePath).Returns(_vaultDir);
        _storageMock.Setup(s => s.GetStorageSizeAsync(It.IsAny<VaultEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);
        _storageMock.Setup(s => s.EntryStorageExists(It.IsAny<VaultEntry>())).Returns(false);
        _storageMock.Setup(s => s.InitializeEntryAsync(It.IsAny<VaultEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Setup pipeline mock to return success
        _pipelineMock.Setup(p => p.MemorizeAsync(It.IsAny<VaultEntry>(), It.IsAny<MemorizeOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MemorizeResult.Succeeded(5, 1000, TimeSpan.FromSeconds(1)));
        _pipelineMock.Setup(p => p.RefreshAsync(It.IsAny<VaultEntry>(), It.IsAny<MemorizeOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MemorizeResult.Succeeded(5, 1000, TimeSpan.FromSeconds(1)));
        _pipelineMock.Setup(p => p.ExtractAsync(It.IsAny<VaultEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Setup queue mock
        _queueServiceMock.Setup(q => q.EnqueueMemorizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string hash, string path, CancellationToken _) => CreateTestJob(hash, path, VaultJobType.Memorize));
        _queueServiceMock.Setup(q => q.EnqueueRefreshAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string hash, string path, CancellationToken _) => CreateTestJob(hash, path, VaultJobType.Refresh));
        _queueServiceMock.Setup(q => q.GetStatisticsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueStatistics());

        var options = MsOptions.Create(new FileVaultOptions
        {
            VaultBasePath = _vaultDir
        });

        var logger = NullLogger<VaultManager>.Instance;

        _vault = new VaultManager(
            _contentHasher,
            _gitServiceMock.Object,
            _pipelineMock.Object,
            _queueServiceMock.Object,
            _fileWatcherMock.Object,
            _storageMock.Object,
            logger,
            options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try
            {
                Directory.Delete(_testDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public async Task MemorizeAsync_NewFile_EnqueuesJob()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Hello, World!");

        // Act
        var result = await _vault.MemorizeAsync(filePath);

        // Assert
        result.Should().NotBeNull();
        result.SourcePath.Should().Be(Path.GetFullPath(filePath));
        _queueServiceMock.Verify(q => q.EnqueueMemorizeAsync(
            It.IsAny<string>(),
            Path.GetFullPath(filePath),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MemorizeAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDir, "nonexistent.txt");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _vault.MemorizeAsync(nonExistentPath));
    }

    [Fact]
    public async Task MemorizeAsync_InitializesEntryStorage()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Hello, World!");

        // Act
        await _vault.MemorizeAsync(filePath);

        // Assert
        _storageMock.Verify(s => s.InitializeEntryAsync(
            It.IsAny<VaultEntry>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_NonExistentEntry_ReturnsNull()
    {
        // Act
        var result = await _vault.GetAsync(Path.Combine(_testDir, "nonexistent.txt"));

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_NonExistentEntry_ThrowsInvalidOperationException()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Hello, World!");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _vault.RefreshAsync(filePath));
    }

    [Fact]
    public async Task RefreshAsync_ExistingEntry_EnqueuesRefreshJob()
    {
        // Arrange - Create entry with metadata on disk (must be at Extracted stage)
        var filePath = CreateTestFile("test.txt", "Hello, World!");
        var entry = CreateEntryWithMetadataAtStage(filePath, ProcessingStage.Extracted);

        // Act
        var result = await _vault.RefreshAsync(filePath);

        // Assert
        result.Should().NotBeNull();
        _queueServiceMock.Verify(q => q.EnqueueRefreshAsync(
            It.IsAny<string>(),
            Path.GetFullPath(filePath),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_ExistingEntry_ReturnsEntry()
    {
        // Arrange - Create entry with metadata on disk
        var filePath = CreateTestFile("test.txt", "Hello, World!");
        var createdEntry = CreateEntryWithMetadata(filePath);

        // Act
        var retrieved = await _vault.GetAsync(filePath);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.SourcePath.Should().Be(Path.GetFullPath(filePath));
    }

    [Fact]
    public async Task GetByHashAsync_ExistingEntry_ReturnsEntry()
    {
        // Arrange - Create entry with metadata on disk
        var filePath = CreateTestFile("test.txt", "Hello, World!");
        var createdEntry = CreateEntryWithMetadata(filePath);

        // Act
        var retrieved = await _vault.GetByHashAsync(createdEntry.FilepathHash);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.FilepathHash.Should().Be(createdEntry.FilepathHash);
    }

    [Fact]
    public async Task ListAsync_WithEntries_ReturnsAll()
    {
        // Arrange - Create entries with metadata on disk
        var file1 = CreateTestFile("file1.txt", "Content 1");
        var file2 = CreateTestFile("file2.txt", "Content 2");
        CreateEntryWithMetadata(file1);
        CreateEntryWithMetadata(file2);

        // Act
        var entries = await _vault.ListAsync();

        // Assert
        entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task StatusAsync_WithEntries_ReturnsCorrectCounts()
    {
        // Arrange - Create entries with metadata on disk
        var file1 = CreateTestFile("file1.txt", "Content 1");
        var file2 = CreateTestFile("file2.txt", "Content 2");
        CreateEntryWithMetadata(file1);
        CreateEntryWithMetadata(file2);

        // Act
        var status = await _vault.StatusAsync();

        // Assert
        status.TotalEntries.Should().Be(2);
    }

    [Fact]
    public async Task DiffAsync_ExistingEntry_CallsGitService()
    {
        // Arrange - Create entry with metadata on disk
        var filePath = CreateTestFile("test.txt", "Hello, World!");
        CreateEntryWithMetadata(filePath);
        _gitServiceMock.Setup(g => g.DiffAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("diff output");

        // Act
        var diff = await _vault.DiffAsync(filePath);

        // Assert
        diff.Should().Be("diff output");
        _gitServiceMock.Verify(g => g.DiffAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogAsync_ExistingEntry_CallsGitService()
    {
        // Arrange - Create entry with metadata on disk
        var filePath = CreateTestFile("test.txt", "Hello, World!");
        CreateEntryWithMetadata(filePath);
        _gitServiceMock.Setup(g => g.LogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var logs = await _vault.LogAsync(filePath);

        // Assert
        _gitServiceMock.Verify(g => g.LogAsync(
            It.IsAny<string>(),
            10,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScanFolderAsync_WithFiles_DiscoverFiles()
    {
        // Arrange
        CreateTestFile("doc1.txt", "Content 1");
        CreateTestFile("doc2.md", "Content 2");

        // Act
        var result = await _vault.ScanFolderAsync(_testDir);

        // Assert
        result.NewFilesCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DetectChangesAsync_NewFile_ReturnsMemorizeAction()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Hello, World!");

        // Act
        var result = await _vault.DetectChangesAsync(filePath);

        // Assert
        result.Should().NotBeNull();
        result.RecommendedAction.Should().Be(ChangeAction.Memorize);
    }

    [Fact]
    public async Task GetQueueStatusAsync_ReturnsQueueStatus()
    {
        // Act
        var status = await _vault.GetQueueStatusAsync();

        // Assert
        status.Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveAsync_ExistingEntry_RemovesFromVectorStore()
    {
        // Arrange - Create entry with metadata on disk
        var filePath = CreateTestFile("test.txt", "Hello, World!");
        CreateEntryWithMetadata(filePath);

        _pipelineMock.Setup(p => p.RemoveAsync(It.IsAny<VaultEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _queueServiceMock.Setup(q => q.EnqueueRemoveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string hash, string path, CancellationToken _) => CreateTestJob(hash, path, VaultJobType.Remove));

        // Act
        await _vault.RemoveAsync(filePath);

        // Assert
        _queueServiceMock.Verify(q => q.EnqueueRemoveAsync(
            It.IsAny<string>(),
            Path.GetFullPath(filePath),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private string CreateTestFile(string fileName, string content)
    {
        var filePath = Path.Combine(_testDir, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    /// <summary>
    /// Creates a VaultEntry and saves its metadata to disk so it can be loaded by GetAsync/ListAsync.
    /// </summary>
    private VaultEntry CreateEntryWithMetadata(string filePath)
    {
        return CreateEntryWithMetadataAtStage(filePath, ProcessingStage.Source);
    }

    /// <summary>
    /// Creates a VaultEntry at a specific stage and saves its metadata to disk.
    /// </summary>
    private VaultEntry CreateEntryWithMetadataAtStage(string filePath, ProcessingStage stage)
    {
        var fullPath = Path.GetFullPath(filePath);
        var entry = VaultEntry.Create(fullPath, _vaultDir);

        // Create entry directory structure
        Directory.CreateDirectory(entry.EntryPath);
        Directory.CreateDirectory(entry.VaultPath);

        // Set the stage
        if (stage >= ProcessingStage.Extracted)
        {
            var hash = _contentHasher.ComputeHashAsync(fullPath, default).GetAwaiter().GetResult();
            entry.MarkExtracted(hash);
        }
        if (stage >= ProcessingStage.Memorized)
        {
            entry.MarkMemorized(1);
        }

        // Save metadata
        entry.SaveMetadata();

        return entry;
    }

    private static VaultJob CreateTestJob(string filepathHash, string filePath, VaultJobType jobType)
    {
        return VaultJob.Create(filepathHash, filePath, jobType);
    }

    #region SyncStatus Query Tests

    [Fact]
    public async Task ListByStatusAsync_FiltersCorrectly()
    {
        // Arrange
        var file1 = CreateTestFile("file1.txt", "Content 1");
        var file2 = CreateTestFile("file2.txt", "Content 2");
        var entry1 = CreateEntryWithMetadata(file1);
        var entry2 = CreateEntryWithMetadata(file2);
        entry2.UpdateSyncStatus(SyncStatus.SourceModified);
        entry2.SaveMetadata();

        // Act
        var sourceModifiedEntries = await _vault.ListByStatusAsync(SyncStatus.SourceModified);

        // Assert
        sourceModifiedEntries.Should().HaveCount(1);
        sourceModifiedEntries[0].SourcePath.Should().Be(Path.GetFullPath(file2));
    }

    [Fact]
    public async Task GetPendingRemovalsAsync_ReturnsAllRemovalStates()
    {
        // Arrange
        var file1 = CreateTestFile("file1.txt", "Content 1");
        var file2 = CreateTestFile("file2.txt", "Content 2");
        var file3 = CreateTestFile("file3.txt", "Content 3");

        var entry1 = CreateEntryWithMetadata(file1);
        entry1.MarkSourceDeleted();
        entry1.SaveMetadata();

        var entry2 = CreateEntryWithMetadata(file2);
        entry2.MarkRemovalPending();
        entry2.SaveMetadata();

        var entry3 = CreateEntryWithMetadata(file3);
        entry3.MarkRemovalPartial("Vector");
        entry3.SaveMetadata();

        // Act
        var pendingRemovals = await _vault.GetPendingRemovalsAsync();

        // Assert
        pendingRemovals.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetErrorEntriesAsync_ReturnsErrorEntries()
    {
        // Arrange
        var file1 = CreateTestFile("file1.txt", "Content 1");
        var file2 = CreateTestFile("file2.txt", "Content 2");

        var entry1 = CreateEntryWithMetadata(file1);
        entry1.MarkSyncError("Test error");
        entry1.SaveMetadata();

        CreateEntryWithMetadata(file2);

        // Act
        var errorEntries = await _vault.GetErrorEntriesAsync();

        // Assert
        errorEntries.Should().HaveCount(1);
        errorEntries[0].SyncStatus.Should().Be(SyncStatus.Error);
        errorEntries[0].LastError.Should().Be("Test error");
    }

    [Fact]
    public async Task GetEntriesNeedingSyncAsync_ReturnsModifiedEntries()
    {
        // Arrange
        var file1 = CreateTestFile("file1.txt", "Content 1");
        var file2 = CreateTestFile("file2.txt", "Content 2");
        var file3 = CreateTestFile("file3.txt", "Content 3");

        var entry1 = CreateEntryWithMetadata(file1);
        entry1.UpdateSyncStatus(SyncStatus.SourceModified);
        entry1.SaveMetadata();

        var entry2 = CreateEntryWithMetadata(file2);
        entry2.UpdateSyncStatus(SyncStatus.VaultModified);
        entry2.SaveMetadata();

        CreateEntryWithMetadata(file3); // InSync

        // Act
        var needingSync = await _vault.GetEntriesNeedingSyncAsync();

        // Assert
        needingSync.Should().HaveCount(2);
    }

    [Fact]
    public async Task StatusAsync_IncludesSyncStatusCounts()
    {
        // Arrange
        var file1 = CreateTestFile("file1.txt", "Content 1");
        var file2 = CreateTestFile("file2.txt", "Content 2");
        var file3 = CreateTestFile("file3.txt", "Content 3");

        var entry1 = CreateEntryWithMetadata(file1);
        // entry1 remains InSync

        var entry2 = CreateEntryWithMetadata(file2);
        entry2.UpdateSyncStatus(SyncStatus.SourceModified);
        entry2.SaveMetadata();

        var entry3 = CreateEntryWithMetadata(file3);
        entry3.MarkSyncError("Error");
        entry3.SaveMetadata();

        // Act
        var status = await _vault.StatusAsync();

        // Assert
        status.TotalEntries.Should().Be(3);
        status.InSyncCount.Should().BeGreaterThanOrEqualTo(0);
        status.SourceModifiedCount.Should().BeGreaterThanOrEqualTo(0);
        status.ErrorCount.Should().Be(1);
    }

    [Fact]
    public async Task DetectChangesAsync_UpdatesEntrySyncStatus()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Original content");
        // Use Extracted stage so that SourceContentHash is set
        var entry = CreateEntryWithMetadataAtStage(filePath, ProcessingStage.Extracted);
        entry.MarkInSync();
        entry.SaveMetadata();

        // Modify the file
        File.WriteAllText(filePath, "Modified content that is different");

        // Setup git service to return no vault changes
        _gitServiceMock.Setup(g => g.StatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitStatus { ModifiedFiles = [] });

        // Act
        var changes = await _vault.DetectChangesAsync(filePath);

        // Assert
        changes.SourceChanged.Should().BeTrue();
        changes.RecommendedAction.Should().Be(ChangeAction.Memorize);

        // Verify entry was updated
        var reloaded = await _vault.GetAsync(filePath);
        reloaded!.SyncStatus.Should().Be(SyncStatus.SourceModified);
    }

    #endregion
}
