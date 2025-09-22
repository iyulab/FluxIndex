using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// SQLite database context for FluxIndex
/// </summary>
public class SQLiteDbContext : DbContext
{
    private readonly SQLiteOptions _options;

    public SQLiteDbContext(DbContextOptions<SQLiteDbContext> options, IOptions<SQLiteOptions> sqliteOptions)
        : base(options)
    {
        _options = sqliteOptions.Value;
    }

    public DbSet<VectorEntity> Vectors { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure VectorEntity
        modelBuilder.Entity<VectorEntity>(entity =>
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

            // Store embedding as JSON in SQLite
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

            // Indexes for performance
            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.ChunkIndex);
        });
    }
}