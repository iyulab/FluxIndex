using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FluxIndex.Core.Application.Interfaces;
using Pgvector;

namespace FluxIndex.Storage.PostgreSQL.EntityGraph;

/// <summary>
/// Database entity for graph entities (named entities from documents).
/// </summary>
[Table("entity_graph_entities")]
public class EntityGraphEntity
{
    [Key]
    [Column("id")]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [Column("name")]
    [MaxLength(512)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Column("normalized_name")]
    [MaxLength(512)]
    public string NormalizedName { get; set; } = string.Empty;

    [Column("entity_type")]
    public int EntityType { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Entity embedding vector for similarity search.
    /// </summary>
    [Column("embedding", TypeName = "vector")]
    public Vector? Embedding { get; set; }

    [Column("confidence")]
    public double Confidence { get; set; }

    [Column("importance_score")]
    public double ImportanceScore { get; set; }

    [Column("mention_count")]
    public int MentionCount { get; set; }

    /// <summary>
    /// JSON array of surface forms (aliases).
    /// </summary>
    [Column("surface_forms", TypeName = "jsonb")]
    public string SurfaceFormsJson { get; set; } = "[]";

    /// <summary>
    /// JSON array of chunk IDs where this entity appears.
    /// </summary>
    [Column("chunk_ids", TypeName = "jsonb")]
    public string ChunkIdsJson { get; set; } = "[]";

    /// <summary>
    /// JSON array of document IDs where this entity appears.
    /// </summary>
    [Column("document_ids", TypeName = "jsonb")]
    public string DocumentIdsJson { get; set; } = "[]";

    /// <summary>
    /// JSON object of external knowledge base links.
    /// </summary>
    [Column("external_links", TypeName = "jsonb")]
    public string ExternalLinksJson { get; set; } = "{}";

    /// <summary>
    /// JSON object for additional properties.
    /// </summary>
    [Column("properties", TypeName = "jsonb")]
    public string PropertiesJson { get; set; } = "{}";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public ICollection<EntityGraphRelationshipEntity> OutgoingRelationships { get; set; } = [];
    public ICollection<EntityGraphRelationshipEntity> IncomingRelationships { get; set; } = [];
    public ICollection<EntityCommunityMemberEntity> CommunityMemberships { get; set; } = [];
}

/// <summary>
/// Database entity for relationships between graph entities.
/// </summary>
[Table("entity_graph_relationships")]
public class EntityGraphRelationshipEntity
{
    [Key]
    [Column("id")]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [Column("source_entity_id")]
    [MaxLength(64)]
    public string SourceEntityId { get; set; } = string.Empty;

    [Required]
    [Column("target_entity_id")]
    [MaxLength(64)]
    public string TargetEntityId { get; set; } = string.Empty;

    [Column("relation_type")]
    public int RelationType { get; set; }

    [Column("label")]
    [MaxLength(256)]
    public string Label { get; set; } = string.Empty;

    [Column("confidence")]
    public double Confidence { get; set; }

    [Column("weight")]
    public double Weight { get; set; } = 1.0;

    [Column("is_directional")]
    public bool IsDirectional { get; set; } = true;

    /// <summary>
    /// JSON array of evidence chunk IDs.
    /// </summary>
    [Column("evidence_chunk_ids", TypeName = "jsonb")]
    public string EvidenceChunkIdsJson { get; set; } = "[]";

    /// <summary>
    /// JSON array of evidence text excerpts.
    /// </summary>
    [Column("evidence_texts", TypeName = "jsonb")]
    public string EvidenceTextsJson { get; set; } = "[]";

    /// <summary>
    /// JSON object for additional properties.
    /// </summary>
    [Column("properties", TypeName = "jsonb")]
    public string PropertiesJson { get; set; } = "{}";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    [ForeignKey(nameof(SourceEntityId))]
    public EntityGraphEntity? SourceEntity { get; set; }

    [ForeignKey(nameof(TargetEntityId))]
    public EntityGraphEntity? TargetEntity { get; set; }
}

/// <summary>
/// Database entity for communities of related entities.
/// </summary>
[Table("entity_graph_communities")]
public class EntityCommunityEntity
{
    [Key]
    [Column("id")]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [Column("name")]
    [MaxLength(512)]
    public string Name { get; set; } = string.Empty;

    [Column("summary")]
    public string? Summary { get; set; }

    [Column("importance_score")]
    public double ImportanceScore { get; set; }

    [Column("level")]
    public int Level { get; set; }

    [Column("parent_community_id")]
    [MaxLength(64)]
    public string? ParentCommunityId { get; set; }

    /// <summary>
    /// Community embedding vector.
    /// </summary>
    [Column("embedding", TypeName = "vector")]
    public Vector? Embedding { get; set; }

    /// <summary>
    /// JSON array of topics/themes.
    /// </summary>
    [Column("topics", TypeName = "jsonb")]
    public string TopicsJson { get; set; } = "[]";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    [ForeignKey(nameof(ParentCommunityId))]
    public EntityCommunityEntity? ParentCommunity { get; set; }

    public ICollection<EntityCommunityEntity> ChildCommunities { get; set; } = [];
    public ICollection<EntityCommunityMemberEntity> Members { get; set; } = [];
}

/// <summary>
/// Join table for entity-community membership.
/// </summary>
[Table("entity_community_members")]
public class EntityCommunityMemberEntity
{
    [Column("entity_id")]
    [MaxLength(64)]
    public string EntityId { get; set; } = string.Empty;

    [Column("community_id")]
    [MaxLength(64)]
    public string CommunityId { get; set; } = string.Empty;

    /// <summary>
    /// Membership strength/weight.
    /// </summary>
    [Column("membership_score")]
    public double MembershipScore { get; set; } = 1.0;

    [Column("joined_at")]
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    [ForeignKey(nameof(EntityId))]
    public EntityGraphEntity? Entity { get; set; }

    [ForeignKey(nameof(CommunityId))]
    public EntityCommunityEntity? Community { get; set; }
}
