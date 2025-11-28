using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;
using System.Text.Json;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// EF Core DbContext for PostgreSQL storage with quantized vector support.
/// </summary>
public class FluxIndexQuantizedDbContext : DbContext
{
    private readonly PostgreSQLQuantizedOptions _options;

    public FluxIndexQuantizedDbContext(
        DbContextOptions<FluxIndexQuantizedDbContext> options,
        IOptions<PostgreSQLQuantizedOptions> postgresOptions)
        : base(options)
    {
        _options = postgresOptions.Value;
    }

    public DbSet<QuantizedVectorEntity> Vectors { get; set; }
    public DbSet<PostgresQuantizedEmbeddingEntity> QuantizedVectors { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        // Configure VectorEntity
        modelBuilder.Entity<QuantizedVectorEntity>(entity =>
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

            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.ChunkIndex);

            // Vector similarity index (IVFFlat for fast approximate search)
            entity.HasIndex(e => e.Embedding)
                .HasMethod("ivfflat")
                .HasOperators("vector_cosine_ops");
        });

        // Configure QuantizedEmbeddingEntity
        modelBuilder.Entity<PostgresQuantizedEmbeddingEntity>(entity =>
        {
            entity.ToTable("quantized_vectors");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ChunkId)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.QuantizedData)
                .IsRequired();

            entity.Property(e => e.MetadataJson)
                .HasColumnType("jsonb");

            entity.HasIndex(e => e.ChunkId)
                .IsUnique();

            entity.HasIndex(e => e.QuantizationType);
        });
    }
}

/// <summary>
/// Entity for storing document chunks with pgvector embeddings.
/// </summary>
public class QuantizedVectorEntity
{
    public Guid Id { get; set; }
    public string DocumentId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public Vector Embedding { get; set; } = new Vector(Array.Empty<float>());
    public int TokenCount { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Entity for storing quantized embeddings in PostgreSQL.
/// </summary>
public class PostgresQuantizedEmbeddingEntity
{
    public Guid Id { get; set; }
    public string ChunkId { get; set; } = string.Empty;
    public byte[] QuantizedData { get; set; } = Array.Empty<byte>();
    public int QuantizationType { get; set; }
    public int OriginalDimension { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
