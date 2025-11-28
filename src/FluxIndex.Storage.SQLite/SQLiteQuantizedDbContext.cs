using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// EF Core DbContext for SQLite storage with quantized vector support.
/// </summary>
public class SQLiteQuantizedDbContext : DbContext
{
    private readonly SQLiteQuantizedOptions _options;

    public SQLiteQuantizedDbContext(
        DbContextOptions<SQLiteQuantizedDbContext> options,
        IOptions<SQLiteQuantizedOptions> sqliteOptions)
        : base(options)
    {
        _options = sqliteOptions.Value;
    }

    public DbSet<QuantizedVectorEntity> Vectors { get; set; }
    public DbSet<QuantizedEmbeddingEntity> QuantizedVectors { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure VectorEntity
        modelBuilder.Entity<QuantizedVectorEntity>(entity =>
        {
            entity.ToTable("vectors");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.DocumentId)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.Content)
                .IsRequired();

            // Store embedding as JSON
            entity.Property(e => e.Embedding)
                .HasConversion(
                    v => v != null ? JsonSerializer.Serialize(v, (JsonSerializerOptions?)null) : null,
                    v => !string.IsNullOrEmpty(v) ? JsonSerializer.Deserialize<float[]>(v, (JsonSerializerOptions?)null) : null
                );

            // Store metadata as JSON
            entity.Property(e => e.Metadata)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, object>()
                );

            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.ChunkIndex);
        });

        // Configure QuantizedEmbeddingEntity
        modelBuilder.Entity<QuantizedEmbeddingEntity>(entity =>
        {
            entity.ToTable("quantized_vectors");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.ChunkId)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.QuantizedData)
                .IsRequired();

            entity.Property(e => e.MetadataJson)
                .HasColumnType("TEXT");

            entity.HasIndex(e => e.ChunkId)
                .IsUnique();

            entity.HasIndex(e => e.QuantizationType);
        });
    }
}

/// <summary>
/// Entity for storing document chunks with embeddings.
/// </summary>
public class QuantizedVectorEntity
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public float[]? Embedding { get; set; }
    public int TokenCount { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Entity for storing quantized embeddings.
/// </summary>
public class QuantizedEmbeddingEntity
{
    public string Id { get; set; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public byte[] QuantizedData { get; set; } = Array.Empty<byte>();
    public int QuantizationType { get; set; }
    public int OriginalDimension { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
