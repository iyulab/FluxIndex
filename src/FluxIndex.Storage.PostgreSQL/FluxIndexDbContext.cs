using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector.EntityFrameworkCore;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL database context for FluxIndex
/// </summary>
public class FluxIndexDbContext : DbContext
{
    private readonly PostgreSQLOptions _options;

    public FluxIndexDbContext(DbContextOptions<FluxIndexDbContext> options, IOptions<PostgreSQLOptions> postgresOptions)
        : base(options)
    {
        _options = postgresOptions.Value;
    }

    public DbSet<VectorEntity> Vectors { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        // Configure VectorEntity
        modelBuilder.Entity<VectorEntity>(entity =>
        {
            entity.ToTable("vectors");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.DocumentId)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Content)
                .IsRequired();

            entity.Property(e => e.Embedding)
                .HasColumnType($"vector({_options.EmbeddingDimensions})");

            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");

            // Indexes for performance
            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.ChunkIndex);

            // Vector similarity index. HNSW (not ivfflat): ivfflat trains centroids at CREATE
            // INDEX time, so an index created on an empty table (EnsureCreated) silently loses
            // recall for data inserted afterwards. HNSW builds incrementally and has no such
            // training requirement (requires pgvector >= 0.5).
            entity.HasIndex(e => e.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
        });
    }
}