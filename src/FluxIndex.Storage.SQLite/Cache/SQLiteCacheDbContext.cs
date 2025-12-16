using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FluxIndex.Storage.SQLite.Cache;

/// <summary>
/// SQLite 시맨틱 캐시용 DbContext
/// </summary>
public class SQLiteCacheDbContext : DbContext
{
    private readonly SQLiteCacheOptions _options;

    public SQLiteCacheDbContext(
        DbContextOptions<SQLiteCacheDbContext> options,
        IOptions<SQLiteCacheOptions> cacheOptions)
        : base(options)
    {
        _options = cacheOptions.Value;
    }

    public DbSet<SemanticCacheEntity> SemanticCache { get; set; } = null!;
    public DbSet<CacheStatsEntity> CacheStats { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // SemanticCacheEntity 설정
        modelBuilder.Entity<SemanticCacheEntity>(entity =>
        {
            entity.ToTable("semantic_cache");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.QueryHash)
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(e => e.Query)
                .IsRequired();

            entity.Property(e => e.EmbeddingJson)
                .HasColumnType("TEXT");

            entity.Property(e => e.ResultsJson)
                .HasColumnType("TEXT");

            entity.Property(e => e.MetadataJson)
                .HasColumnType("TEXT");

            // 인덱스
            entity.HasIndex(e => e.QueryHash);
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasIndex(e => e.LastAccessedAt);
            entity.HasIndex(e => e.HitCount);
        });

        // CacheStatsEntity 설정
        modelBuilder.Entity<CacheStatsEntity>(entity =>
        {
            entity.ToTable("cache_stats");
            entity.HasKey(e => e.Id);
        });
    }
}

/// <summary>
/// SQLite 캐시 옵션
/// </summary>
public class SQLiteCacheOptions : SQLiteOptions
{
    /// <summary>
    /// 캐시 데이터베이스 경로
    /// </summary>
    public string? CacheDatabasePath { get; set; }

    /// <summary>
    /// 기본 캐시 만료 시간 (기본: 1시간)
    /// </summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// 유사도 검색 임계값 (기본: 0.85)
    /// </summary>
    public float SimilarityThreshold { get; set; } = 0.85f;

    /// <summary>
    /// 최대 캐시 항목 수 (기본: 10000)
    /// </summary>
    public int MaxEntries { get; set; } = 10000;

    /// <summary>
    /// 자동 정리 활성화 (기본: true)
    /// </summary>
    public bool EnableAutoCleanup { get; set; } = true;

    /// <summary>
    /// 자동 정리 주기 (기본: 5분)
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 캐시 연결 문자열 반환
    /// </summary>
    public string GetCacheConnectionString()
    {
        if (!string.IsNullOrEmpty(CacheDatabasePath))
        {
            return UseInMemory
                ? "Data Source=:memory:"
                : $"Data Source={CacheDatabasePath}";
        }
        return GetConnectionString();
    }
}
