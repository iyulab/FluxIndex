using System;
using System.Collections.Generic;

namespace FluxIndex.Domain.Models;

/// <summary>
/// 청크 계층 구조 모델 - Small-to-Big 검색을 위한 부모-자식 관계
/// </summary>
public class ChunkHierarchy
{
    /// <summary>
    /// 청크 ID
    /// </summary>
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>
    /// 부모 청크 ID (상위 계층)
    /// </summary>
    public string? ParentChunkId { get; set; }

    /// <summary>
    /// 자식 청크 ID 목록 (하위 계층)
    /// </summary>
    public List<string> ChildChunkIds { get; set; } = new();

    /// <summary>
    /// 계층 레벨 (0=문장, 1=문단, 2=섹션, 3=챕터)
    /// </summary>
    public int HierarchyLevel { get; set; }

    /// <summary>
    /// 컨텍스트 확장 시 권장 윈도우 크기
    /// </summary>
    public int RecommendedWindowSize { get; set; } = 1;

    /// <summary>
    /// 청크 경계 정보
    /// </summary>
    public ChunkBoundary Boundary { get; set; } = new();

    /// <summary>
    /// 계층 구조 메타데이터
    /// </summary>
    public HierarchyMetadata Metadata { get; set; } = new();

    /// <summary>
    /// 생성 시간
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 업데이트 시간
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 청크 경계 정보
/// </summary>
public class ChunkBoundary
{
    /// <summary>
    /// 시작 위치 (문자 단위)
    /// </summary>
    public int StartPosition { get; set; }

    /// <summary>
    /// 종료 위치 (문자 단위)
    /// </summary>
    public int EndPosition { get; set; }

    /// <summary>
    /// 경계 타입
    /// </summary>
    public BoundaryType Type { get; set; } = BoundaryType.Sentence;

    /// <summary>
    /// 경계 신뢰도 (0.0 - 1.0)
    /// </summary>
    public double Confidence { get; set; } = 1.0;

    /// <summary>
    /// 경계 감지 방법
    /// </summary>
    public string DetectionMethod { get; set; } = "rule_based";
}

/// <summary>
/// 계층 구조 메타데이터
/// </summary>
public class HierarchyMetadata
{
    /// <summary>
    /// 계층 깊이 (루트로부터의 거리)
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// 형제 청크 수
    /// </summary>
    public int SiblingCount { get; set; }

    /// <summary>
    /// 자손 청크 총 수
    /// </summary>
    public int DescendantCount { get; set; }

    /// <summary>
    /// 계층 가중치 (중요도 점수)
    /// </summary>
    public double HierarchyWeight { get; set; } = 1.0;

    /// <summary>
    /// 계층 구조 품질 점수
    /// </summary>
    public double QualityScore { get; set; } = 1.0;

    /// <summary>
    /// 추가 메타데이터
    /// </summary>
    public Dictionary<string, object> Properties { get; set; } = new();
}

/// <summary>
/// 경계 타입 열거형
/// </summary>
public enum BoundaryType
{
    /// <summary>
    /// 문장 경계
    /// </summary>
    Sentence,

    /// <summary>
    /// 문단 경계
    /// </summary>
    Paragraph,

    /// <summary>
    /// 섹션 경계
    /// </summary>
    Section,

    /// <summary>
    /// 챕터 경계
    /// </summary>
    Chapter,

    /// <summary>
    /// 사용자 정의 경계
    /// </summary>
    Custom
}

/// <summary>
/// 청크 관계 확장 모델
/// </summary>
public class ChunkRelationshipExtended
{
    /// <summary>
    /// 관계 ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 소스 청크 ID
    /// </summary>
    public string SourceChunkId { get; set; } = string.Empty;

    /// <summary>
    /// 타겟 청크 ID
    /// </summary>
    public string TargetChunkId { get; set; } = string.Empty;

    /// <summary>
    /// 관계 타입
    /// </summary>
    public RelationshipType Type { get; set; }

    /// <summary>
    /// 관계 강도 (0.0 - 1.0)
    /// </summary>
    public double Strength { get; set; }

    /// <summary>
    /// 관계 방향 (단방향/양방향)
    /// </summary>
    public RelationshipDirection Direction { get; set; } = RelationshipDirection.Bidirectional;

    /// <summary>
    /// 관계 설명
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 관계 메타데이터
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// 생성 시간
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 관계 타입 열거형 (확장)
/// </summary>
public enum RelationshipType
{
    /// <summary>
    /// 순차적 관계 (시간/공간적 순서)
    /// </summary>
    Sequential,

    /// <summary>
    /// 계층적 관계 (부모-자식)
    /// </summary>
    Hierarchical,

    /// <summary>
    /// 의미적 관계 (주제/개념 유사성)
    /// </summary>
    Semantic,

    /// <summary>
    /// 참조 관계 (명시적 언급)
    /// </summary>
    Reference,

    /// <summary>
    /// 인과 관계 (원인-결과)
    /// </summary>
    Causal,

    /// <summary>
    /// 대비 관계 (비교/대조)
    /// </summary>
    Contrastive,

    /// <summary>
    /// 보완 관계 (상호 보완)
    /// </summary>
    Complementary
}

/// <summary>
/// 관계 방향 열거형
/// </summary>
public enum RelationshipDirection
{
    /// <summary>
    /// 단방향 (소스 → 타겟)
    /// </summary>
    Unidirectional,

    /// <summary>
    /// 양방향 (소스 ↔ 타겟)
    /// </summary>
    Bidirectional
}