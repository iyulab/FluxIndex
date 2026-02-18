using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FluxIndex.Storage.SQLite.Graph;

/// <summary>
/// DbContext for entity graph storage in SQLite.
/// </summary>
public class SQLiteEntityGraphDbContext : DbContext
{
    private readonly SQLiteEntityGraphOptions _options;

    public SQLiteEntityGraphDbContext(
        DbContextOptions<SQLiteEntityGraphDbContext> options,
        IOptions<SQLiteEntityGraphOptions> graphOptions)
        : base(options)
    {
        _options = graphOptions.Value;
    }

    public DbSet<SQLiteEntityGraphEntity> Entities => Set<SQLiteEntityGraphEntity>();
    public DbSet<SQLiteEntityGraphRelationshipEntity> Relationships => Set<SQLiteEntityGraphRelationshipEntity>();
    public DbSet<SQLiteEntityCommunityEntity> Communities => Set<SQLiteEntityCommunityEntity>();
    public DbSet<SQLiteEntityCommunityMemberEntity> CommunityMembers => Set<SQLiteEntityCommunityMemberEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureEntityGraphEntity(modelBuilder);
        ConfigureEntityGraphRelationshipEntity(modelBuilder);
        ConfigureEntityCommunityEntity(modelBuilder);
        ConfigureEntityCommunityMemberEntity(modelBuilder);
    }

    private static void ConfigureEntityGraphEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SQLiteEntityGraphEntity>();

        entity.HasKey(e => e.Id);

        // Index for name lookups
        entity.HasIndex(e => e.NormalizedName);

        // Index for type filtering
        entity.HasIndex(e => e.EntityType);

        // Index for importance ranking
        entity.HasIndex(e => e.ImportanceScore);

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
        var entity = modelBuilder.Entity<SQLiteEntityGraphRelationshipEntity>();

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

    private static void ConfigureEntityCommunityEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SQLiteEntityCommunityEntity>();

        entity.HasKey(e => e.Id);

        // Index for hierarchy navigation
        entity.HasIndex(e => e.ParentCommunityId);

        // Index for level-based queries
        entity.HasIndex(e => e.Level);

        // Index for importance ranking
        entity.HasIndex(e => e.ImportanceScore);

        // Self-referencing hierarchy
        entity.HasOne(e => e.ParentCommunity)
            .WithMany(e => e.ChildCommunities)
            .HasForeignKey(e => e.ParentCommunityId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureEntityCommunityMemberEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SQLiteEntityCommunityMemberEntity>();

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
/// Options for SQLite entity graph storage.
/// </summary>
public class SQLiteEntityGraphOptions
{
    /// <summary>
    /// SQLite database path.
    /// </summary>
    public string DatabasePath { get; set; } = "fluxindex-entitygraph.db";

    /// <summary>
    /// Use in-memory database.
    /// </summary>
    public bool UseInMemory { get; set; }

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

    /// <summary>
    /// Command timeout in seconds.
    /// </summary>
    public int CommandTimeout { get; set; } = 30;

    /// <summary>
    /// Gets the connection string.
    /// </summary>
    public string GetConnectionString()
    {
        if (UseInMemory)
        {
            return "Data Source=:memory:";
        }
        return $"Data Source={DatabasePath}";
    }
}
