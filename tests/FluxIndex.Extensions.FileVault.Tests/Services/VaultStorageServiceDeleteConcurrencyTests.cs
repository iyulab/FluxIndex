using FluentAssertions;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using FluxIndex.Extensions.FileVault.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace FluxIndex.Extensions.FileVault.Tests.Services;

/// <summary>
/// Regression tests for the FileVault removal Phase-2 deletion race.
///
/// Bug (reported by Filer, 2026-06-10): a background remove job's
/// <see cref="VaultStorageService.DeleteEntryStorageAsync"/> failed with
/// "The process cannot access the file 'meta.json' because it is being used by another process."
/// whenever a concurrent <c>ListAsync</c> enumeration was reading meta.json — leaving the entry
/// directory on disk indefinitely (so a removed file kept showing in the user-facing list).
///
/// Root cause: <see cref="VaultEntry.Load"/> opened meta.json without <see cref="FileShare.Delete"/>,
/// so Windows blocked the parent <c>Directory.Delete</c> (ERROR_SHARING_VIOLATION).
/// Fix: open meta.json with FileShare.Delete + retry the directory delete through transient locks.
/// </summary>
public class VaultStorageServiceDeleteConcurrencyTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _vaultDir;
    private readonly VaultStorageService _storage;

    public VaultStorageServiceDeleteConcurrencyTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"VaultDeleteRaceTests_{Guid.NewGuid():N}");
        _vaultDir = Path.Combine(_testDir, ".vault");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_vaultDir);

        var gitMock = Substitute.For<IGitService>();
        _storage = new VaultStorageService(
            NullLogger<VaultStorageService>.Instance,
            gitMock,
            MsOptions.Create(new FileVaultOptions { VaultBasePath = _vaultDir }));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch { /* ignore cleanup errors */ }
        GC.SuppressFinalize(this);
    }

    private VaultEntry CreateEntryWithMetadata(string fileName)
    {
        var docPath = Path.Combine(_testDir, fileName);
        File.WriteAllText(docPath, "content");
        var entry = VaultEntry.Create(docPath, _vaultDir);
        entry.SaveMetadata();
        File.Exists(entry.MetaPath).Should().BeTrue();
        return entry;
    }

    [Fact]
    public async Task DeleteEntryStorageAsync_SucceedsWhileConcurrentMetadataReadsAreInFlight()
    {
        // Arrange: an entry on disk, and a hammer task that mimics ListAsync continuously
        // re-reading meta.json (VaultEntry.LoadByHash) — the exact polling pattern Filer's
        // Vault view uses. Without FileShare.Delete on the read path this collides with the delete.
        var entry = CreateEntryWithMetadata("concurrent-read.txt");

        using var stop = new CancellationTokenSource();
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                // Real production read path used by VaultManager.ListAsync.
                _ = VaultEntry.LoadByHash(entry.FilepathHash, _vaultDir);
            }
        });

        // Act: deletion must win despite the concurrent reads.
        await _storage.DeleteEntryStorageAsync(entry);
        stop.Cancel();
        await reader;

        // Assert
        Directory.Exists(entry.EntryPath).Should().BeFalse(
            "the entry directory must be physically removed even under concurrent meta.json reads");
    }

    [Fact]
    public async Task DeleteEntryStorageAsync_RetriesThroughTransientForeignLock()
    {
        // Arrange: a foreign holder (e.g. antivirus/indexer) holds meta.json WITHOUT share-delete,
        // then releases it within the retry window. The retry loop must absorb this and still delete.
        var entry = CreateEntryWithMetadata("transient-lock.txt");

        var handle = new FileStream(entry.MetaPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(async () =>
        {
            await Task.Delay(150);
            handle.Dispose();
        });

        // Act
        await _storage.DeleteEntryStorageAsync(entry);
        await release;

        // Assert
        Directory.Exists(entry.EntryPath).Should().BeFalse(
            "the retry loop should succeed once the transient lock is released");
    }
}
