using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FluxIndex.Storage.PostgreSQL.EntityGraph;

/// <summary>
/// DbContext for entity graph storage in PostgreSQL.
/// </summary>
public class EntityGraphDbContext : DbContext, IEmbeddingDimensionsModel
{
    private readonly EntityGraphOptions _options;

    public EntityGraphDbContext(
        DbContextOptions<EntityGraphDbContext> options,
        IOptions<EntityGraphOptions> graphOptions)
        : base(options)
    {
        _options = graphOptions.Value;
    }

    public DbSet<EntityGraphEntity> Entities => Set<EntityGraphEntity>();
    public DbSet<EntityGraphRelationshipEntity> Relationships => Set<EntityGraphRelationshipEntity>();
    public DbSet<EntityCommunityEntity> Communities => Set<EntityCommunityEntity>();
    public DbSet<EntityCommunityMemberEntity> CommunityMembers => Set<EntityCommunityMemberEntity>();

    int IEmbeddingDimensionsModel.EmbeddingDimensions => _options.EmbeddingDimension;

    /// <inheritdoc />
    /// <remarks>The model depends on the embedding dimension, so the model cache is keyed on it too.</remarks>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, EmbeddingDimensionsModelCacheKeyFactory>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        ConfigureEntityGraphEntity(modelBuilder);
        ConfigureEntityGraphRelationshipEntity(modelBuilder);
        ConfigureEntityCommunityEntity(modelBuilder);
        ConfigureEntityCommunityMemberEntity(modelBuilder);
    }

    private void ConfigureEntityGraphEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<EntityGraphEntity>();

        entity.HasKey(e => e.Id);

        // A database default, not only a CLR one: it is what lets the provisioner add the column to an existing table.
        entity.Property(e => e.Partition).HasDefaultValue(string.Empty);

        // The row's version for optimistic concurrency: every write stamps UpdatedAt, so an UPDATE or DELETE that read the row
        // before another writer changed it matches no row and fails with DbUpdateConcurrencyException; the store re-reads
        // and applies its write again. Without it two merges into one stored entity both read the same chunk list and the
        // later write drops the earlier one's new chunks. An existing column, so no schema change.
        entity.Property(e => e.UpdatedAt).IsConcurrencyToken();

        // Index for name lookups
        entity.HasIndex(e => e.NormalizedName);

        // Index for type filtering
        entity.HasIndex(e => e.EntityType);

        // Index for importance ranking
        entity.HasIndex(e => e.ImportanceScore);

        // Vector index for similarity search (if embedding dimension is configured).
        // HNSW (not ivfflat): ivfflat trains centroids at CREATE INDEX time, so an index
        // created on an empty table (EnsureCreated) silently loses recall for data inserted
        // afterwards. HNSW builds incrementally (pgvector >= 0.5). Same rationale as the
        // main vector store (FluxIndexDbContext).
        // The column MUST declare its dimension — pgvector rejects vector indexes on a
        // dimensionless "vector" column ("column does not have dimensions"), which made
        // EnsureCreated fail whenever EmbeddingDimension > 0 (latent since the ivfflat era).
        if (_options.EmbeddingDimension > 0)
        {
            entity.Property(e => e.Embedding)
                .HasColumnType($"vector({_options.EmbeddingDimension})");

            entity.HasIndex(e => e.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
        }

        // Relationships
        entity.HasMany(e => e.OutgoingRelationships)
            .WithOne(r => r.SourceEntity)
            .HasForeignKey(r => r.SourceEntityId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(e => e.IncomingRelationships)
            .WithOne(r => r.TargetEntity)
            .HasForeignKey(r => r.TargetEntityId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureEntityGraphRelationshipEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<EntityGraphRelationshipEntity>();

        entity.HasKey(e => e.Id);

        // Index for source entity lookups
        entity.HasIndex(e => e.SourceEntityId);

        // Index for target entity lookups
        entity.HasIndex(e => e.TargetEntityId);

        // Composite index for bidirectional lookups
        entity.HasIndex(e => new { e.SourceEntityId, e.TargetEntityId });

        // Index for type filtering
        entity.HasIndex(e => e.RelationType);

        // Index for weight-based traversal
        entity.HasIndex(e => e.Weight);
    }

    private void ConfigureEntityCommunityEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<EntityCommunityEntity>();

        entity.HasKey(e => e.Id);

        entity.Property(e => e.Partition).HasDefaultValue(string.Empty);

        // Index for hierarchy navigation
        entity.HasIndex(e => e.ParentCommunityId);

        // Index for level-based queries
        entity.HasIndex(e => e.Level);

        // Index for importance ranking
        entity.HasIndex(e => e.ImportanceScore);

        // Vector index for community similarity. HNSW + declared column dimension for the
        // same rationale as the entity index above.
        if (_options.EmbeddingDimension > 0)
        {
            entity.Property(e => e.Embedding)
                .HasColumnType($"vector({_options.EmbeddingDimension})");

            entity.HasIndex(e => e.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
        }

        // Self-referencing hierarchy
        entity.HasOne(e => e.ParentCommunity)
            .WithMany(e => e.ChildCommunities)
            .HasForeignKey(e => e.ParentCommunityId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureEntityCommunityMemberEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<EntityCommunityMemberEntity>();

        // Composite primary key
        entity.HasKey(e => new { e.EntityId, e.CommunityId });

        // Index for entity's communities lookup
        entity.HasIndex(e => e.EntityId);

        // Index for community's members lookup
        entity.HasIndex(e => e.CommunityId);

        // Relationships
        entity.HasOne(e => e.Entity)
            .WithMany(e => e.CommunityMemberships)
            .HasForeignKey(e => e.EntityId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Community)
            .WithMany(e => e.Members)
            .HasForeignKey(e => e.CommunityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Options for entity graph storage.
/// </summary>
public class EntityGraphOptions
{
    /// <summary>
    /// PostgreSQL connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Embedding vector dimension (e.g., 384, 768, 1536).
    /// Set to 0 to disable vector columns.
    /// </summary>
    public int EmbeddingDimension { get; set; } = 384;

    /// <summary>
    /// Maximum traversal depth for recursive queries.
    /// </summary>
    public int MaxTraversalDepth { get; set; } = 10;

    /// <summary>
    /// Default page size for queries.
    /// </summary>
    public int DefaultPageSize { get; set; } = 100;

    /// <summary>
    /// Provision the entity graph schema (the pgvector extension and the tables) when the application host
    /// starts. Off for a schema managed outside the application, or a managed PostgreSQL without the
    /// CREATE EXTENSION privilege — then call <c>EnsureEntityGraphSchemaAsync</c> yourself, or provision it externally.
    /// </summary>
    public bool AutoMigrate { get; set; } = true;
}
