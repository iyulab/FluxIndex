using FluxIndex.Core.Models;

namespace FluxIndex.Core.Interfaces;

/// <summary>
/// AI 기반 메타데이터 추출 서비스 인터페이스
/// FileFlux IMetadataEnricher + WebFlux IWebMetadataExtractor 통합 패턴
/// </summary>
public interface IMetadataExtractor
{
    /// <summary>
    /// 문서 콘텐츠에서 메타데이터를 추출합니다.
    /// </summary>
    /// <param name="content">문서 콘텐츠 (텍스트)</param>
    /// <param name="schema">메타데이터 스키마 (General, ProductManual, TechnicalDoc, Article, Custom)</param>
    /// <param name="options">추출 옵션 (null인 경우 기본값 사용)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추출된 메타데이터</returns>
    Task<ExtractedMetadata> ExtractAsync(
        string content,
        MetadataSchema schema = MetadataSchema.General,
        AIMetadataExtractionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐싱을 지원하는 메타데이터 추출
    /// 동일한 콘텐츠 재추출 시 캐시된 결과 반환으로 API 비용 절감
    /// </summary>
    /// <param name="content">문서 콘텐츠</param>
    /// <param name="cacheKey">캐시 키 (GenerateCacheKey로 생성)</param>
    /// <param name="schema">메타데이터 스키마</param>
    /// <param name="options">추출 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추출된 메타데이터 (캐시 또는 새로 추출)</returns>
    Task<ExtractedMetadata> ExtractWithCacheAsync(
        string content,
        string cacheKey,
        MetadataSchema schema = MetadataSchema.General,
        AIMetadataExtractionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 문서에서 메타데이터를 배치로 추출합니다.
    /// API 호출 최적화를 위해 병렬 처리를 수행합니다.
    /// </summary>
    /// <param name="requests">추출 요청 목록</param>
    /// <param name="schema">메타데이터 스키마</param>
    /// <param name="options">추출 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추출된 메타데이터 목록</returns>
    Task<IReadOnlyList<ExtractedMetadata>> ExtractBatchAsync(
        IReadOnlyList<BatchMetadataRequest> requests,
        MetadataSchema schema = MetadataSchema.General,
        AIMetadataExtractionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 콘텐츠 기반 캐시 키 생성
    /// 콘텐츠 해시 + 스키마로 고유한 캐시 키 생성
    /// </summary>
    /// <param name="content">문서 콘텐츠</param>
    /// <param name="schema">메타데이터 스키마</param>
    /// <returns>캐시 키</returns>
    string GenerateCacheKey(string content, MetadataSchema schema);

    /// <summary>
    /// 지원하는 메타데이터 스키마 목록 반환
    /// </summary>
    /// <returns>지원 스키마 목록</returns>
    IReadOnlyList<MetadataSchema> GetSupportedSchemas();

    /// <summary>
    /// 특정 스키마에 대한 설명 반환
    /// </summary>
    /// <param name="schema">스키마</param>
    /// <returns>스키마 설명</returns>
    string GetSchemaDescription(MetadataSchema schema);
}

/// <summary>
/// 규칙 기반 메타데이터 추출기 인터페이스
/// AI 서비스 없이 패턴 매칭으로 메타데이터 추출 (폴백용)
/// </summary>
public interface IRuleBasedMetadataExtractor
{
    /// <summary>
    /// 규칙 기반 메타데이터 추출
    /// AI 서비스 불필요, 빠른 처리
    /// </summary>
    /// <param name="content">문서 콘텐츠</param>
    /// <param name="schema">메타데이터 스키마</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추출된 메타데이터</returns>
    Task<ExtractedMetadata> ExtractAsync(
        string content,
        MetadataSchema schema,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 두 메타데이터를 병합 (AI + RuleBased 하이브리드 전략용)
    /// </summary>
    /// <param name="primary">주 메타데이터 (AI 추출)</param>
    /// <param name="fallback">폴백 메타데이터 (RuleBased 추출)</param>
    /// <returns>병합된 메타데이터</returns>
    ExtractedMetadata MergeMetadata(ExtractedMetadata primary, ExtractedMetadata fallback);
}
