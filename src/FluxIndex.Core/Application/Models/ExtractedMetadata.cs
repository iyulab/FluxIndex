namespace FluxIndex.Core.Models;

/// <summary>
/// AI 추출 메타데이터 모델
/// WebFlux, FileFlux 통합 패턴 기반 구조화된 메타데이터
/// </summary>
public class ExtractedMetadata
{
    // ===================================================================
    // 핵심 검색 컨텍스트 (모든 스키마 공통)
    // ===================================================================

    /// <summary>
    /// 주제 목록 (AI 추출, 3-5개 권장)
    /// 예: ["React Hooks", "useState", "useEffect"]
    /// </summary>
    public string[] Topics { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 키워드 목록 (검색 최적화, 5-10개 권장)
    /// 예: ["async", "await", "promises", "javascript"]
    /// </summary>
    public string[] Keywords { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 문서 요약 (1-2문장, 최대 200자)
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 문서 타입 분류
    /// 예: "manual", "guide", "tutorial", "reference", "article", "note", "documentation"
    /// </summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>
    /// 언어 코드 (ISO 639-1)
    /// 예: "en", "ko", "ja", "zh"
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// 문서 카테고리
    /// 예: ["backend", "database"], ["frontend", "ui"]
    /// </summary>
    public string[] Categories { get; set; } = Array.Empty<string>();

    // ===================================================================
    // 스키마별 메타데이터 (유연한 확장성)
    // ===================================================================

    /// <summary>
    /// 스키마별 전용 메타데이터 (Dictionary로 유연성 확보)
    ///
    /// 예시:
    /// - TechnicalDoc: ["libraries": ["react@18.2.0"], "frameworks": ["React"], "technologies": ["JavaScript"]]
    /// - ProductManual: ["productName": "iPhone 15 Pro", "company": "Apple", "version": "iOS 17.2", "price": 999.00]
    /// - Article: ["author": "John Doe", "publishedDate": "2024-01-10", "readingTimeMinutes": 8, "tags": ["tutorial"]]
    /// </summary>
    public Dictionary<string, object> SchemaSpecificData { get; set; } = new();

    // ===================================================================
    // 메타데이터 소스 추적 (투명성 및 디버깅)
    // ===================================================================

    /// <summary>
    /// 메타데이터 전체 소스
    /// </summary>
    public MetadataSource Source { get; set; } = MetadataSource.AI;

    /// <summary>
    /// 필드별 소스 추적
    /// 예: {"topics": MetadataSource.AI, "keywords": MetadataSource.Merged, "description": MetadataSource.RuleBased}
    /// </summary>
    public Dictionary<string, MetadataSource> FieldSources { get; set; } = new();

    // ===================================================================
    // 신뢰도 및 품질 메트릭
    // ===================================================================

    /// <summary>
    /// 전체 신뢰도 점수 (0.0 - 1.0)
    /// AI 추출의 전반적인 신뢰도를 나타냄
    /// </summary>
    public float OverallConfidence { get; set; }

    /// <summary>
    /// 필드별 신뢰도 점수
    /// 예: {"topics": 0.96, "keywords": 0.92, "documentType": 0.88}
    /// </summary>
    public Dictionary<string, float> FieldConfidence { get; set; } = new();

    // ===================================================================
    // 사용자 검증 및 수정
    // ===================================================================

    /// <summary>
    /// 사용자가 메타데이터를 검증했는지 여부
    /// </summary>
    public bool UserVerified { get; set; } = false;

    /// <summary>
    /// 사용자 수정 사항
    /// 예: {"topics": ["React", "Hooks"], "contentType": "reference"}
    /// </summary>
    public Dictionary<string, object> UserCorrections { get; set; } = new();

    // ===================================================================
    // 추출 메타데이터
    // ===================================================================

    /// <summary>
    /// 추출 방법
    /// "AI", "RuleBased", "Hybrid"
    /// </summary>
    public string ExtractionMethod { get; set; } = "AI";

    /// <summary>
    /// 메타데이터 추출 시간
    /// </summary>
    public DateTimeOffset ExtractedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 문서 ID (추적용)
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;
}

/// <summary>
/// 메타데이터 소스 열거형
/// 메타데이터 출처 추적 및 신뢰도 판단용
/// </summary>
public enum MetadataSource
{
    /// <summary>AI 기반 추출 (LLM 텍스트 분석)</summary>
    AI,

    /// <summary>규칙 기반 추출 (패턴 매칭, AI 서비스 불필요)</summary>
    RuleBased,

    /// <summary>HTML 메타 태그 추출 (WebFlux 전용)</summary>
    Html,

    /// <summary>여러 소스 병합 (AI + RuleBased, AI + Html 등)</summary>
    Merged,

    /// <summary>사용자 검증 또는 수정한 데이터</summary>
    User
}

/// <summary>
/// 메타데이터 추출 스키마
/// 문서 타입에 따라 최적화된 추출 전략 선택
/// </summary>
public enum MetadataSchema
{
    /// <summary>
    /// 일반 문서 (기본값)
    /// 추출 필드: topics, keywords, description, documentType, language, categories
    /// 적합한 문서: 일반 텍스트, 블로그, 노트, 가이드
    /// </summary>
    General,

    /// <summary>
    /// 제품 매뉴얼 (Product Manual/Specification)
    /// 추출 필드: productName, company, version, model, releaseDate, keywords, topics
    /// 적합한 문서: 제품 설명서, 사용자 가이드, 스펙 문서
    /// 예시 데이터: productName="iPhone 15 Pro", company="Apple", version="iOS 17.2"
    /// </summary>
    ProductManual,

    /// <summary>
    /// 기술 문서 (Technical Documentation)
    /// 추출 필드: topics, libraries, frameworks, technologies, apiVersion, keywords
    /// 적합한 문서: API 문서, 개발자 가이드, 기술 레퍼런스
    /// 예시 데이터: libraries=["react@18.2.0"], frameworks=["React"], technologies=["JavaScript", "TypeScript"]
    /// </summary>
    TechnicalDoc,

    /// <summary>
    /// 블로그/뉴스 기사 (Article/Blog Post)
    /// 추출 필드: author, publishedDate, tags, readingTimeMinutes, topics, keywords
    /// 적합한 문서: 블로그 포스트, 뉴스 기사, 튜토리얼
    /// 예시 데이터: author="John Doe", publishedDate="2024-01-10", readingTimeMinutes=8
    /// </summary>
    Article,

    /// <summary>
    /// 사용자 정의 스키마 (Custom Schema)
    /// 커스텀 프롬프트를 사용하여 특정 도메인에 맞는 메타데이터 추출
    /// CustomPrompt 파라미터 필수
    /// </summary>
    Custom
}

/// <summary>
/// 메타데이터 추출 전략 (토큰 예산 제어)
/// FileFlux 패턴 채택
/// </summary>
public enum MetadataExtractionStrategy
{
    /// <summary>
    /// 빠른 추출 (2000 chars)
    /// 제목과 서론만 분석하여 빠른 메타데이터 추출
    /// 사용 케이스: 대용량 배치 처리, 프리뷰 생성
    /// </summary>
    Fast,

    /// <summary>
    /// 스마트 추출 (4000 chars, 기본값)
    /// 적응형 샘플링으로 문서 유형별 최적 섹션 분석
    /// 사용 케이스: 일반적인 문서 인덱싱, 균형잡힌 품질/비용
    /// </summary>
    Smart,

    /// <summary>
    /// 심층 추출 (8000 chars)
    /// 전체 컨텍스트 분석으로 가장 정확한 메타데이터 추출
    /// 사용 케이스: 중요 문서, 최고 품질 요구사항
    /// </summary>
    Deep
}

/// <summary>
/// 배치 메타데이터 추출 요청
/// </summary>
public class BatchMetadataRequest
{
    /// <summary>
    /// 고유 문서 식별자
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// 분석할 문서 콘텐츠
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 캐시 키 (선택적, 캐시 조회용)
    /// </summary>
    public string? CacheKey { get; set; }

    /// <summary>
    /// 문서별 커스텀 메타데이터 (병합됨)
    /// </summary>
    public Dictionary<string, object>? CustomMetadata { get; set; }
}

/// <summary>
/// 메타데이터 추출 옵션
/// FileFlux 패턴 + FluxIndex 확장
/// </summary>
public class AIMetadataExtractionOptions
{
    // ===================================================================
    // 추출 전략 (FileFlux 패턴)
    // ===================================================================

    /// <summary>
    /// 추출 전략 (토큰 예산 제어)
    /// </summary>
    public MetadataExtractionStrategy Strategy { get; set; } = MetadataExtractionStrategy.Smart;

    /// <summary>
    /// 문서 타입별 적응형 샘플링 활성화
    /// </summary>
    public bool EnableAdaptiveSampling { get; set; } = true;

    /// <summary>
    /// 최대 토큰 수 (null = 전략에 따라 자동 설정)
    /// Fast: 2000, Smart: 4000, Deep: 8000
    /// </summary>
    public int? MaxTokens { get; set; }

    // ===================================================================
    // 신뢰도 및 품질
    // ===================================================================

    /// <summary>
    /// 최소 신뢰도 임계값 (0.0 - 1.0)
    /// 이 값보다 낮으면 RuleBased와 병합
    /// </summary>
    public float MinConfidence { get; set; } = 0.6f;

    /// <summary>
    /// 메타데이터 추출 실패 시 문서 처리 계속 진행 여부
    /// true: 추출 실패 시에도 문서 인덱싱 계속
    /// false: 추출 실패 시 예외 발생
    /// </summary>
    public bool ContinueOnFailure { get; set; } = true;

    // ===================================================================
    // 재시도 및 타임아웃 (FileFlux 패턴)
    // ===================================================================

    /// <summary>
    /// 최대 재시도 횟수
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// 재시도 지연 시간 (밀리초)
    /// 지수 백오프 적용됨
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// 타임아웃 (밀리초)
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;

    // ===================================================================
    // 커스텀 프롬프트
    // ===================================================================

    /// <summary>
    /// 커스텀 추출 프롬프트 (스키마 프롬프트 오버라이드)
    /// MetadataSchema.Custom 사용 시 필수
    /// </summary>
    public string? CustomPrompt { get; set; }

    // ===================================================================
    // 캐싱 설정
    // ===================================================================

    /// <summary>
    /// 캐싱 활성화 여부
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// 캐시 TTL (Time To Live)
    /// </summary>
    public TimeSpan CacheTTL { get; set; } = TimeSpan.FromHours(1);
}
