using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Storage.PostgreSQL.Graph;

/// <summary>
/// PostgreSQL 그래프 저장소용 DbContext
/// </summary>
public class PostgresGraphDbContext : DbContext
{
    public PostgresGraphDbContext(DbContextOptions<PostgresGraphDbContext> options)
        : base(options)
    {
    }

    public DbSet<ChunkHierarchyEntity> ChunkHierarchies { get; set; } = null!;
    public DbSet<ChunkRelationshipEntity> ChunkRelationships { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ChunkHierarchyEntity 설정
        modelBuilder.Entity<ChunkHierarchyEntity>(entity =>
        {
            entity.ToTable("chunk_hierarchies");
            entity.HasKey(e => e.ChunkId);

            entity.Property(e => e.ChunkId)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.ParentChunkId)
                .HasMaxLength(200);

            // JSONB 배열로 자식 ID 저장
            entity.Property(e => e.ChildChunkIds)
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'[]'::jsonb");

            entity.Property(e => e.BoundaryType)
                .HasMaxLength(50);

            // JSONB로 확장 메타데이터 저장
            entity.Property(e => e.ExtendedMetadata)
                .HasColumnType("jsonb");

            // 인덱스
            entity.HasIndex(e => e.ParentChunkId);
            entity.HasIndex(e => e.HierarchyLevel);
            entity.HasIndex(e => e.BoundaryStartPosition);

            // GIN 인덱스 for JSONB ChildChunkIds (contains query 지원)
            entity.HasIndex(e => e.ChildChunkIds)
                .HasMethod("gin");
        });

        // ChunkRelationshipEntity 설정
        modelBuilder.Entity<ChunkRelationshipEntity>(entity =>
        {
            entity.ToTable("chunk_relationships");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.SourceChunkId)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.TargetChunkId)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.Type)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.Direction)
                .HasMaxLength(50);

            entity.Property(e => e.Description)
                .HasMaxLength(1000);

            // JSONB로 메타데이터 저장
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");

            // 인덱스
            entity.HasIndex(e => e.SourceChunkId);
            entity.HasIndex(e => e.TargetChunkId);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.Strength);

            // 복합 인덱스 for 관계 조회
            entity.HasIndex(e => new { e.SourceChunkId, e.Type });
            entity.HasIndex(e => new { e.TargetChunkId, e.Type });
        });
    }
}
