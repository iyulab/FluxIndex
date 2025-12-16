using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FluxIndex.Storage.PostgreSQL.Cache;

/// <summary>
/// PostgreSQL 시맨틱 캐시용 DbContext
/// </summary>
public class PostgresCacheDbContext : DbContext
{
    private readonly PostgresCacheOptions _options;

    public PostgresCacheDbContext(
        DbContextOptions<PostgresCacheDbContext> options,
        IOptions<PostgresCacheOptions> cacheOptions)
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

            // pgvector for embedding
            entity.Property(e => e.Embedding)
                .HasColumnType($"vector({_options.EmbeddingDimensions})");

            // JSONB for results and metadata
            entity.Property(e => e.Results)
                .HasColumnType("jsonb");

            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");

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
