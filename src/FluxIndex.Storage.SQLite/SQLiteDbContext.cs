using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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

            // Store metadata as JSON
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

            // Indexes for performance
            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.ChunkIndex);
        });
    }
}