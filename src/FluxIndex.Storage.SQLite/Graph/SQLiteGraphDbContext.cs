using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FluxIndex.Storage.SQLite.Graph;

/// <summary>
/// SQLite Graph 저장소용 DbContext
/// </summary>
public class SQLiteGraphDbContext : DbContext
{
    private readonly SQLiteGraphOptions _options;

    public SQLiteGraphDbContext(
        DbContextOptions<SQLiteGraphDbContext> options,
        IOptions<SQLiteGraphOptions> graphOptions)
        : base(options)
    {
        _options = graphOptions.Value;
    }

    public DbSet<ChunkHierarchyEntity> ChunkHierarchies { get; set; } = null!;
    public DbSet<ChunkRelationshipEntity> ChunkRelationships { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ChunkHierarchy 설정
        modelBuilder.Entity<ChunkHierarchyEntity>(entity =>
        {
            entity.ToTable("chunk_hierarchies");
            entity.HasKey(e => e.ChunkId);

            entity.Property(e => e.ChunkId)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.ParentChunkId)
                .HasMaxLength(100);

            entity.Property(e => e.ChildChunkIdsJson)
                .HasColumnType("TEXT");

            entity.Property(e => e.BoundaryType)
                .HasMaxLength(50);

            // 인덱스
            entity.HasIndex(e => e.ParentChunkId);
            entity.HasIndex(e => e.HierarchyLevel);
            entity.HasIndex(e => e.CreatedAt);
        });

        // ChunkRelationship 설정
        modelBuilder.Entity<ChunkRelationshipEntity>(entity =>
        {
            entity.ToTable("chunk_relationships");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.SourceChunkId)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.TargetChunkId)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.Type)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.Direction)
                .HasMaxLength(50);

            entity.Property(e => e.Description)
                .HasMaxLength(500);

            entity.Property(e => e.MetadataJson)
                .HasColumnType("TEXT");

            // 인덱스 - 그래프 탐색 최적화
            entity.HasIndex(e => e.SourceChunkId);
            entity.HasIndex(e => e.TargetChunkId);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => new { e.SourceChunkId, e.Type });
            entity.HasIndex(e => new { e.TargetChunkId, e.Type });
        });
    }
}

/// <summary>
/// SQLite Graph 저장소 옵션
/// </summary>
public class SQLiteGraphOptions : SQLiteOptions
{
    /// <summary>
    /// 계층 데이터베이스 경로 (null이면 기본 경로 사용)
    /// </summary>
    public string? GraphDatabasePath { get; set; }

    /// <summary>
    /// Graph 전용 연결 문자열 반환
    /// </summary>
    public string GetGraphConnectionString()
    {
        if (!string.IsNullOrEmpty(GraphDatabasePath))
        {
            return UseInMemory
                ? "Data Source=:memory:"
                : $"Data Source={GraphDatabasePath}";
        }
        return GetConnectionString();
    }
}
