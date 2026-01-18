using FluentAssertions;
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
        _fileWatcherMock = new Mock<IFileWatcherService>();
        _storageMock = new Mock<IVaultStorageService>();

        // Setup default mock returns
        _fileWatcherMock.Setup(f => f.GetAllWatchers()).Returns(new List<WatcherInfo>());
        _storageMock.Setup(s => s.GetStorageSizeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        // Create test directories
        _testDir = Path.Combine(Path.GetTempPath(), "FileVaultTests_" + Guid.NewGuid().ToString("N"));
        _vaultDir = Path.Combine(_testDir, ".fluxindex");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_vaultDir);

        var options = MsOptions.Create(new FileVaultOptions
        {
            VaultBasePath = _vaultDir
        });

        var logger = NullLogger<VaultManager>.Instance;

        _vault = new VaultManager(
            _contentHasher,
            _gitServiceMock.Object,
            _pipelineMock.Object,
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
    public async Task AddAsync_NewFile_CreatesVaultEntry()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Hello, World!");

        // Act
        var result = await _vault.AddAsync(filePath);

        // Assert
        result.Should().NotBeNull();
        result.SourcePath.Should().Be(Path.GetFullPath(filePath));
        result.Stage.Should().Be(ProcessingStage.Source);
        result.SourceHash.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_ExistingFile_ReturnsSameEntry()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Hello, World!");
        var first = await _vault.AddAsync(filePath);

        // Act
        var second = await _vault.AddAsync(filePath);

        // Assert
        second.SourceHash.Should().Be(first.SourceHash);
        second.VaultPath.Should().Be(first.VaultPath);
    }

    [Fact]
    public async Task AddAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDir, "nonexistent.txt");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _vault.AddAsync(nonExistentPath));
    }

    [Fact]
    public async Task GetAsync_ExistingEntry_ReturnsEntry()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Hello, World!");
        var added = await _vault.AddAsync(filePath);

        // Act
        var retrieved = await _vault.GetAsync(filePath);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.SourceHash.Should().Be(added.SourceHash);
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
    public async Task RemoveAsync_ExistingEntry_RemovesEntry()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Hello, World!");
        await _vault.AddAsync(filePath);

        // Act
        await _vault.RemoveAsync(filePath);

        // Assert
        var result = await _vault.GetAsync(filePath);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_MultipleEntries_ReturnsAll()
    {
        // Arrange
        var file1 = CreateTestFile("file1.txt", "Content 1");
        var file2 = CreateTestFile("file2.txt", "Content 2");
        var file3 = CreateTestFile("file3.txt", "Content 3");

        await _vault.AddAsync(file1);
        await _vault.AddAsync(file2);
        await _vault.AddAsync(file3);

        // Act
        var entries = await _vault.ListAsync();

        // Assert
        entries.Should().HaveCount(3);
    }

    [Fact]
    public async Task StatusAsync_MixedEntries_ReturnsCorrectCounts()
    {
        // Arrange
        var file1 = CreateTestFile("file1.txt", "Content 1");
        var file2 = CreateTestFile("file2.txt", "Content 2");

        await _vault.AddAsync(file1);
        await _vault.AddAsync(file2);

        // Act
        var status = await _vault.StatusAsync();

        // Assert
        status.TotalEntries.Should().Be(2);
        status.SourceCount.Should().Be(2);
    }

    [Fact]
    public async Task ExtractAsync_ValidEntry_CallsPipeline()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Hello, World!");
        await _vault.AddAsync(filePath);

        // Act
        await _vault.ExtractAsync(filePath);

        // Assert
        _pipelineMock.Verify(p => p.ExtractAsync(
            It.IsAny<FluxIndex.Extensions.FileVault.Domain.Entities.VaultEntry>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefineAsync_ValidEntry_CallsPipeline()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Hello, World!");
        await _vault.AddAsync(filePath);

        // Act
        await _vault.RefineAsync(filePath);

        // Assert
        _pipelineMock.Verify(p => p.RefineAsync(
            It.IsAny<FluxIndex.Extensions.FileVault.Domain.Entities.VaultEntry>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChunkAsync_ValidEntry_CallsPipeline()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Hello, World!");
        await _vault.AddAsync(filePath);

        // Act
        await _vault.ChunkAsync(filePath);

        // Assert
        _pipelineMock.Verify(p => p.ChunkAsync(
            It.IsAny<FluxIndex.Extensions.FileVault.Domain.Entities.VaultEntry>(),
            It.IsAny<ChunkingOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MemorizeAsync_ValidEntry_CallsPipeline()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Hello, World!");
        await _vault.AddAsync(filePath);

        // Act
        await _vault.MemorizeAsync(filePath);

        // Assert
        _pipelineMock.Verify(p => p.MemorizeAsync(
            It.IsAny<FluxIndex.Extensions.FileVault.Domain.Entities.VaultEntry>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NewFile_ProcessesAllStages()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Hello, World!");

        // Act
        var result = await _vault.ProcessAsync(filePath);

        // Assert
        result.Should().NotBeNull();
        _pipelineMock.Verify(p => p.ProcessToStageAsync(
            It.IsAny<FluxIndex.Extensions.FileVault.Domain.Entities.VaultEntry>(),
            ProcessingStage.Memorized,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DiffAsync_ValidEntry_CallsGitService()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Hello, World!");
        var entry = await _vault.AddAsync(filePath);
        _gitServiceMock.Setup(g => g.DiffAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("diff output");

        // Act
        var diff = await _vault.DiffAsync(filePath);

        // Assert
        diff.Should().Be("diff output");
        _gitServiceMock.Verify(g => g.DiffAsync(
            entry.VaultPath,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogAsync_ValidEntry_CallsGitService()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Hello, World!");
        var entry = await _vault.AddAsync(filePath);
        _gitServiceMock.Setup(g => g.LogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GitCommit>());

        // Act
        var logs = await _vault.LogAsync(filePath);

        // Assert
        _gitServiceMock.Verify(g => g.LogAsync(
            entry.VaultPath,
            10,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScanFolderAsync_WithFiles_DiscoverFiles()
    {
        // Arrange
        CreateTestFile("doc1.txt", "Content 1");
        CreateTestFile("doc2.md", "Content 2");
        CreateTestFile("doc3.pdf", "Content 3");

        // Act
        var result = await _vault.ScanFolderAsync(_testDir);

        // Assert
        result.NewFilesCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetByHashAsync_ExistingHash_ReturnsEntry()
    {
        // Arrange
        var filePath = CreateTestFile("test.txt", "Hello, World!");
        var entry = await _vault.AddAsync(filePath);

        // Act
        var retrieved = await _vault.GetByHashAsync(entry.SourceHash.ToString());

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.SourcePath.Should().Be(entry.SourcePath);
    }

    private string CreateTestFile(string fileName, string content)
    {
        var filePath = Path.Combine(_testDir, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }
}
