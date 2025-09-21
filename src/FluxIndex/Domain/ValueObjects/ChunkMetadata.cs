using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxIndex.Domain.ValueObjects;

/// <summary>
/// 문서 청크의 메타데이터를 나타내는 불변 값 객체
/// 테스트 가능성을 위한 팩토리 메서드와 검증 로직 포함
/// </summary>
public sealed record ChunkMetadata
{
    /// <summary>
    /// 청크의 제목 (최대 100자)
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// 청크의 요약 (최대 200자)
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// 추출된 키워드 목록 (중복 제거, 정렬)
    /// </summary>
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 식별된 엔터티 목록 (Person, Organization, Location 등)
    /// </summary>
    public IReadOnlyList<string> Entities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 청크 내용을 기반으로 생성된 질문 목록
    /// </summary>
    public IReadOnlyList<string> GeneratedQuestions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 사용자 정의 필드 (확장 가능한 메타데이터)
    /// </summary>
    public IReadOnlyDictionary<string, object> CustomFields { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// 메타데이터 품질 점수 (0.0 ~ 1.0)
    /// </summary>
    public float QualityScore { get; init; }

    /// <summary>
    /// 메타데이터 추출 시각
    /// </summary>
    public DateTime ExtractedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 추출에 사용된 모델 또는 서비스 정보
    /// </summary>
    public string ExtractedBy { get; init; } = string.Empty;

    /// <summary>
    /// 메타데이터의 신뢰도 수준
    /// </summary>
    public ConfidenceLevel Confidence { get; init; } = ConfidenceLevel.Medium;

    /// <summary>
    /// 유효한 메타데이터인지 검증
    /// </summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Title) &&
        QualityScore >= 0.0f && QualityScore <= 1.0f &&
        Title.Length <= 100 &&
        Summary.Length <= 200;

    /// <summary>
    /// 테스트용 팩토리 메서드 - 기본값으로 유효한 메타데이터 생성
    /// </summary>
    public static ChunkMetadata CreateDefault(string title = "Test Title") => new()
    {
        Title = title,
        Summary = "Test summary for validation",
        Keywords = new[] { "test", "metadata", "chunk" },
        Entities = new[] { "TestEntity" },
        GeneratedQuestions = new[] { "What is this about?" },
        QualityScore = 0.85f,
        ExtractedBy = "TestService",
        Confidence = ConfidenceLevel.High
    };

    /// <summary>
    /// 빌더 패턴을 통한 유연한 생성 (테스트에서 활용)
    /// </summary>
    public static ChunkMetadataBuilder Builder() => new();

    /// <summary>
    /// 메타데이터를 검색 가능한 키워드로 변환
    /// </summary>
    public IEnumerable<string> GetSearchableTerms()
    {
        var terms = new List<string>();

        if (!string.IsNullOrWhiteSpace(Title))
            terms.AddRange(Title.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        terms.AddRange(Keywords);
        terms.AddRange(Entities);

        return terms.Where(t => t.Length > 2).Distinct().Select(t => t.ToLowerInvariant());
    }

    /// <summary>
    /// 디버깅용 문자열 표현
    /// </summary>
    public override string ToString() =>
        $"ChunkMetadata: {Title} (Quality: {QualityScore:F2}, Keywords: {Keywords.Count})";
}

/// <summary>
/// 메타데이터 신뢰도 수준
/// </summary>
public enum ConfidenceLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    VeryHigh = 3
}

/// <summary>
/// ChunkMetadata 생성을 위한 빌더 클래스 (테스트에서 활용)
/// </summary>
public class ChunkMetadataBuilder
{
    private string _title = string.Empty;
    private string _summary = string.Empty;
    private List<string> _keywords = new();
    private List<string> _entities = new();
    private List<string> _questions = new();
    private Dictionary<string, object> _customFields = new();
    private float _qualityScore = 0.8f;
    private string _extractedBy = "TestService";
    private ConfidenceLevel _confidence = ConfidenceLevel.Medium;

    public ChunkMetadataBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public ChunkMetadataBuilder WithSummary(string summary)
    {
        _summary = summary;
        return this;
    }

    public ChunkMetadataBuilder WithKeywords(params string[] keywords)
    {
        _keywords.AddRange(keywords);
        return this;
    }

    public ChunkMetadataBuilder WithEntities(params string[] entities)
    {
        _entities.AddRange(entities);
        return this;
    }

    public ChunkMetadataBuilder WithQuestions(params string[] questions)
    {
        _questions.AddRange(questions);
        return this;
    }

    public ChunkMetadataBuilder WithQualityScore(float score)
    {
        _qualityScore = score;
        return this;
    }

    public ChunkMetadataBuilder WithConfidence(ConfidenceLevel confidence)
    {
        _confidence = confidence;
        return this;
    }

    public ChunkMetadataBuilder WithCustomField(string key, object value)
    {
        _customFields[key] = value;
        return this;
    }

    public ChunkMetadata Build() => new()
    {
        Title = _title,
        Summary = _summary,
        Keywords = _keywords.Distinct().ToList(),
        Entities = _entities.Distinct().ToList(),
        GeneratedQuestions = _questions.ToList(),
        CustomFields = _customFields.ToDictionary(kv => kv.Key, kv => kv.Value),
        QualityScore = _qualityScore,
        ExtractedBy = _extractedBy,
        Confidence = _confidence
    };
}