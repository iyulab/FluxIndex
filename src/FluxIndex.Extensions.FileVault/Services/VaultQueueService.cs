using System.Data;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// SQLite-backed vault processing queue service.
/// Provides persistence and crash recovery for processing jobs.
/// </summary>
public sealed partial class VaultQueueService : IVaultQueueService, IDisposable
{
    private readonly ILogger<VaultQueueService> _logger;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private readonly List<double> _processingTimes = [];
    private DateTimeOffset? _lastProcessedAt;
    private bool _isPaused;
    private bool _disposed;

    public bool IsPaused => _isPaused;

    public event EventHandler<VaultJob>? JobEnqueued;
    public event EventHandler<VaultJob>? JobCompleted;

    public VaultQueueService(
        ILogger<VaultQueueService> logger,
        IOptions<FileVaultOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var opts = options?.Value ?? new FileVaultOptions();
        var basePath = opts.VaultBasePath ?? Path.Combine(Directory.GetCurrentDirectory(), opts.VaultDirectoryName);
        Directory.CreateDirectory(basePath);

        var dbPath = Path.Combine(basePath, "queue.db");
        _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared";

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = CreateConnection();
        connection.Open();

        // Enable WAL mode for better concurrency
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
        }

        // Create jobs table
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS vault_jobs (
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
                    error_message TEXT,
                    last_completed_chunk_index INTEGER NOT NULL DEFAULT -1
                );

                CREATE INDEX IF NOT EXISTS idx_jobs_status ON vault_jobs(status);
                CREATE INDEX IF NOT EXISTS idx_jobs_priority ON vault_jobs(priority DESC, queued_at ASC);
                CREATE INDEX IF NOT EXISTS idx_jobs_filepath_hash ON vault_jobs(filepath_hash);
                """;
            cmd.ExecuteNonQuery();
        }

        // Migration: add last_completed_chunk_index column to pre-existing databases (idempotent).
        // CREATE TABLE IF NOT EXISTS above only includes the column for fresh databases;
        // existing tables from older versions need ALTER TABLE.
        using (var migrateCmd = connection.CreateCommand())
        {
            migrateCmd.CommandText =
                "ALTER TABLE vault_jobs ADD COLUMN last_completed_chunk_index INTEGER NOT NULL DEFAULT -1";
            try
            {
                migrateCmd.ExecuteNonQuery();
            }
            catch (SqliteException ex)
                when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // Column already exists — expected on every run after the first migration.
            }
        }

        LogDatabaseInitialized(_logger);
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    #region Enqueue Methods

    public Task<VaultJob> EnqueueMemorizeAsync(
        string filepathHash,
        string filePath,
        CancellationToken ct = default)
    {
        return EnqueueJobAsync(filepathHash, filePath, VaultJobType.Memorize, VaultJobPriority.Normal, ct);
    }

    public Task<VaultJob> EnqueueMemorizeAsync(
        string filepathHash,
        string filePath,
        VaultJobPriority priority,
        CancellationToken ct = default)
    {
        return EnqueueJobAsync(filepathHash, filePath, VaultJobType.Memorize, priority, ct);
    }

    public Task<VaultJob> EnqueueRefreshAsync(
        string filepathHash,
        string filePath,
        CancellationToken ct = default)
    {
        return EnqueueJobAsync(filepathHash, filePath, VaultJobType.Refresh, VaultJobPriority.Normal, ct);
    }

    public Task<VaultJob> EnqueueRefreshAsync(
        string filepathHash,
        string filePath,
        VaultJobPriority priority,
        CancellationToken ct = default)
    {
        return EnqueueJobAsync(filepathHash, filePath, VaultJobType.Refresh, priority, ct);
    }

    public Task<VaultJob> EnqueueRemoveAsync(
        string filepathHash,
        string filePath,
        CancellationToken ct = default)
    {
        return EnqueueJobAsync(filepathHash, filePath, VaultJobType.Remove, VaultJobPriority.Normal, ct);
    }

    public Task<VaultJob> EnqueueRemoveAsync(
        string filepathHash,
        string filePath,
        VaultJobPriority priority,
        CancellationToken ct = default)
    {
        return EnqueueJobAsync(filepathHash, filePath, VaultJobType.Remove, priority, ct);
    }

    private async Task<VaultJob> EnqueueJobAsync(
        string filepathHash,
        string filePath,
        VaultJobType jobType,
        VaultJobPriority priority,
        CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(filePath);
        var job = VaultJob.Create(fullPath, filepathHash, jobType, priority);

        await _dbLock.WaitAsync(ct);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO vault_jobs (id, file_path, filepath_hash, job_type, status, priority, queued_at, retry_count, max_retries)
                VALUES (@id, @file_path, @filepath_hash, @job_type, @status, @priority, @queued_at, @retry_count, @max_retries)
                """;

            cmd.Parameters.AddWithValue("@id", job.Id.ToString());
            cmd.Parameters.AddWithValue("@file_path", job.FilePath);
            cmd.Parameters.AddWithValue("@filepath_hash", job.FilepathHash);
            cmd.Parameters.AddWithValue("@job_type", (int)job.JobType);
            cmd.Parameters.AddWithValue("@status", (int)job.Status);
            cmd.Parameters.AddWithValue("@priority", (int)job.Priority);
            cmd.Parameters.AddWithValue("@queued_at", job.QueuedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@retry_count", job.RetryCount);
            cmd.Parameters.AddWithValue("@max_retries", 3);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _dbLock.Release();
        }

        LogEnqueued(_logger, jobType, filePath);
        JobEnqueued?.Invoke(this, job);

        return job;
    }

    public async Task<IReadOnlyList<VaultJob>> EnqueueBatchAsync(
        IEnumerable<(string FilepathHash, string FilePath)> files,
        VaultJobType jobType = VaultJobType.Memorize,
        VaultJobPriority priority = VaultJobPriority.Normal,
        CancellationToken ct = default)
    {
        var jobs = new List<VaultJob>();

        foreach (var (filepathHash, filePath) in files)
        {
            var job = await EnqueueJobAsync(filepathHash, filePath, jobType, priority, ct);
            jobs.Add(job);
        }

        return jobs;
    }

    #endregion

    #region Dequeue & Status Updates

    public async Task<VaultJob?> DequeueAsync(CancellationToken ct = default)
    {
        if (_isPaused)
            return null;

        await _dbLock.WaitAsync(ct);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);

            // Get highest priority queued job
            await using var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = """
                SELECT id, file_path, filepath_hash, job_type, status, priority, queued_at,
                       started_at, completed_at, retry_count, max_retries, error_message,
                       last_completed_chunk_index
                FROM vault_jobs
                WHERE status = @status
                ORDER BY priority DESC, queued_at ASC
                LIMIT 1
                """;
            selectCmd.Parameters.AddWithValue("@status", (int)VaultJobStatus.Queued);

            await using var reader = await selectCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            var job = ReadJob(reader);

            // Update to processing
            await using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = """
                UPDATE vault_jobs
                SET status = @status, started_at = @started_at
                WHERE id = @id
                """;
            updateCmd.Parameters.AddWithValue("@id", job.Id.ToString());
            updateCmd.Parameters.AddWithValue("@status", (int)VaultJobStatus.Processing);
            updateCmd.Parameters.AddWithValue("@started_at", DateTimeOffset.UtcNow.ToString("O"));

            await updateCmd.ExecuteNonQueryAsync(ct);

            job.TryStart();
            LogDequeued(_logger, job.Id, job.FilePath);

            return job;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task CompleteAsync(Guid jobId, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);

            var completedAt = DateTimeOffset.UtcNow;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                UPDATE vault_jobs
                SET status = @status, completed_at = @completed_at, error_message = NULL
                WHERE id = @id
                """;
            cmd.Parameters.AddWithValue("@id", jobId.ToString());
            cmd.Parameters.AddWithValue("@status", (int)VaultJobStatus.Completed);
            cmd.Parameters.AddWithValue("@completed_at", completedAt.ToString("O"));

            await cmd.ExecuteNonQueryAsync(ct);

            _lastProcessedAt = completedAt;
            LogCompleted(_logger, jobId);

            var job = await GetJobInternalAsync(connection, jobId, ct);
            if (job != null)
            {
                JobCompleted?.Invoke(this, job);

                // Track processing time
                if (job.Duration.HasValue)
                {
                    lock (_processingTimes)
                    {
                        _processingTimes.Add(job.Duration.Value.TotalMilliseconds);
                        if (_processingTimes.Count > 100)
                            _processingTimes.RemoveAt(0);
                    }
                }
            }
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task FailAsync(Guid jobId, string errorMessage, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                UPDATE vault_jobs
                SET status = @status, completed_at = @completed_at, error_message = @error_message
                WHERE id = @id
                """;
            cmd.Parameters.AddWithValue("@id", jobId.ToString());
            cmd.Parameters.AddWithValue("@status", (int)VaultJobStatus.Failed);
            cmd.Parameters.AddWithValue("@completed_at", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@error_message", errorMessage);

            await cmd.ExecuteNonQueryAsync(ct);
            LogFailed(_logger, jobId, errorMessage);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<bool> RetryAsync(Guid jobId, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);

            // Check if retry is allowed
            var job = await GetJobInternalAsync(connection, jobId, ct);
            if (job == null || !job.CanRetry)
                return false;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                UPDATE vault_jobs
                SET status = @status, started_at = NULL, completed_at = NULL,
                    error_message = NULL, retry_count = retry_count + 1
                WHERE id = @id AND status = @failed_status AND retry_count < max_retries
                """;
            cmd.Parameters.AddWithValue("@id", jobId.ToString());
            cmd.Parameters.AddWithValue("@status", (int)VaultJobStatus.Queued);
            cmd.Parameters.AddWithValue("@failed_status", (int)VaultJobStatus.Failed);

            var rows = await cmd.ExecuteNonQueryAsync(ct);
            if (rows > 0)
            {
                LogRetrying(_logger, jobId, job.RetryCount + 1);
                return true;
            }

            return false;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<bool> CancelAsync(Guid jobId, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                UPDATE vault_jobs
                SET status = @status, completed_at = @completed_at
                WHERE id = @id AND status IN (@queued, @processing)
                """;
            cmd.Parameters.AddWithValue("@id", jobId.ToString());
            cmd.Parameters.AddWithValue("@status", (int)VaultJobStatus.Cancelled);
            cmd.Parameters.AddWithValue("@completed_at", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@queued", (int)VaultJobStatus.Queued);
            cmd.Parameters.AddWithValue("@processing", (int)VaultJobStatus.Processing);

            var rows = await cmd.ExecuteNonQueryAsync(ct);
            if (rows > 0)
            {
                LogCancelled(_logger, jobId);
                return true;
            }

            return false;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    #endregion

    #region Query Methods

    public async Task<VaultJob?> GetJobAsync(Guid jobId, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            return await GetJobInternalAsync(connection, jobId, ct);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private static async Task<VaultJob?> GetJobInternalAsync(SqliteConnection connection, Guid jobId, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, file_path, filepath_hash, job_type, status, priority, queued_at,
                   started_at, completed_at, retry_count, max_retries, error_message,
                   last_completed_chunk_index
            FROM vault_jobs
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", jobId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return ReadJob(reader);

        return null;
    }

    public async Task<IReadOnlyList<VaultJob>> GetJobsAsync(
        VaultJobStatus? statusFilter = null,
        VaultJobType? typeFilter = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);

            var sql = """
                SELECT id, file_path, filepath_hash, job_type, status, priority, queued_at,
                       started_at, completed_at, retry_count, max_retries, error_message,
                       last_completed_chunk_index
                FROM vault_jobs
                WHERE 1=1
                """;

            if (statusFilter.HasValue)
                sql += " AND status = @status";
            if (typeFilter.HasValue)
                sql += " AND job_type = @job_type";

            sql += " ORDER BY priority DESC, queued_at ASC";

            if (limit.HasValue)
                sql += $" LIMIT {limit.Value}";

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;

            if (statusFilter.HasValue)
                cmd.Parameters.AddWithValue("@status", (int)statusFilter.Value);
            if (typeFilter.HasValue)
                cmd.Parameters.AddWithValue("@job_type", (int)typeFilter.Value);

            var jobs = new List<VaultJob>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                jobs.Add(ReadJob(reader));
            }

            return jobs;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<QueueStatistics> GetStatisticsAsync(CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT status, COUNT(*) as count
                FROM vault_jobs
                GROUP BY status
                """;

            var counts = new Dictionary<VaultJobStatus, int>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var status = (VaultJobStatus)reader.GetInt32(0);
                var count = reader.GetInt32(1);
                counts[status] = count;
            }

            double avgTime;
            lock (_processingTimes)
            {
                avgTime = _processingTimes.Count > 0 ? _processingTimes.Average() : 0;
            }

            return new QueueStatistics
            {
                QueuedCount = counts.GetValueOrDefault(VaultJobStatus.Queued),
                ProcessingCount = counts.GetValueOrDefault(VaultJobStatus.Processing),
                CompletedCount = counts.GetValueOrDefault(VaultJobStatus.Completed),
                FailedCount = counts.GetValueOrDefault(VaultJobStatus.Failed),
                CancelledCount = counts.GetValueOrDefault(VaultJobStatus.Cancelled),
                IsPaused = _isPaused,
                LastProcessedAt = _lastProcessedAt,
                AverageProcessingTimeMs = avgTime
            };
        }
        finally
        {
            _dbLock.Release();
        }
    }

    #endregion

    #region Recovery & Cleanup

    public async Task<int> RecoverStuckJobsAsync(CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                UPDATE vault_jobs
                SET status = @queued, started_at = NULL
                WHERE status = @processing
                """;
            cmd.Parameters.AddWithValue("@queued", (int)VaultJobStatus.Queued);
            cmd.Parameters.AddWithValue("@processing", (int)VaultJobStatus.Processing);

            var recovered = await cmd.ExecuteNonQueryAsync(ct);

            if (recovered > 0)
            {
                LogRecovered(_logger, recovered);
            }

            return recovered;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task UpdateCheckpointAsync(Guid jobId, int lastCompletedChunkIndex, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                UPDATE vault_jobs
                SET last_completed_chunk_index = @checkpoint
                WHERE id = @id AND status = @processing
                """;
            cmd.Parameters.AddWithValue("@checkpoint", lastCompletedChunkIndex);
            cmd.Parameters.AddWithValue("@id", jobId.ToString());
            cmd.Parameters.AddWithValue("@processing", (int)VaultJobStatus.Processing);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<int> ClearCompletedAsync(CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                DELETE FROM vault_jobs
                WHERE status IN (@completed, @cancelled)
                """;
            cmd.Parameters.AddWithValue("@completed", (int)VaultJobStatus.Completed);
            cmd.Parameters.AddWithValue("@cancelled", (int)VaultJobStatus.Cancelled);

            var deleted = await cmd.ExecuteNonQueryAsync(ct);
            LogClearedCompleted(_logger, deleted);

            return deleted;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<int> ClearFailedAsync(CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                DELETE FROM vault_jobs
                WHERE status = @failed
                """;
            cmd.Parameters.AddWithValue("@failed", (int)VaultJobStatus.Failed);

            var deleted = await cmd.ExecuteNonQueryAsync(ct);
            LogClearedFailed(_logger, deleted);

            return deleted;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM vault_jobs";
            await cmd.ExecuteNonQueryAsync(ct);

            LogClearedAll(_logger);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    #endregion

    #region Pause/Resume

    public void Pause()
    {
        _isPaused = true;
        LogPaused(_logger);
    }

    public void ResumeProcessing()
    {
        _isPaused = false;
        LogResumed(_logger);
    }

    #endregion

    #region Helpers

    private static VaultJob ReadJob(SqliteDataReader reader)
    {
        return VaultJob.Restore(
            id: Guid.Parse(reader.GetString(0)),
            filePath: reader.GetString(1),
            filepathHash: reader.GetString(2),
            jobType: (VaultJobType)reader.GetInt32(3),
            status: (VaultJobStatus)reader.GetInt32(4),
            priority: (VaultJobPriority)reader.GetInt32(5),
            queuedAt: DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
            startedAt: reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
            completedAt: reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture),
            retryCount: reader.GetInt32(9),
            maxRetries: reader.GetInt32(10),
            errorMessage: reader.IsDBNull(11) ? null : reader.GetString(11),
            lastCompletedChunkIndex: reader.IsDBNull(12) ? -1 : reader.GetInt32(12)
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _dbLock.Dispose();
        _disposed = true;
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Vault queue database initialized")]
    private static partial void LogDatabaseInitialized(ILogger logger);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Enqueued {JobType} job for {FilePath}")]
    private static partial void LogEnqueued(ILogger logger, VaultJobType jobType, string filePath);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Dequeued job {JobId} for {FilePath}")]
    private static partial void LogDequeued(ILogger logger, Guid jobId, string filePath);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Completed job {JobId}")]
    private static partial void LogCompleted(ILogger logger, Guid jobId);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed job {JobId}: {Error}")]
    private static partial void LogFailed(ILogger logger, Guid jobId, string error);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Retrying job {JobId}, attempt {RetryCount}")]
    private static partial void LogRetrying(ILogger logger, Guid jobId, int retryCount);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Cancelled job {JobId}")]
    private static partial void LogCancelled(ILogger logger, Guid jobId);
    [LoggerMessage(Level = LogLevel.Information, Message = "Recovered {Count} stuck processing jobs")]
    private static partial void LogRecovered(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Cleared {Count} completed/cancelled jobs")]
    private static partial void LogClearedCompleted(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Cleared {Count} failed jobs")]
    private static partial void LogClearedFailed(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Information, Message = "Cleared all queue jobs")]
    private static partial void LogClearedAll(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Queue processing paused")]
    private static partial void LogPaused(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Queue processing resumed")]
    private static partial void LogResumed(ILogger logger);

    #endregion
}
