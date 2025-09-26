using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// sqlite-vec 확장을 지원하는 SQLite 데이터베이스 컨텍스트
/// </summary>
public class SQLiteVecDbContext : DbContext
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
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, object>()
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
                    v => v != null ? JsonSerializer.Serialize(v, (JsonSerializerOptions?)null) : null,
                    v => !string.IsNullOrEmpty(v) ? JsonSerializer.Deserialize<float[]>(v, (JsonSerializerOptions?)null) : null
                );

            entity.Property(e => e.Metadata)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, object>()
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
            optionsBuilder.LogTo(message => _logger.LogDebug("EF Core SQL: {Message}", message));
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

            if (_options.AutoMigrateFromLegacy)
            {
                await MigrateFromLegacyAsync(cancellationToken);
            }

            _logger.LogInformation("SQLite 데이터베이스 초기화 완료");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQLite 데이터베이스 초기화 실패");
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
                // vec0 가상 테이블 생성
                await _extensionLoader.CreateVecTableAsync(
                    (Microsoft.Data.Sqlite.SqliteConnection)connection,
                    "chunk_embeddings",
                    _options.VectorDimension,
                    _options.VecTableOptions,
                    cancellationToken);

                _logger.LogInformation("sqlite-vec 확장 초기화 완료");
            }
            else
            {
                _logger.LogWarning("sqlite-vec 확장 로드 실패, in-memory 벡터 검색으로 폴백");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "sqlite-vec 확장 초기화 중 오류 발생");

            if (!_options.FallbackToInMemoryOnError)
            {
                throw;
            }

            _logger.LogInformation("폴백 모드로 계속 진행");
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
                _logger.LogInformation("마이그레이션할 레거시 데이터가 없음");
                return;
            }

            var legacyCount = await LegacyVectors.CountAsync(cancellationToken);
            if (legacyCount == 0)
            {
                _logger.LogInformation("레거시 테이블은 있지만 데이터가 없음");
                return;
            }

            _logger.LogInformation("레거시 데이터 마이그레이션 시작: {Count}개 항목", legacyCount);

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

                _logger.LogInformation("마이그레이션 진행률: {Processed}/{Total}", processed, legacyCount);
            }

            _logger.LogInformation("레거시 데이터 마이그레이션 완료");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "레거시 데이터 마이그레이션 실패");
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
            var vectorString = "[" + string.Join(",", embedding.Select(f => f.ToString("F6"))) + "]";

            await Database.ExecuteSqlRawAsync(
                $"INSERT OR REPLACE INTO chunk_embeddings (chunk_id, embedding) VALUES ('{chunkId}', '{vectorString}')",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "vec0 테이블에 벡터 저장 실패: {ChunkId}", chunkId);
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
            await Database.ExecuteSqlRawAsync(
                "DELETE FROM chunk_embeddings WHERE chunk_id = {0}",
                chunkId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "vec0 테이블에서 벡터 삭제 실패: {ChunkId}", chunkId);
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