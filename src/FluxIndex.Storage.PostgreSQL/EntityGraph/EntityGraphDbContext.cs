using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FluxIndex.Storage.PostgreSQL.EntityGraph;

/// <summary>
/// DbContext for entity graph storage in PostgreSQL.
/// </summary>
public class EntityGraphDbContext : DbContext
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
    /// Number of lists for IVFFlat index.
    /// </summary>
    [Obsolete("Unused: EntityGraph vector indexes use HNSW (no training-time parameter) since 0.20.1 — " +
        "ivfflat trained on an empty table silently lost recall for later inserts. " +
        "Setting this has no effect; the property will be removed in a future minor.")]
    public int IvfflatLists { get; set; } = 100;

    /// <summary>
    /// Maximum traversal depth for recursive queries.
    /// </summary>
    public int MaxTraversalDepth { get; set; } = 10;

    /// <summary>
    /// Default page size for queries.
    /// </summary>
    public int DefaultPageSize { get; set; } = 100;

    /// <summary>
    /// Enable automatic migration.
    /// </summary>
    public bool AutoMigrate { get; set; } = true;
}
