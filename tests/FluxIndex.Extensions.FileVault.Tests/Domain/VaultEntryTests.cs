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
        var hash = ContentHash.FromHex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

        // Act
        var entry = VaultEntry.Create(sourcePath, hash, _testDir);

        // Assert
        entry.SourceHash.Should().Be(hash);
        entry.SourcePath.Should().Be(Path.GetFullPath(sourcePath));
        entry.Stage.Should().Be(ProcessingStage.Source);
        entry.VaultPath.Should().StartWith(_testDir);
    }

    [Fact]
    public void MarkExtracted_FromSource_TransitionsToExtracted()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act
        entry.MarkExtracted();

        // Assert
        entry.Stage.Should().Be(ProcessingStage.Extracted);
    }

    [Fact]
    public void MarkRefined_FromExtracted_TransitionsToRefined()
    {
        // Arrange
        var entry = CreateTestEntry();
        entry.MarkExtracted();

        // Act
        entry.MarkRefined();

        // Assert
        entry.Stage.Should().Be(ProcessingStage.Refined);
        entry.LastProcessedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void MarkRefined_WithManualEdit_SetsManualEditFlag()
    {
        // Arrange
        var entry = CreateTestEntry();
        entry.MarkExtracted();

        // Act
        entry.MarkRefined(isManualEdit: true);

        // Assert
        entry.Stage.Should().Be(ProcessingStage.Refined);
        entry.IsRefinedEdited.Should().BeTrue();
    }

    [Fact]
    public void MarkChunked_FromRefined_TransitionsToChunked()
    {
        // Arrange
        var entry = CreateTestEntry();
        entry.MarkExtracted();
        entry.MarkRefined();

        // Act
        entry.MarkChunked(10);

        // Assert
        entry.Stage.Should().Be(ProcessingStage.Chunked);
        entry.ChunkCount.Should().Be(10);
    }

    [Fact]
    public void MarkMemorized_FromChunked_TransitionsToMemorized()
    {
        // Arrange
        var entry = CreateTestEntry();
        entry.MarkExtracted();
        entry.MarkRefined();
        entry.MarkChunked(5);

        // Act
        entry.MarkMemorized();

        // Assert
        entry.Stage.Should().Be(ProcessingStage.Memorized);
    }

    [Fact]
    public void ResetToStage_FromMemorized_ResetsCorrectly()
    {
        // Arrange
        var entry = CreateTestEntry();
        entry.MarkExtracted();
        entry.MarkRefined();
        entry.MarkChunked(5);
        entry.MarkMemorized();

        // Act
        entry.ResetToStage(ProcessingStage.Refined);

        // Assert
        entry.Stage.Should().Be(ProcessingStage.Refined);
        entry.ChunkCount.Should().Be(0);
    }

    [Fact]
    public void ExtractedPath_ReturnsCorrectPath()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act & Assert
        entry.ExtractedPath.Should().EndWith("extracted.md");
        entry.ExtractedPath.Should().Contain(entry.VaultPath);
    }

    [Fact]
    public void RefinedPath_ReturnsCorrectPath()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act & Assert
        entry.RefinedPath.Should().EndWith("refined.md");
        entry.RefinedPath.Should().Contain(entry.VaultPath);
    }

    [Fact]
    public void ChunksPath_ReturnsCorrectPath()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act & Assert
        entry.ChunksPath.Should().EndWith("chunks");
        entry.ChunksPath.Should().Contain(entry.VaultPath);
    }

    [Fact]
    public void ManifestPath_ReturnsCorrectPath()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act & Assert
        entry.ManifestPath.Should().EndWith("manifest.json");
        entry.ManifestPath.Should().Contain(entry.ChunksPath);
    }

    [Fact]
    public void ImagesPath_ReturnsCorrectPath()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act & Assert
        entry.ImagesPath.Should().EndWith("images");
        entry.ImagesPath.Should().Contain(entry.VaultPath);
    }

    private VaultEntry CreateTestEntry()
    {
        var sourcePath = Path.Combine(_testDir, $"test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(sourcePath, "Test content");
        var hash = ContentHash.FromHex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        return VaultEntry.Create(sourcePath, hash, _testDir);
    }
}
