namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// FileFlux/WebFlux 청크를 FluxIndex에서 사용하기 위한 통합 인터페이스
/// RAG 시스템에서 필요한 모든 메타데이터를 표준화된 형태로 제공
/// </summary>
public interface IEnrichedChunk
{
    /// <summary>
    /// 청크의 텍스트 내용
    /// </summary>
    string Content { get; }

    /// <summary>
    /// 청크의 고유 식별자
    /// </summary>
    string ChunkId { get; }

    /// <summary>
    /// 문서 내 청크 순서 (0-based index)
    /// </summary>
    int ChunkIndex { get; }

    /// <summary>
    /// 헤딩 계층 구조
    /// 예: ["1장 서론", "1.2 배경", "1.2.1 연구 목적"]
    /// </summary>
    IReadOnlyList<string> HeadingPath { get; }

    /// <summary>
    /// 현재 섹션 제목 (HeadingPath의 마지막 요소)
    /// </summary>
    string? SectionTitle { get; }

    /// <summary>
    /// 시작 페이지 번호 (1-based, 해당 없으면 null)
    /// </summary>
    int? StartPage { get; }

    /// <summary>
    /// 종료 페이지 번호 (여러 페이지에 걸친 청크의 경우)
    /// </summary>
    int? EndPage { get; }

    /// <summary>
    /// 청크 품질 점수 (0.0 - 1.0)
    /// </summary>
    double Quality { get; }

    /// <summary>
    /// 컨텍스트 의존도 (0.0 - 1.0)
    /// 높을수록 주변 맥락에 대한 의존도가 높음
    /// LLM 기반 Contextual Header 생성 필요 여부 판단에 사용
    /// </summary>
    double ContextDependency { get; }

    /// <summary>
    /// 예상 토큰 수 (null이면 계산 필요)
    /// </summary>
    int? TokenCount { get; }

    /// <summary>
    /// 소스 문서 메타데이터
    /// </summary>
    ISourceMetadata Source { get; }
}

/// <summary>
/// 소스 문서 메타데이터
/// 추적성과 필터링을 위한 원본 문서 정보
/// </summary>
public interface ISourceMetadata
{
    /// <summary>
    /// 소스 문서의 고유 식별자
    /// </summary>
    string SourceId { get; }

    /// <summary>
    /// 소스 유형 (PDF, DOCX, MD, url, html 등)
    /// </summary>
    string SourceType { get; }

    /// <summary>
    /// 문서 제목
    /// </summary>
    string Title { get; }

    /// <summary>
    /// 원본 파일 경로 (파일인 경우)
    /// </summary>
    string? FilePath { get; }

    /// <summary>
    /// 소스 URL (웹 문서인 경우)
    /// </summary>
    string? Url { get; }

    /// <summary>
    /// 문서 생성/처리 시간
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// 감지된 언어 (ISO 639-1 코드: "ko", "en", "ja")
    /// </summary>
    string Language { get; }

    /// <summary>
    /// 언어 감지 신뢰도 (0.0 - 1.0)
    /// </summary>
    double? LanguageConfidence { get; }

    /// <summary>
    /// 문서의 총 단어 수
    /// </summary>
    int WordCount { get; }

    /// <summary>
    /// 생성된 총 청크 수
    /// </summary>
    int ChunkCount { get; }

    /// <summary>
    /// 총 페이지 수 (해당되는 경우)
    /// </summary>
    int? PageCount { get; }

    /// <summary>
    /// 콘텐츠 발행일 (웹 문서의 article:published_time 등)
    /// </summary>
    DateTime? PublishedAt { get; }

    /// <summary>
    /// 작성자
    /// </summary>
    string? Author { get; }

    /// <summary>
    /// 키워드 목록
    /// </summary>
    IReadOnlyList<string>? Keywords { get; }
}
