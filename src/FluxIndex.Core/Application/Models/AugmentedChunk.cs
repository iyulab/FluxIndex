using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.Core.Application.Models;

/// <summary>
/// FluxIndex가 증강한 청크 (IEnrichedChunk + 증강 데이터)
/// FileFlux/WebFlux에서 받은 청크에 Contextual Header, Summary 등을 추가
/// </summary>
public class AugmentedChunk
{
    /// <summary>
    /// 청크의 텍스트 내용
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// 청크의 고유 식별자
    /// </summary>
    public required string ChunkId { get; set; }

    /// <summary>
    /// 문서 내 청크 순서 (0-based index)
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// 헤딩 계층 구조
    /// </summary>
    public List<string> HeadingPath { get; set; } = [];

    /// <summary>
    /// 현재 섹션 제목
    /// </summary>
    public string? SectionTitle { get; set; }

    /// <summary>
    /// 시작 페이지 번호
    /// </summary>
    public int? StartPage { get; set; }

    /// <summary>
    /// 종료 페이지 번호
    /// </summary>
    public int? EndPage { get; set; }

    /// <summary>
    /// 청크 품질 점수
    /// </summary>
    public double Quality { get; set; }

    /// <summary>
    /// 컨텍스트 의존도
    /// </summary>
    public double ContextDependency { get; set; }

    /// <summary>
    /// 토큰 수
    /// </summary>
    public int? TokenCount { get; set; }

    /// <summary>
    /// 소스 메타데이터
    /// </summary>
    public required SourceMetadata Source { get; set; }

    // =========================================================================
    // FluxIndex 증강 데이터
    // =========================================================================

    /// <summary>
    /// Contextual Header (핵심 증강 데이터)
    /// 청크 앞에 추가되어 문맥을 제공
    /// </summary>
    public string ContextualHeader { get; set; } = string.Empty;

    /// <summary>
    /// 문서/청크 요약
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// 토픽 목록 (LLM 생성 또는 정제)
    /// </summary>
    public List<string>? Topics { get; set; }

    /// <summary>
    /// 정제된 키워드 목록
    /// </summary>
    public List<string>? RefinedKeywords { get; set; }

    /// <summary>
    /// 예상 질문 목록 (HyDE용)
    /// </summary>
    public List<string>? PotentialQuestions { get; set; }

    // =========================================================================
    // 임베딩
    // =========================================================================

    /// <summary>
    /// 원본 콘텐츠 임베딩
    /// </summary>
    public float[]? Embedding { get; set; }

    /// <summary>
    /// Contextual Header 포함 임베딩
    /// </summary>
    public float[]? ContextualEmbedding { get; set; }

    // =========================================================================
    // 계산된 속성
    // =========================================================================

    /// <summary>
    /// 검색용 결합 텍스트 (Contextual Header + Content)
    /// </summary>
    public string SearchableContent => string.IsNullOrEmpty(ContextualHeader)
        ? Content
        : $"{ContextualHeader}\n\n{Content}";

    /// <summary>
    /// 페이지 범위 문자열
    /// </summary>
    public string? PageRange => StartPage.HasValue
        ? EndPage.HasValue && EndPage != StartPage
            ? $"pp.{StartPage}-{EndPage}"
            : $"p.{StartPage}"
        : null;
}

/// <summary>
/// 소스 문서 메타데이터 구현
/// </summary>
public class SourceMetadata : ISourceMetadata
{
    public required string SourceId { get; set; }
    public required string SourceType { get; set; }
    public required string Title { get; set; }
    public string? FilePath { get; set; }
    public string? Url { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Language { get; set; } = "en";
    public double? LanguageConfidence { get; set; }
    public int WordCount { get; set; }
    public int ChunkCount { get; set; }
    public int? PageCount { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? Author { get; set; }
    public IReadOnlyList<string>? Keywords { get; set; }
}
