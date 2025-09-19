using FluxIndex.Core.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluxIndex.AI.OpenAI.Parsers;

/// <summary>
/// OpenAI 응답에서 메타데이터를 안전하게 파싱하는 클래스
/// 테스트 가능하고 오류에 강한 설계
/// </summary>
public class MetadataJsonParser
{
    private readonly JsonSerializerOptions _jsonOptions;

    public MetadataJsonParser()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
    }

    /// <summary>
    /// OpenAI 응답에서 단일 메타데이터 파싱
    /// </summary>
    /// <param name="response">OpenAI 응답 텍스트</param>
    /// <param name="extractedBy">추출 서비스 정보</param>
    /// <returns>파싱된 메타데이터</returns>
    /// <exception cref="MetadataParsingException">파싱 실패 시</exception>
    public ChunkMetadata ParseMetadata(string response, string extractedBy = "OpenAI")
    {
        if (string.IsNullOrWhiteSpace(response))
            throw new MetadataParsingException("Response cannot be empty");

        try
        {
            var cleanedJson = CleanJsonResponse(response);
            var jsonDoc = JsonDocument.Parse(cleanedJson);

            return ParseMetadataFromJson(jsonDoc.RootElement, extractedBy);
        }
        catch (JsonException ex)
        {
            throw new MetadataParsingException($"Invalid JSON format: {ex.Message}", ex);
        }
        catch (Exception ex) when (!(ex is MetadataParsingException))
        {
            throw new MetadataParsingException($"Unexpected parsing error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 배치 응답에서 여러 메타데이터 파싱
    /// </summary>
    /// <param name="response">OpenAI 배치 응답</param>
    /// <param name="expectedCount">예상 아이템 수</param>
    /// <param name="extractedBy">추출 서비스 정보</param>
    /// <returns>파싱된 메타데이터 목록</returns>
    public IReadOnlyList<ChunkMetadata> ParseBatchMetadata(
        string response,
        int expectedCount,
        string extractedBy = "OpenAI")
    {
        if (string.IsNullOrWhiteSpace(response))
            throw new MetadataParsingException("Batch response cannot be empty");

        if (expectedCount <= 0)
            throw new ArgumentException("Expected count must be positive", nameof(expectedCount));

        try
        {
            var cleanedJson = CleanJsonResponse(response);
            var jsonDoc = JsonDocument.Parse(cleanedJson);

            if (jsonDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new MetadataParsingException("Batch response must be a JSON array");
            }

            var results = new List<ChunkMetadata>();
            var array = jsonDoc.RootElement;

            // 배열 크기 검증
            if (array.GetArrayLength() != expectedCount)
            {
                throw new MetadataParsingException(
                    $"Expected {expectedCount} items, but got {array.GetArrayLength()}");
            }

            for (int i = 0; i < array.GetArrayLength(); i++)
            {
                try
                {
                    var metadata = ParseMetadataFromJson(array[i], extractedBy);
                    results.Add(metadata);
                }
                catch (Exception ex)
                {
                    throw new MetadataParsingException(
                        $"Failed to parse item {i}: {ex.Message}", ex);
                }
            }

            return results;
        }
        catch (JsonException ex)
        {
            throw new MetadataParsingException($"Invalid batch JSON format: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// JSON 응답 정리 (마크다운, 코드 블록 제거 등)
    /// </summary>
    private static string CleanJsonResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "{}";

        // 코드 블록 제거 (```json ... ```)
        var cleaned = Regex.Replace(response, @"```json\s*", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"```\s*$", "", RegexOptions.IgnoreCase);

        // 앞뒤 공백 및 마크다운 제거
        cleaned = cleaned.Trim();
        cleaned = Regex.Replace(cleaned, @"^#+\s*.*$", "", RegexOptions.Multiline);

        // JSON 객체 추출 시도
        var jsonMatch = Regex.Match(cleaned, @"\{.*\}", RegexOptions.Singleline);
        if (jsonMatch.Success)
        {
            cleaned = jsonMatch.Value;
        }

        // 배열인 경우
        var arrayMatch = Regex.Match(cleaned, @"\[.*\]", RegexOptions.Singleline);
        if (arrayMatch.Success && !jsonMatch.Success)
        {
            cleaned = arrayMatch.Value;
        }

        return cleaned;
    }

    /// <summary>
    /// JsonElement에서 ChunkMetadata 생성
    /// </summary>
    private static ChunkMetadata ParseMetadataFromJson(JsonElement element, string extractedBy)
    {
        var builder = ChunkMetadata.Builder();

        // 필수 필드 파싱
        if (element.TryGetProperty("title", out var titleElement))
        {
            builder.WithTitle(SafeGetString(titleElement, 100));
        }

        if (element.TryGetProperty("summary", out var summaryElement))
        {
            builder.WithSummary(SafeGetString(summaryElement, 200));
        }

        // 키워드 파싱
        if (element.TryGetProperty("keywords", out var keywordsElement) &&
            keywordsElement.ValueKind == JsonValueKind.Array)
        {
            var keywords = new List<string>();
            foreach (var keyword in keywordsElement.EnumerateArray())
            {
                var keywordStr = SafeGetString(keyword, 50);
                if (!string.IsNullOrWhiteSpace(keywordStr))
                {
                    keywords.Add(keywordStr.ToLowerInvariant());
                }
            }
            builder.WithKeywords(keywords.Take(15).ToArray()); // 최대 15개
        }

        // 엔터티 파싱
        if (element.TryGetProperty("entities", out var entitiesElement) &&
            entitiesElement.ValueKind == JsonValueKind.Array)
        {
            var entities = new List<string>();
            foreach (var entity in entitiesElement.EnumerateArray())
            {
                var entityStr = SafeGetString(entity, 100);
                if (!string.IsNullOrWhiteSpace(entityStr))
                {
                    entities.Add(entityStr);
                }
            }
            builder.WithEntities(entities.Take(20).ToArray()); // 최대 20개
        }

        // 생성된 질문 파싱
        if (element.TryGetProperty("generated_questions", out var questionsElement) &&
            questionsElement.ValueKind == JsonValueKind.Array)
        {
            var questions = new List<string>();
            foreach (var question in questionsElement.EnumerateArray())
            {
                var questionStr = SafeGetString(question, 200);
                if (!string.IsNullOrWhiteSpace(questionStr))
                {
                    questions.Add(questionStr);
                }
            }
            builder.WithQuestions(questions.Take(10).ToArray()); // 최대 10개
        }

        // 품질 점수 파싱
        if (element.TryGetProperty("quality_score", out var qualityElement))
        {
            var qualityScore = SafeGetFloat(qualityElement);
            builder.WithQualityScore(Math.Clamp(qualityScore, 0.0f, 1.0f));
        }

        // 사용자 정의 필드 파싱
        if (element.TryGetProperty("domain_specific_fields", out var customElement) &&
            customElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in customElement.EnumerateObject())
            {
                builder.WithCustomField(property.Name, property.Value.ToString());
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// 안전한 문자열 추출 (길이 제한 포함)
    /// </summary>
    private static string SafeGetString(JsonElement element, int maxLength = int.MaxValue)
    {
        try
        {
            var value = element.GetString() ?? string.Empty;
            return value.Length > maxLength ? value[..maxLength] : value;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 안전한 float 추출
    /// </summary>
    private static float SafeGetFloat(JsonElement element)
    {
        try
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number => element.GetSingle(),
                JsonValueKind.String when float.TryParse(element.GetString(), out var parsed) => parsed,
                _ => 0.5f
            };
        }
        catch
        {
            return 0.5f;
        }
    }

    /// <summary>
    /// 파싱 결과 검증
    /// </summary>
    public static ValidationResult ValidateMetadata(ChunkMetadata metadata)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(metadata.Title))
            issues.Add("Title is missing or empty");

        if (metadata.Title.Length > 100)
            issues.Add("Title exceeds 100 characters");

        if (string.IsNullOrWhiteSpace(metadata.Summary))
            issues.Add("Summary is missing or empty");

        if (metadata.Summary.Length > 200)
            issues.Add("Summary exceeds 200 characters");

        if (metadata.QualityScore < 0.0f || metadata.QualityScore > 1.0f)
            issues.Add("Quality score must be between 0.0 and 1.0");

        if (!metadata.Keywords.Any())
            issues.Add("No keywords extracted");

        if (metadata.Keywords.Count > 15)
            issues.Add("Too many keywords (max 15)");

        return new ValidationResult
        {
            IsValid = !issues.Any(),
            Issues = issues,
            QualityScore = CalculateValidationScore(metadata, issues)
        };
    }

    /// <summary>
    /// 검증 점수 계산
    /// </summary>
    private static float CalculateValidationScore(ChunkMetadata metadata, List<string> issues)
    {
        var baseScore = 1.0f;

        // 이슈별 점수 차감
        baseScore -= issues.Count * 0.1f;

        // 컨텐츠 품질 평가
        if (metadata.Keywords.Any()) baseScore += 0.1f;
        if (metadata.Entities.Any()) baseScore += 0.1f;
        if (metadata.GeneratedQuestions.Any()) baseScore += 0.1f;

        return Math.Clamp(baseScore, 0.0f, 1.0f);
    }

    /// <summary>
    /// 테스트용 파서 생성
    /// </summary>
    public static MetadataJsonParser CreateForTesting() => new();
}

/// <summary>
/// 메타데이터 파싱 예외
/// </summary>
public class MetadataParsingException : Exception
{
    public MetadataParsingException(string message) : base(message) { }
    public MetadataParsingException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>
    /// 테스트용 예외 생성
    /// </summary>
    public static MetadataParsingException CreateForTesting(string message = "Test parsing error") =>
        new(message);
}

/// <summary>
/// 검증 결과
/// </summary>
public record ValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
    public float QualityScore { get; init; }

    /// <summary>
    /// 성공적인 검증 결과
    /// </summary>
    public static ValidationResult Success() => new()
    {
        IsValid = true,
        QualityScore = 1.0f
    };

    /// <summary>
    /// 실패한 검증 결과
    /// </summary>
    public static ValidationResult Failure(params string[] issues) => new()
    {
        IsValid = false,
        Issues = issues,
        QualityScore = 0.0f
    };
}