using FluentAssertions;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Enums;
using FluxIndex.Extensions.FileVault.Domain.ValueObjects;
using Xunit;

namespace FluxIndex.Extensions.FileVault.Tests.Domain;

public class VaultEntryTests : IDisposable
{
    private readonly string _testDir;

    public VaultEntryTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "VaultEntryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public void Create_ValidPath_CreatesSourceStageEntry()
    {
        // Arrange
        var sourcePath = Path.Combine(_testDir, "test.txt");
        File.WriteAllText(sourcePath, "Hello");

        // Act
        var entry = VaultEntry.Create(sourcePath, _testDir);

        // Assert
        entry.SourcePath.Should().Be(Path.GetFullPath(sourcePath));
        entry.Stage.Should().Be(ProcessingStage.Source);
        entry.FilepathHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Create_SetsFilepathHashCorrectly()
    {
        // Arrange
        var sourcePath = Path.Combine(_testDir, "test.txt");
        File.WriteAllText(sourcePath, "Hello");

        // Act
        var entry = VaultEntry.Create(sourcePath, _testDir);

        // Assert
        entry.FilepathHash.Should().HaveLength(16); // First 8 bytes of SHA256 = 16 hex chars
        entry.FileName.Should().Be("test.txt");
    }

    [Fact]
    public void MarkExtracted_FromSource_TransitionsToExtracted()
    {
        // Arrange
        var entry = CreateTestEntry();
        var hash = ContentHash.FromHex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

        // Act
        entry.MarkExtracted(hash);

        // Assert
        entry.Stage.Should().Be(ProcessingStage.Extracted);
        entry.SourceContentHash.Should().Be(hash);
    }

    [Fact]
    public void MarkMemorized_FromExtracted_TransitionsToMemorized()
    {
        // Arrange
        var entry = CreateTestEntry();
        var hash = ContentHash.FromHex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        entry.MarkExtracted(hash);

        // Act
        entry.MarkMemorized(5);

        // Assert
        entry.Stage.Should().Be(ProcessingStage.Memorized);
        entry.ChunkCount.Should().Be(5);
        entry.LastProcessedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void MarkError_SetsErrorState()
    {
        // Arrange
        var entry = CreateTestEntry();
        var errorMessage = "Processing failed";

        // Act
        entry.MarkError(errorMessage);

        // Assert
        entry.LastError.Should().Be(errorMessage);
    }

    [Fact]
    public void ResetToSource_FromMemorized_ResetsCorrectly()
    {
        // Arrange
        var entry = CreateTestEntry();
        var hash = ContentHash.FromHex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        entry.MarkExtracted(hash);
        entry.MarkMemorized(5);

        // Act
        entry.ResetToSource();

        // Assert
        entry.Stage.Should().Be(ProcessingStage.Source);
        entry.ChunkCount.Should().Be(0);
    }

    [Fact]
    public void EntryPath_ReturnsCorrectPath()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act & Assert
        entry.EntryPath.Should().StartWith(_testDir);
        entry.EntryPath.Should().Contain(entry.FilepathHash);
    }

    [Fact]
    public void VaultPath_ReturnsCorrectPath()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act & Assert
        entry.VaultPath.Should().EndWith("vault");
        entry.VaultPath.Should().Contain(entry.EntryPath);
    }

    [Fact]
    public void RefinedMdPath_ReturnsCorrectPath()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act & Assert
        entry.RefinedMdPath.Should().EndWith("refined.md");
        entry.RefinedMdPath.Should().Contain(entry.VaultPath);
    }

    [Fact]
    public void MetaPath_ReturnsCorrectPath()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act & Assert
        entry.MetaPath.Should().EndWith("meta.json");
        entry.MetaPath.Should().Contain(entry.EntryPath);
    }

    [Fact]
    public void ImagesPath_ReturnsCorrectPath()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act & Assert
        entry.ImagesPath.Should().EndWith("images");
        entry.ImagesPath.Should().Contain(entry.EntryPath);
    }

    [Fact]
    public void SameSourcePath_ProducesSameFilepathHash()
    {
        // Arrange
        var sourcePath = Path.Combine(_testDir, "test.txt");
        File.WriteAllText(sourcePath, "Content 1");

        var entry1 = VaultEntry.Create(sourcePath, _testDir);

        File.WriteAllText(sourcePath, "Content 2"); // Different content
        var entry2 = VaultEntry.Create(sourcePath, _testDir);

        // Assert - Same path should produce same filepath hash
        entry1.FilepathHash.Should().Be(entry2.FilepathHash);
    }

    [Fact]
    public void DifferentSourcePaths_ProduceDifferentFilepathHashes()
    {
        // Arrange
        var sourcePath1 = Path.Combine(_testDir, "test1.txt");
        var sourcePath2 = Path.Combine(_testDir, "test2.txt");
        File.WriteAllText(sourcePath1, "Hello");
        File.WriteAllText(sourcePath2, "Hello"); // Same content

        var entry1 = VaultEntry.Create(sourcePath1, _testDir);
        var entry2 = VaultEntry.Create(sourcePath2, _testDir);

        // Assert - Different paths should produce different filepath hashes
        entry1.FilepathHash.Should().NotBe(entry2.FilepathHash);
    }

    #region SyncStatus Tests

    [Fact]
    public void Create_NewEntry_HasInSyncStatus()
    {
        // Arrange & Act
        var entry = CreateTestEntry();

        // Assert
        entry.SyncStatus.Should().Be(SyncStatus.InSync);
    }

    [Fact]
    public void UpdateSyncStatus_ChangesStatus()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act
        entry.UpdateSyncStatus(SyncStatus.SourceModified);

        // Assert
        entry.SyncStatus.Should().Be(SyncStatus.SourceModified);
        entry.LastSyncCheckAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void MarkSourceDeleted_SetsCorrectStatus()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act
        entry.MarkSourceDeleted();

        // Assert
        entry.SyncStatus.Should().Be(SyncStatus.SourceDeleted);
        entry.LastSyncCheckAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void MarkRemovalPending_SetsCorrectStatus()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act
        entry.MarkRemovalPending();

        // Assert
        entry.SyncStatus.Should().Be(SyncStatus.RemovalPending);
        entry.RemovalPhase.Should().BeNull();
    }

    [Fact]
    public void MarkRemovalPartial_SetsCorrectStatusAndPhase()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act
        entry.MarkRemovalPartial("Vector");

        // Assert
        entry.SyncStatus.Should().Be(SyncStatus.RemovalPartial);
        entry.RemovalPhase.Should().Be("Vector");
    }

    [Fact]
    public void MarkInSync_ClearsRemovalPhase()
    {
        // Arrange
        var entry = CreateTestEntry();
        entry.MarkRemovalPartial("Vector");

        // Act
        entry.MarkInSync();

        // Assert
        entry.SyncStatus.Should().Be(SyncStatus.InSync);
        entry.RemovalPhase.Should().BeNull();
        entry.LastError.Should().BeNull();
    }

    [Fact]
    public void MarkSyncError_SetsErrorStatus()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act
        entry.MarkSyncError("Test error");

        // Assert
        entry.SyncStatus.Should().Be(SyncStatus.Error);
        entry.LastError.Should().Be("Test error");
    }

    [Fact]
    public void ResetToSource_ResetsSyncStatus()
    {
        // Arrange
        var entry = CreateTestEntry();
        entry.MarkRemovalPartial("Vector");

        // Act
        entry.ResetToSource();

        // Assert
        entry.SyncStatus.Should().Be(SyncStatus.InSync);
        entry.RemovalPhase.Should().BeNull();
    }

    [Fact]
    public void UpdateSyncStatus_NonRemovalState_ClearsRemovalPhase()
    {
        // Arrange
        var entry = CreateTestEntry();
        entry.MarkRemovalPartial("Vector");

        // Act
        entry.UpdateSyncStatus(SyncStatus.SourceModified);

        // Assert
        entry.RemovalPhase.Should().BeNull();
    }

    [Fact]
    public void SyncStatus_SourceDeleted_TransitionsToRemovalPending()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act
        entry.MarkSourceDeleted();
        entry.MarkRemovalPending();

        // Assert - Verify correct state transition
        entry.SyncStatus.Should().Be(SyncStatus.RemovalPending);
    }

    [Fact]
    public void SyncStatus_RemovalPending_TransitionsToRemovalPartial()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act
        entry.MarkRemovalPending();
        entry.MarkRemovalPartial("Vector");

        // Assert
        entry.SyncStatus.Should().Be(SyncStatus.RemovalPartial);
        entry.RemovalPhase.Should().Be("Vector");
    }

    [Fact]
    public void SaveAndLoad_PreservesSyncStatus()
    {
        // Arrange
        var entry = CreateTestEntry();
        entry.MarkRemovalPartial("Vector");
        entry.SaveMetadata();

        // Act
        var loaded = VaultEntry.Load(entry.EntryPath, _testDir);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.SyncStatus.Should().Be(SyncStatus.RemovalPartial);
        loaded.RemovalPhase.Should().Be("Vector");
        loaded.LastSyncCheckAt.Should().BeCloseTo(entry.LastSyncCheckAt!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void MarkMemorized_WithMarkInSync_SetsInSyncStatus()
    {
        // Arrange
        var entry = CreateTestEntry();
        var hash = ContentHash.FromHex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        entry.MarkExtracted(hash);
        entry.UpdateSyncStatus(SyncStatus.SourceModified);

        // Act
        entry.MarkMemorized(5);
        entry.MarkInSync();

        // Assert
        entry.Stage.Should().Be(ProcessingStage.Memorized);
        entry.SyncStatus.Should().Be(SyncStatus.InSync);
    }

    #endregion

    #region EmbeddedDimension Tests

    [Fact]
    public void MarkMemorized_WithDimension_StoresDimension()
    {
        var entry = CreateTestEntry();
        var hash = ContentHash.FromHex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        entry.MarkExtracted(hash);

        entry.MarkMemorized(5, embeddedDimension: 384);

        entry.Stage.Should().Be(ProcessingStage.Memorized);
        entry.ChunkCount.Should().Be(5);
        entry.EmbeddedDimension.Should().Be(384);
    }

    [Fact]
    public void MarkMemorized_WithoutDimension_LeavesNull()
    {
        var entry = CreateTestEntry();
        var hash = ContentHash.FromHex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        entry.MarkExtracted(hash);

        entry.MarkMemorized(5);

        entry.EmbeddedDimension.Should().BeNull();
    }

    [Fact]
    public void SaveAndLoad_WithEmbeddedDimension_Roundtrips()
    {
        var entry = CreateTestEntry();
        var hash = ContentHash.FromHex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        entry.MarkExtracted(hash);
        entry.MarkMemorized(10, embeddedDimension: 1024);
        entry.SaveMetadata();

        var loaded = VaultEntry.Load(entry.EntryPath, entry.VaultBasePath);

        loaded.Should().NotBeNull();
        loaded!.EmbeddedDimension.Should().Be(1024);
        loaded.ChunkCount.Should().Be(10);
    }

    [Fact]
    public void Load_LegacyMetaWithoutDimension_ReturnsNullDimension()
    {
        var entry = CreateTestEntry();
        var hash = ContentHash.FromHex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        entry.MarkExtracted(hash);
        entry.MarkMemorized(5);
        entry.SaveMetadata();

        var loaded = VaultEntry.Load(entry.EntryPath, entry.VaultBasePath);

        loaded.Should().NotBeNull();
        loaded!.EmbeddedDimension.Should().BeNull();
    }

    #endregion

    private VaultEntry CreateTestEntry()
    {
        var sourcePath = Path.Combine(_testDir, $"test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(sourcePath, "Test content");
        return VaultEntry.Create(sourcePath, _testDir);
    }
}
