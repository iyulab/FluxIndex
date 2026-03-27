using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Globalization;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// sqlite-vec 확장을 지원하는 SQLite 데이터베이스 컨텍스트
/// </summary>
public partial class SQLiteVecDbContext : DbContext
{
    private readonly SQLiteVecOptions _options;
    private readonly ISQLiteVecExtensionLoader _extensionLoader;
    private readonly ILogger<SQLiteVecDbContext> _logger;

    public SQLiteVecDbContext(
        DbContextOptions<SQLiteVecDbContext> options,
        IOptions<SQLiteVecOptions> sqliteOptions,
        ISQLiteVecExtensionLoader extensionLoader,
        ILogger<SQLiteVecDbContext> logger)
        : base(options)
    {
        _options = sqliteOptions.Value;
        _extensionLoader = extensionLoader;
        _logger = logger;
    }

    // 메타데이터 테이블 (기존 방식과 호환)
    public DbSet<VectorChunkEntity> VectorChunks { get; set; }

    // 레거시 테이블 (마이그레이션 중에만 사용)
    public DbSet<VectorEntity> LegacyVectors { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // VectorChunkEntity 구성 (메타데이터만 저장, 벡터는 vec0 테이블에)
        modelBuilder.Entity<VectorChunkEntity>(entity =>
        {
            entity.ToTable("vector_chunks");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.DocumentId)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.Content)
                .IsRequired();

            // 메타데이터를 JSON으로 저장
            entity.Property(e => e.Metadata)
                .HasConversion(
                    new ValueConverter<Dictionary<string, object>, string>(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, object>()
                    ),
                    new ValueComparer<Dictionary<string, object>>(
                        (l, r) => JsonSerializer.Serialize(l) == JsonSerializer.Serialize(r),
                        v => v == null ? 0 : JsonSerializer.Serialize(v).GetHashCode(),
                        v => new Dictionary<string, object>(v ?? new())
                    )
                );

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // 인덱스
            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.ChunkIndex);
            entity.HasIndex(e => e.CreatedAt);
        });

        // 레거시 VectorEntity 구성 (마이그레이션 지원용)
        modelBuilder.Entity<VectorEntity>(entity =>
        {
            entity.ToTable("legacy_vectors");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.DocumentId)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.Content)
                .IsRequired();

            // 기존 방식: embedding을 JSON으로 저장
            entity.Property(e => e.Embedding)
                .HasConversion(
                    new ValueConverter<float[]?, string?>(
                        v => v != null ? JsonSerializer.Serialize(v, (JsonSerializerOptions?)null) : null,
                        v => !string.IsNullOrEmpty(v) ? JsonSerializer.Deserialize<float[]>(v, (JsonSerializerOptions?)null) : null
                    ),
                    new ValueComparer<float[]?>(
                        (l, r) => (l == null && r == null) || (l != null && r != null && l.SequenceEqual(r)),
                        v => v == null ? 0 : v.Aggregate(0, (acc, x) => HashCode.Combine(acc, x.GetHashCode())),
                        v => v == null ? null : v.ToArray()
                    )
                );

            entity.Property(e => e.Metadata)
                .HasConversion(
                    new ValueConverter<Dictionary<string, object>, string>(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, object>()
                    ),
                    new ValueComparer<Dictionary<string, object>>(
                        (l, r) => JsonSerializer.Serialize(l) == JsonSerializer.Serialize(r),
                        v => v == null ? 0 : JsonSerializer.Serialize(v).GetHashCode(),
                        v => new Dictionary<string, object>(v ?? new())
                    )
                );

            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.ChunkIndex);
        });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        if (_options.UseSQLiteVec)
        {
            // sqlite-vec 확장 사용 시 추가 설정
            optionsBuilder.LogTo(message => LogEfCoreSql(_logger, message));
        }
    }

    /// <summary>
    /// 데이터베이스 초기화 (sqlite-vec 확장 로드 및 vec0 테이블 생성)
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 기본 테이블 생성
            await Database.EnsureCreatedAsync(cancellationToken);

            if (_options.UseSQLiteVec)
            {
                await InitializeSQLiteVecAsync(cancellationToken);
            }

            if (_options.UseFts5)
            {
                await InitializeFts5Async(cancellationToken);
            }

            if (_options.AutoMigrateFromLegacy)
            {
                await MigrateFromLegacyAsync(cancellationToken);
            }

            LogDbInitialized(_logger);
        }
        catch (Exception ex)
        {
            LogDbInitFailed(_logger, ex);
            throw;
        }
    }

    /// <summary>
    /// FTS5 전문 검색 테이블 초기화
    /// </summary>
    private async Task InitializeFts5Async(CancellationToken cancellationToken)
    {
        try
        {
            var connection = Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            // FTS5 테이블이 이미 존재하는지 확인
            using var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='chunk_fts'";
            var tableExists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;

            if (!tableExists)
            {
                // FTS5 가상 테이블 생성 (콘텐츠 테이블과 연결)
                using var createCommand = connection.CreateCommand();
                createCommand.CommandText = $@"
                    CREATE VIRTUAL TABLE IF NOT EXISTS chunk_fts USING fts5(
                        content,
                        content='vector_chunks',
                        content_rowid='rowid',
                        tokenize='{_options.Fts5Tokenizer}'
                    )";
                await createCommand.ExecuteNonQueryAsync(cancellationToken);

                // FTS5 트리거 생성 (자동 동기화)
                // INSERT 트리거
                using var insertTrigger = connection.CreateCommand();
                insertTrigger.CommandText = @"
                    CREATE TRIGGER IF NOT EXISTS chunk_fts_insert AFTER INSERT ON vector_chunks BEGIN
                        INSERT INTO chunk_fts(rowid, content) VALUES (NEW.rowid, NEW.Content);
                    END";
                await insertTrigger.ExecuteNonQueryAsync(cancellationToken);

                // DELETE 트리거
                using var deleteTrigger = connection.CreateCommand();
                deleteTrigger.CommandText = @"
                    CREATE TRIGGER IF NOT EXISTS chunk_fts_delete AFTER DELETE ON vector_chunks BEGIN
                        INSERT INTO chunk_fts(chunk_fts, rowid, content) VALUES('delete', OLD.rowid, OLD.Content);
                    END";
                await deleteTrigger.ExecuteNonQueryAsync(cancellationToken);

                // UPDATE 트리거
                using var updateTrigger = connection.CreateCommand();
                updateTrigger.CommandText = @"
                    CREATE TRIGGER IF NOT EXISTS chunk_fts_update AFTER UPDATE ON vector_chunks BEGIN
                        INSERT INTO chunk_fts(chunk_fts, rowid, content) VALUES('delete', OLD.rowid, OLD.Content);
                        INSERT INTO chunk_fts(rowid, content) VALUES (NEW.rowid, NEW.Content);
                    END";
                await updateTrigger.ExecuteNonQueryAsync(cancellationToken);

                LogFts5Created(_logger, _options.Fts5Tokenizer);
            }
            else
            {
                LogFts5AlreadyExists(_logger);
            }
        }
        catch (Exception ex)
        {
            LogFts5InitFailed(_logger, ex);
            // FTS5 실패는 치명적이지 않음 - 벡터 검색은 계속 작동
        }
    }

    /// <summary>
    /// FTS5 인덱스 재구성 (기존 데이터 인덱싱)
    /// </summary>
    public async Task RebuildFts5IndexAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            LogFts5RebuildStarted(_logger);

            // 기존 FTS 데이터 삭제 후 재구성
            using var rebuildCommand = connection.CreateCommand();
            rebuildCommand.CommandText = @"
                INSERT INTO chunk_fts(chunk_fts) VALUES('delete-all');
                INSERT INTO chunk_fts(rowid, content) SELECT rowid, Content FROM vector_chunks";
            await rebuildCommand.ExecuteNonQueryAsync(cancellationToken);

            LogFts5RebuildCompleted(_logger);
        }
        catch (Exception ex)
        {
            LogFts5RebuildFailed(_logger, ex);
            throw;
        }
    }

    /// <summary>
    /// sqlite-vec 확장 초기화
    /// </summary>
    private async Task InitializeSQLiteVecAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connection = Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            // sqlite-vec 확장 로드
            var extensionLoaded = await _extensionLoader.LoadExtensionAsync((Microsoft.Data.Sqlite.SqliteConnection)connection, cancellationToken);

            if (extensionLoaded)
            {
                // Migrate legacy table if exists
                await MigrateLegacyVecTableAsync(
                    (Microsoft.Data.Sqlite.SqliteConnection)connection, cancellationToken);

                // vec0 가상 테이블 생성
                await _extensionLoader.CreateVecTableAsync(
                    (Microsoft.Data.Sqlite.SqliteConnection)connection,
                    _options.GetVecTableName(),
                    _options.VectorDimension,
                    _options.VecTableOptions,
                    cancellationToken);

                LogVecExtensionInitialized(_logger);
            }
            else
            {
                if (!_options.FallbackToInMemoryOnError)
                {
                    throw new InvalidOperationException(
                        "sqlite-vec 확장을 로드할 수 없습니다. " +
                        "확장 파일이 존재하는지 확인하거나 FallbackToInMemoryOnError 옵션을 활성화하세요.");
                }
                LogVecExtensionFallback(_logger);
            }
        }
        catch (Exception ex)
        {
            LogVecExtensionInitError(_logger, ex);

            if (!_options.FallbackToInMemoryOnError)
            {
                throw;
            }

            LogContinueWithFallback(_logger);
        }
    }

    /// <summary>
    /// Legacy "chunk_embeddings" 테이블을 차원 기반 이름으로 자동 마이그레이션
    /// </summary>
    private async Task MigrateLegacyVecTableAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        // Safe: GetVecTableName() returns "chunk_embeddings_{fingerprint}" — no injection risk
        var newTableName = _options.GetVecTableName();

        // Check if legacy "chunk_embeddings" table exists
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='chunk_embeddings'";
        var legacyExists = await checkCmd.ExecuteScalarAsync(cancellationToken) != null;

        if (!legacyExists)
            return;

        // Check if the new dimension-based table already exists
        using var checkNewCmd = connection.CreateCommand();
        checkNewCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@tableName";
        checkNewCmd.Parameters.AddWithValue("@tableName", newTableName);
        var newExists = await checkNewCmd.ExecuteScalarAsync(cancellationToken) != null;

        if (newExists)
        {
            // Both exist — drop legacy to avoid confusion
            using var dropCmd = connection.CreateCommand();
            dropCmd.CommandText = "DROP TABLE IF EXISTS chunk_embeddings";
            await dropCmd.ExecuteNonQueryAsync(cancellationToken);
            LogLegacyVecTableDropped(_logger, newTableName);
            return;
        }

        // vec0 virtual tables may not support ALTER TABLE RENAME
        // Use copy strategy: create new table, copy data, drop old
        try
        {
            // Try rename first (fastest)
            using var renameCmd = connection.CreateCommand();
            renameCmd.CommandText = $"ALTER TABLE chunk_embeddings RENAME TO {newTableName}";
            await renameCmd.ExecuteNonQueryAsync(cancellationToken);
            LogLegacyVecTableRenamed(_logger, newTableName);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // Rename failed (vec0 virtual tables may not support ALTER TABLE RENAME)
            // Fall back to copy strategy
            LogLegacyVecTableRenameFailed(_logger);

            // Create new table
            var createSql = _options.GetVecTableSchema();
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = createSql.Replace("CREATE VIRTUAL TABLE", "CREATE VIRTUAL TABLE IF NOT EXISTS");
            await createCmd.ExecuteNonQueryAsync(cancellationToken);

            // Copy data
            using var copyCmd = connection.CreateCommand();
            copyCmd.CommandText = $"INSERT INTO {newTableName} (chunk_id, embedding) SELECT chunk_id, embedding FROM chunk_embeddings";
            await copyCmd.ExecuteNonQueryAsync(cancellationToken);

            // Drop legacy
            using var dropCmd = connection.CreateCommand();
            dropCmd.CommandText = "DROP TABLE chunk_embeddings";
            await dropCmd.ExecuteNonQueryAsync(cancellationToken);

            LogLegacyVecTableCopied(_logger, newTableName);
        }
    }

    /// <summary>
    /// 레거시 데이터 마이그레이션
    /// </summary>
    private async Task MigrateFromLegacyAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 레거시 테이블이 존재하는지 확인
            var hasLegacyData = await Database.ExecuteSqlRawAsync(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='vectors'",
                cancellationToken) > 0;

            if (!hasLegacyData)
            {
                LogNoLegacyData(_logger);
                return;
            }

            var legacyCount = await LegacyVectors.CountAsync(cancellationToken);
            if (legacyCount == 0)
            {
                LogLegacyTableEmpty(_logger);
                return;
            }

            LogMigrationStarted(_logger, legacyCount);

            // 배치 단위로 마이그레이션
            const int batchSize = 1000;
            var processed = 0;

            while (processed < legacyCount)
            {
                var legacyBatch = await LegacyVectors
                    .Skip(processed)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken);

                foreach (var legacy in legacyBatch)
                {
                    // VectorChunkEntity로 변환
                    var chunk = new VectorChunkEntity
                    {
                        Id = legacy.Id,
                        DocumentId = legacy.DocumentId,
                        ChunkIndex = legacy.ChunkIndex,
                        Content = legacy.Content,
                        TokenCount = legacy.TokenCount,
                        Metadata = legacy.Metadata,
                        CreatedAt = DateTime.UtcNow
                    };

                    VectorChunks.Add(chunk);

                    // vec0 테이블에 벡터 저장 (sqlite-vec 사용 시)
                    if (_options.UseSQLiteVec && legacy.Embedding != null)
                    {
                        await StoreVectorInVecTableAsync(legacy.Id, legacy.Embedding, cancellationToken);
                    }
                }

                await SaveChangesAsync(cancellationToken);
                processed += legacyBatch.Count;

                LogMigrationProgress(_logger, processed, legacyCount);
            }

            LogMigrationCompleted(_logger);
        }
        catch (Exception ex)
        {
            LogMigrationFailed(_logger, ex);
            throw;
        }
    }

    /// <summary>
    /// vec0 테이블에 벡터 저장
    /// </summary>
    public async Task StoreVectorInVecTableAsync(string chunkId, float[] embedding, CancellationToken cancellationToken = default)
    {
        if (!_options.UseSQLiteVec || embedding == null)
            return;

        try
        {
            // 벡터를 적절한 형식으로 변환
            var vectorString = "[" + string.Join(",", embedding.Select(f => f.ToString("F6", CultureInfo.InvariantCulture))) + "]";

            // vec0 가상 테이블은 INSERT OR REPLACE를 지원하지 않으므로 DELETE + INSERT 사용
            var vecTable = _options.GetVecTableName();

            // 먼저 기존 벡터 삭제 (존재하지 않아도 오류 없음)
            var deleteSql = $"DELETE FROM {vecTable} WHERE chunk_id = {{0}}";
            await Database.ExecuteSqlRawAsync(
                deleteSql,
                new object[] { chunkId },
                cancellationToken);

            // 새 벡터 삽입
            var insertSql = $"INSERT INTO {vecTable} (chunk_id, embedding) VALUES ({{0}}, {{1}})";
            await Database.ExecuteSqlRawAsync(
                insertSql,
                new object[] { chunkId, vectorString },
                cancellationToken);
        }
        catch (Exception ex)
        {
            LogVecStoreError(_logger, ex, chunkId);
            throw;
        }
    }

    /// <summary>
    /// vec0 테이블에서 벡터 삭제
    /// </summary>
    public async Task DeleteVectorFromVecTableAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        if (!_options.UseSQLiteVec)
            return;

        try
        {
            var deleteSql = $"DELETE FROM {_options.GetVecTableName()} WHERE chunk_id = {{0}}";
            await Database.ExecuteSqlRawAsync(
                deleteSql,
                chunkId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            LogVecDeleteError(_logger, ex, chunkId);
            // 벡터 삭제 실패는 치명적이지 않으므로 로그만 남김
        }
    }

    /// <summary>
    /// sqlite-vec 확장 사용 여부 확인
    /// </summary>
    public async Task<bool> IsSQLiteVecAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.UseSQLiteVec)
            return false;

        try
        {
            var connection = Database.GetDbConnection();
            return await _extensionLoader.IsExtensionLoadedAsync((Microsoft.Data.Sqlite.SqliteConnection)connection, cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "EF Core SQL: {Message}")]
    private static partial void LogEfCoreSql(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "데이터베이스 초기화 완료")]
    private static partial void LogDbInitialized(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "데이터베이스 초기화 실패")]
    private static partial void LogDbInitFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "FTS5 테이블 생성 완료: tokenizer={Tokenizer}")]
    private static partial void LogFts5Created(ILogger logger, string tokenizer);

    [LoggerMessage(Level = LogLevel.Debug, Message = "FTS5 테이블이 이미 존재합니다")]
    private static partial void LogFts5AlreadyExists(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "FTS5 초기화 실패")]
    private static partial void LogFts5InitFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "FTS5 인덱스 재구성 시작")]
    private static partial void LogFts5RebuildStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "FTS5 인덱스 재구성 완료")]
    private static partial void LogFts5RebuildCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "FTS5 인덱스 재구성 실패")]
    private static partial void LogFts5RebuildFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "sqlite-vec 확장 초기화 완료")]
    private static partial void LogVecExtensionInitialized(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "sqlite-vec 확장 로드 실패, 폴백 모드 사용")]
    private static partial void LogVecExtensionFallback(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "sqlite-vec 확장 초기화 오류")]
    private static partial void LogVecExtensionInitError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "폴백 모드로 계속 진행")]
    private static partial void LogContinueWithFallback(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "레거시 데이터 없음")]
    private static partial void LogNoLegacyData(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "레거시 테이블이 비어있습니다")]
    private static partial void LogLegacyTableEmpty(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "레거시 데이터 마이그레이션 시작: {Count}건")]
    private static partial void LogMigrationStarted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "마이그레이션 진행: {Processed}/{Total}")]
    private static partial void LogMigrationProgress(ILogger logger, int processed, int total);

    [LoggerMessage(Level = LogLevel.Information, Message = "레거시 데이터 마이그레이션 완료")]
    private static partial void LogMigrationCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "레거시 데이터 마이그레이션 실패")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "vec0 테이블에 벡터 저장 실패: {ChunkId}")]
    private static partial void LogVecStoreError(ILogger logger, Exception exception, string chunkId);

    [LoggerMessage(Level = LogLevel.Error, Message = "vec0 테이블에서 벡터 삭제 실패: {ChunkId}")]
    private static partial void LogVecDeleteError(ILogger logger, Exception exception, string chunkId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dropped legacy chunk_embeddings table (dimension-based table {NewTable} already exists)")]
    private static partial void LogLegacyVecTableDropped(ILogger logger, string newTable);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrated legacy chunk_embeddings table to {NewTable} via rename")]
    private static partial void LogLegacyVecTableRenamed(ILogger logger, string newTable);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ALTER TABLE RENAME failed for vec0 table, using copy strategy")]
    private static partial void LogLegacyVecTableRenameFailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrated legacy chunk_embeddings table to {NewTable} via copy")]
    private static partial void LogLegacyVecTableCopied(ILogger logger, string newTable);

    #endregion
}

/// <summary>
/// 벡터 청크 메타데이터 엔티티 (sqlite-vec 사용 시)
/// </summary>
public class VectorChunkEntity
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}