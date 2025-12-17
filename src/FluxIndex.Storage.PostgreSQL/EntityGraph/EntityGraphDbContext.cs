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

        // Vector index for similarity search (if embedding dimension is configured)
        if (_options.EmbeddingDimension > 0)
        {
            entity.HasIndex(e => e.Embedding)
                .HasMethod("ivfflat")
                .HasOperators("vector_cosine_ops")
                .HasStorageParameter("lists", _options.IvfflatLists);
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

    private void ConfigureEntityGraphRelationshipEntity(ModelBuilder modelBuilder)
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

        // Vector index for community similarity
        if (_options.EmbeddingDimension > 0)
        {
            entity.HasIndex(e => e.Embedding)
                .HasMethod("ivfflat")
                .HasOperators("vector_cosine_ops")
                .HasStorageParameter("lists", _options.IvfflatLists / 4); // Fewer communities
        }

        // Self-referencing hierarchy
        entity.HasOne(e => e.ParentCommunity)
            .WithMany(e => e.ChildCommunities)
            .HasForeignKey(e => e.ParentCommunityId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private void ConfigureEntityCommunityMemberEntity(ModelBuilder modelBuilder)
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
