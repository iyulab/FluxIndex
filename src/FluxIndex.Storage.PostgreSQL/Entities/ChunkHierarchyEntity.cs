using FluxIndex.Core.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json;

namespace FluxIndex.Storage.PostgreSQL.Entities;

/// <summary>
/// 청크 계층 구조 엔터티
/// </summary>
[Table("chunk_hierarchies")]
[Index(nameof(ChunkId), IsUnique = true)]
[Index(nameof(ParentChunkId))]
[Index(nameof(HierarchyLevel))]
public class ChunkHierarchyEntity
{
    /// <summary>
    /// 청크 ID
    /// </summary>
    [Key]
    [Column("chunk_id")]
    [StringLength(450)]
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>
    /// 부모 청크 ID
    /// </summary>
    [Column("parent_chunk_id")]
    [StringLength(450)]
    public string? ParentChunkId { get; set; }

    /// <summary>
    /// 자식 청크 ID 목록 (JSON)
    /// </summary>
    [Column("child_chunk_ids", TypeName = "jsonb")]
    public string ChildChunkIdsJson { get; set; } = "[]";

    /// <summary>
    /// 계층 레벨
    /// </summary>
    [Column("hierarchy_level")]
    public int HierarchyLevel { get; set; }

    /// <summary>
    /// 권장 윈도우 크기
    /// </summary>
    [Column("recommended_window_size")]
    public int RecommendedWindowSize { get; set; } = 1;

    /// <summary>
    /// 시작 위치
    /// </summary>
    [Column("start_position")]
    public int StartPosition { get; set; }

    /// <summary>
    /// 종료 위치
    /// </summary>
    [Column("end_position")]
    public int EndPosition { get; set; }

    /// <summary>
    /// 경계 타입
    /// </summary>
    [Column("boundary_type")]
    public BoundaryType BoundaryType { get; set; } = BoundaryType.Sentence;

    /// <summary>
    /// 경계 신뢰도
    /// </summary>
    [Column("boundary_confidence")]
    public double BoundaryConfidence { get; set; } = 1.0;

    /// <summary>
    /// 경계 감지 방법
    /// </summary>
    [Column("detection_method")]
    [StringLength(100)]
    public string DetectionMethod { get; set; } = "rule_based";

    /// <summary>
    /// 계층 깊이
    /// </summary>
    [Column("depth")]
    public int Depth { get; set; }

    /// <summary>
    /// 형제 청크 수
    /// </summary>
    [Column("sibling_count")]
    public int SiblingCount { get; set; }

    /// <summary>
    /// 자손 청크 총 수
    /// </summary>
    [Column("descendant_count")]
    public int DescendantCount { get; set; }

    /// <summary>
    /// 계층 가중치
    /// </summary>
    [Column("hierarchy_weight")]
    public double HierarchyWeight { get; set; } = 1.0;

    /// <summary>
    /// 품질 점수
    /// </summary>
    [Column("quality_score")]
    public double QualityScore { get; set; } = 1.0;

    /// <summary>
    /// 추가 메타데이터 (JSON)
    /// </summary>
    [Column("metadata", TypeName = "jsonb")]
    public string MetadataJson { get; set; } = "{}";

    /// <summary>
    /// 생성 시간
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 업데이트 시간
    /// </summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 자식 청크 ID 목록 (deserialize용)
    /// </summary>
    [NotMapped]
    public List<string> ChildChunkIds
    {
        get => JsonSerializer.Deserialize<List<string>>(ChildChunkIdsJson) ?? new List<string>();
        set => ChildChunkIdsJson = JsonSerializer.Serialize(value);
    }

    /// <summary>
    /// 메타데이터 딕셔너리 (deserialize용)
    /// </summary>
    [NotMapped]
    public Dictionary<string, object> Metadata
    {
        get => JsonSerializer.Deserialize<Dictionary<string, object>>(MetadataJson) ?? new Dictionary<string, object>();
        set => MetadataJson = JsonSerializer.Serialize(value);
    }

    /// <summary>
    /// 도메인 모델로 변환
    /// </summary>
    public ChunkHierarchy ToDomainModel()
    {
        return new ChunkHierarchy
        {
            ChunkId = ChunkId,
            ParentChunkId = ParentChunkId,
            ChildChunkIds = ChildChunkIds,
            HierarchyLevel = HierarchyLevel,
            RecommendedWindowSize = RecommendedWindowSize,
            Boundary = new ChunkBoundary
            {
                StartPosition = StartPosition,
                EndPosition = EndPosition,
                Type = BoundaryType,
                Confidence = BoundaryConfidence,
                DetectionMethod = DetectionMethod
            },
            Metadata = new HierarchyMetadata
            {
                Depth = Depth,
                SiblingCount = SiblingCount,
                DescendantCount = DescendantCount,
                HierarchyWeight = HierarchyWeight,
                QualityScore = QualityScore,
                Properties = Metadata
            },
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }

    /// <summary>
    /// 도메인 모델에서 생성
    /// </summary>
    public static ChunkHierarchyEntity FromDomainModel(ChunkHierarchy hierarchy)
    {
        return new ChunkHierarchyEntity
        {
            ChunkId = hierarchy.ChunkId,
            ParentChunkId = hierarchy.ParentChunkId,
            ChildChunkIds = hierarchy.ChildChunkIds,
            HierarchyLevel = hierarchy.HierarchyLevel,
            RecommendedWindowSize = hierarchy.RecommendedWindowSize,
            StartPosition = hierarchy.Boundary.StartPosition,
            EndPosition = hierarchy.Boundary.EndPosition,
            BoundaryType = hierarchy.Boundary.Type,
            BoundaryConfidence = hierarchy.Boundary.Confidence,
            DetectionMethod = hierarchy.Boundary.DetectionMethod,
            Depth = hierarchy.Metadata.Depth,
            SiblingCount = hierarchy.Metadata.SiblingCount,
            DescendantCount = hierarchy.Metadata.DescendantCount,
            HierarchyWeight = hierarchy.Metadata.HierarchyWeight,
            QualityScore = hierarchy.Metadata.QualityScore,
            Metadata = hierarchy.Metadata.Properties,
            CreatedAt = hierarchy.CreatedAt,
            UpdatedAt = hierarchy.UpdatedAt
        };
    }

    /// <summary>
    /// 도메인 모델로부터 기존 엔터티 업데이트
    /// </summary>
    public void UpdateFromDomainModel(ChunkHierarchy hierarchy)
    {
        ParentChunkId = hierarchy.ParentChunkId;
        ChildChunkIds = hierarchy.ChildChunkIds;
        HierarchyLevel = hierarchy.HierarchyLevel;
        RecommendedWindowSize = hierarchy.RecommendedWindowSize;
        StartPosition = hierarchy.Boundary.StartPosition;
        EndPosition = hierarchy.Boundary.EndPosition;
        BoundaryType = hierarchy.Boundary.Type;
        BoundaryConfidence = hierarchy.Boundary.Confidence;
        DetectionMethod = hierarchy.Boundary.DetectionMethod;
        Depth = hierarchy.Metadata.Depth;
        SiblingCount = hierarchy.Metadata.SiblingCount;
        DescendantCount = hierarchy.Metadata.DescendantCount;
        HierarchyWeight = hierarchy.Metadata.HierarchyWeight;
        QualityScore = hierarchy.Metadata.QualityScore;
        Metadata = hierarchy.Metadata.Properties;
        UpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// 청크 관계 엔터티
/// </summary>
[Table("chunk_relationships")]
[Index(nameof(SourceChunkId))]
[Index(nameof(TargetChunkId))]
[Index(nameof(Type))]
public class ChunkRelationshipEntity
{
    /// <summary>
    /// 관계 ID
    /// </summary>
    [Key]
    [Column("id")]
    [StringLength(450)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 소스 청크 ID
    /// </summary>
    [Column("source_chunk_id")]
    [StringLength(450)]
    [Required]
    public string SourceChunkId { get; set; } = string.Empty;

    /// <summary>
    /// 타겟 청크 ID
    /// </summary>
    [Column("target_chunk_id")]
    [StringLength(450)]
    [Required]
    public string TargetChunkId { get; set; } = string.Empty;

    /// <summary>
    /// 관계 타입
    /// </summary>
    [Column("type")]
    public RelationshipType Type { get; set; }

    /// <summary>
    /// 관계 강도
    /// </summary>
    [Column("strength")]
    public double Strength { get; set; }

    /// <summary>
    /// 관계 방향
    /// </summary>
    [Column("direction")]
    public RelationshipDirection Direction { get; set; } = RelationshipDirection.Bidirectional;

    /// <summary>
    /// 관계 설명
    /// </summary>
    [Column("description")]
    [StringLength(1000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 메타데이터 (JSON)
    /// </summary>
    [Column("metadata", TypeName = "jsonb")]
    public string MetadataJson { get; set; } = "{}";

    /// <summary>
    /// 생성 시간
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 메타데이터 딕셔너리
    /// </summary>
    [NotMapped]
    public Dictionary<string, object> Metadata
    {
        get => JsonSerializer.Deserialize<Dictionary<string, object>>(MetadataJson) ?? new Dictionary<string, object>();
        set => MetadataJson = JsonSerializer.Serialize(value);
    }

    /// <summary>
    /// 도메인 모델로 변환
    /// </summary>
    public ChunkRelationshipExtended ToDomainModel()
    {
        return new ChunkRelationshipExtended
        {
            Id = Id,
            SourceChunkId = SourceChunkId,
            TargetChunkId = TargetChunkId,
            Type = Type,
            Strength = Strength,
            Direction = Direction,
            Description = Description,
            Metadata = Metadata,
            CreatedAt = CreatedAt
        };
    }

    /// <summary>
    /// 도메인 모델에서 생성
    /// </summary>
    public static ChunkRelationshipEntity FromDomainModel(ChunkRelationshipExtended relationship)
    {
        return new ChunkRelationshipEntity
        {
            Id = relationship.Id,
            SourceChunkId = relationship.SourceChunkId,
            TargetChunkId = relationship.TargetChunkId,
            Type = relationship.Type,
            Strength = relationship.Strength,
            Direction = relationship.Direction,
            Description = relationship.Description,
            Metadata = relationship.Metadata,
            CreatedAt = relationship.CreatedAt
        };
    }

    /// <summary>
    /// 도메인 모델로부터 기존 엔터티 업데이트
    /// </summary>
    public void UpdateFromDomainModel(ChunkRelationshipExtended relationship)
    {
        SourceChunkId = relationship.SourceChunkId;
        TargetChunkId = relationship.TargetChunkId;
        Type = relationship.Type;
        Strength = relationship.Strength;
        Direction = relationship.Direction;
        Description = relationship.Description;
        Metadata = relationship.Metadata;
    }
}