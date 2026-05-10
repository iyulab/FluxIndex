using FluentAssertions;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Options;
using FluxIndex.Extensions.FileVault.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace FluxIndex.Extensions.FileVault.Tests.Services;

/// <summary>
/// Regression tests for the per-chunk checkpoint feature (B-5).
/// Verifies that VaultQueueService persists last_completed_chunk_index across
/// host restarts and that RecoverStuckJobsAsync does NOT reset the checkpoint.
/// </summary>
public class VaultQueueCheckpointTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileVaultOptions _options;

    public VaultQueueCheckpointTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "VaultCheckpointTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _options = new FileVaultOptions { VaultBasePath = _testDir };
    }

    public void Dispose()
    {
        try
        {
            // Force GC to release any lingering SqliteConnection finalizers before deleting files
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; OS may still hold file handles briefly.
        }
    }

    private VaultQueueService CreateService() =>
        new(NullLogger<VaultQueueService>.Instance, MsOptions.Create(_options));

    [Fact]
    public async Task UpdateCheckpointAsync_PersistsValue_AndSurvivesRecovery()
    {
        await using var queue = CreateServiceDisposable();

        var job = await queue.EnqueueMemorizeAsync("hash1", Path.Combine(_testDir, "file1.txt"));
        var dequeued = await queue.DequeueAsync();
        dequeued.Should().NotBeNull();
        dequeued!.LastCompletedChunkIndex.Should().Be(-1, "fresh job has no progress");

        // Simulate per-chunk progress
        await queue.UpdateCheckpointAsync(job.Id, 14);

        // Simulate host restart: stuck-job recovery resets status but preserves checkpoint
        var recovered = await queue.RecoverStuckJobsAsync();
        recovered.Should().Be(1);

        var resumed = await queue.DequeueAsync();
        resumed.Should().NotBeNull();
        resumed!.LastCompletedChunkIndex.Should().Be(14,
            "RecoverStuckJobsAsync MUST preserve last_completed_chunk_index so the embedding pipeline can resume");
    }

    [Fact]
    public async Task UpdateCheckpointAsync_NoOp_WhenJobNotProcessing()
    {
        await using var queue = CreateServiceDisposable();

        var job = await queue.EnqueueMemorizeAsync("hash2", Path.Combine(_testDir, "file2.txt"));
        // Status is Queued, not Processing — UpdateCheckpointAsync should be a no-op
        await queue.UpdateCheckpointAsync(job.Id, 5);

        var dequeued = await queue.DequeueAsync();
        dequeued.Should().NotBeNull();
        dequeued!.LastCompletedChunkIndex.Should().Be(-1,
            "UpdateCheckpointAsync only affects jobs in Processing status to avoid corrupting queued jobs");
    }

    [Fact]
    public async Task PreMigrationDatabase_GetsColumnAdded_WithDefaultMinusOne()
    {
        // Simulate a database from before the migration (column missing).
        var dbPath = Path.Combine(_testDir, "queue.db");
        await using (var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate"))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE vault_jobs (
                    id TEXT PRIMARY KEY,
                    file_path TEXT NOT NULL,
                    filepath_hash TEXT NOT NULL,
                    job_type INTEGER NOT NULL,
                    status INTEGER NOT NULL,
                    priority INTEGER NOT NULL,
                    queued_at TEXT NOT NULL,
                    started_at TEXT,
                    completed_at TEXT,
                    retry_count INTEGER NOT NULL DEFAULT 0,
                    max_retries INTEGER NOT NULL DEFAULT 3,
                    error_message TEXT
                );
                INSERT INTO vault_jobs (id, file_path, filepath_hash, job_type, status, priority, queued_at)
                VALUES (@id, @path, 'legacyhash', 0, 0, 1, '2026-05-10T00:00:00+00:00');
                """;
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@path", Path.Combine(_testDir, "legacy.txt"));
            await cmd.ExecuteNonQueryAsync();
        }

        // Construct VaultQueueService — its constructor runs the ALTER TABLE migration.
        await using var queue = CreateServiceDisposable();

        var dequeued = await queue.DequeueAsync();
        dequeued.Should().NotBeNull();
        dequeued!.LastCompletedChunkIndex.Should().Be(-1,
            "ALTER TABLE adds the column with DEFAULT -1 for legacy rows");
    }

    private DisposableQueueService CreateServiceDisposable() =>
        new(CreateService());

    /// <summary>
    /// Wraps VaultQueueService in IAsyncDisposable so the test can use 'await using'
    /// to ensure the service is disposed before Dispose() tries to delete the directory.
    /// </summary>
    private sealed class DisposableQueueService(VaultQueueService inner) : IAsyncDisposable
    {
        public Task<VaultJob> EnqueueMemorizeAsync(string hash, string path) =>
            inner.EnqueueMemorizeAsync(hash, path);
        public Task<VaultJob?> DequeueAsync() => inner.DequeueAsync();
        public Task UpdateCheckpointAsync(Guid id, int idx) => inner.UpdateCheckpointAsync(id, idx);
        public Task<int> RecoverStuckJobsAsync() => inner.RecoverStuckJobsAsync();

        public ValueTask DisposeAsync()
        {
            inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
