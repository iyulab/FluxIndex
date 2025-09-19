using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxIndex.AI.OpenAI.Prompts;

/// <summary>
/// 메타데이터 추출을 위한 프롬프트 템플릿 모음
/// 테스트 가능하고 버전 관리가 가능한 설계
/// </summary>
public static class MetadataPrompts
{
    /// <summary>
    /// 기본 메타데이터 추출 프롬프트 (JSON Schema 강제)
    /// </summary>
    public const string ExtractionPrompt = @"
You are an expert text analyst. Extract structured metadata from the provided text chunk.

CRITICAL: Respond with ONLY a valid JSON object. No explanations, no markdown, no code blocks.

Required JSON Schema:
{
  ""title"": ""Descriptive title (max 100 chars)"",
  ""summary"": ""Concise summary (max 200 chars)"",
  ""keywords"": [""keyword1"", ""keyword2"", ""keyword3""],
  ""entities"": [""Entity1"", ""Entity2"", ""Entity3""],
  ""generated_questions"": [""Question 1?"", ""Question 2?""],
  ""quality_score"": 0.85
}

Extraction Rules:
1. Title: Clear, descriptive, under 100 characters
2. Summary: Essential information, under 200 characters
3. Keywords: 3-10 relevant terms/phrases, lowercase
4. Entities: People, organizations, locations, concepts (3-15 items)
5. Questions: 2-5 questions answerable from content
6. Quality Score: 0.0 (poor) to 1.0 (excellent) based on:
   - Content clarity and completeness
   - Information density
   - Potential usefulness for search

Text to analyze:
{content}

{context_section}

Return only the JSON object:";

    /// <summary>
    /// 배치 처리용 프롬프트 (여러 청크 동시 처리)
    /// </summary>
    public const string BatchExtractionPrompt = @"
You are an expert text analyst. Extract structured metadata from multiple text chunks.

CRITICAL: Respond with ONLY a valid JSON array. Each element must follow the exact schema.

Required JSON Schema for each chunk:
{
  ""title"": ""Descriptive title (max 100 chars)"",
  ""summary"": ""Concise summary (max 200 chars)"",
  ""keywords"": [""keyword1"", ""keyword2""],
  ""entities"": [""Entity1"", ""Entity2""],
  ""generated_questions"": [""Question?""],
  ""quality_score"": 0.85
}

Process each chunk independently. Return an array with {chunk_count} elements in the same order.

Text chunks to analyze:
{chunks}

Return only the JSON array:";

    /// <summary>
    /// 도메인 특화 메타데이터 추출 프롬프트
    /// </summary>
    public const string DomainSpecificPrompt = @"
You are a domain expert in {domain}. Extract specialized metadata from the text.

Focus on {domain}-specific terms, concepts, and relationships.

Required JSON Schema:
{
  ""title"": ""Domain-focused title"",
  ""summary"": ""Domain-specific summary"",
  ""keywords"": [""domain-specific keywords""],
  ""entities"": [""domain entities""],
  ""generated_questions"": [""domain-focused questions""],
  ""quality_score"": 0.85,
  ""domain_specific_fields"": {
    ""field1"": ""value1"",
    ""field2"": ""value2""
  }
}

Domain: {domain}
Text to analyze:
{content}

Return only the JSON object:";

    /// <summary>
    /// 품질 검증 프롬프트 (추출된 메타데이터 검증용)
    /// </summary>
    public const string QualityValidationPrompt = @"
Validate the quality of extracted metadata against the original text.

Original Text:
{content}

Extracted Metadata:
{metadata}

Evaluate and return JSON:
{
  ""title_accuracy"": 0.9,
  ""summary_completeness"": 0.8,
  ""keyword_relevance"": 0.85,
  ""entity_accuracy"": 0.9,
  ""question_answerability"": 0.7,
  ""overall_quality"": 0.83,
  ""issues"": [""Missing key concept"", ""Irrelevant keyword""],
  ""suggestions"": [""Add more specific terms""]
}

Return only the JSON object:";

    /// <summary>
    /// 프롬프트 빌더 - 동적으로 프롬프트 구성
    /// </summary>
    public class PromptBuilder
    {
        private readonly Dictionary<string, string> _placeholders = new();
        private string _template = ExtractionPrompt;

        /// <summary>
        /// 사용할 프롬프트 템플릿 설정
        /// </summary>
        public PromptBuilder WithTemplate(string template)
        {
            _template = template;
            return this;
        }

        /// <summary>
        /// 컨텐츠 설정
        /// </summary>
        public PromptBuilder WithContent(string content)
        {
            _placeholders["content"] = content ?? throw new ArgumentNullException(nameof(content));
            return this;
        }

        /// <summary>
        /// 컨텍스트 설정 (선택사항)
        /// </summary>
        public PromptBuilder WithContext(string? context)
        {
            if (!string.IsNullOrWhiteSpace(context))
            {
                _placeholders["context_section"] = $"\nAdditional Context:\n{context}";
            }
            else
            {
                _placeholders["context_section"] = "";
            }
            return this;
        }

        /// <summary>
        /// 배치 처리용 청크 설정
        /// </summary>
        public PromptBuilder WithChunks(IReadOnlyList<string> chunks)
        {
            if (chunks == null || chunks.Count == 0)
                throw new ArgumentException("Chunks cannot be null or empty", nameof(chunks));

            var chunksText = string.Join("\n\n",
                chunks.Select((chunk, index) => $"=== Chunk {index + 1} ===\n{chunk}"));

            _placeholders["chunks"] = chunksText;
            _placeholders["chunk_count"] = chunks.Count.ToString();
            return this;
        }

        /// <summary>
        /// 도메인 설정
        /// </summary>
        public PromptBuilder WithDomain(string domain)
        {
            _placeholders["domain"] = domain ?? throw new ArgumentNullException(nameof(domain));
            return this;
        }

        /// <summary>
        /// 메타데이터 설정 (검증용)
        /// </summary>
        public PromptBuilder WithMetadata(string metadata)
        {
            _placeholders["metadata"] = metadata ?? throw new ArgumentNullException(nameof(metadata));
            return this;
        }

        /// <summary>
        /// 사용자 정의 플레이스홀더 추가
        /// </summary>
        public PromptBuilder WithPlaceholder(string key, string value)
        {
            _placeholders[key] = value;
            return this;
        }

        /// <summary>
        /// 최종 프롬프트 생성
        /// </summary>
        public string Build()
        {
            var result = _template;

            foreach (var (key, value) in _placeholders)
            {
                result = result.Replace($"{{{key}}}", value);
            }

            // 남은 플레이스홀더 확인 (테스트에서 유용)
            var remainingPlaceholders = System.Text.RegularExpressions.Regex
                .Matches(result, @"\{([^}]+)\}")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Groups[1].Value)
                .ToList();

            if (remainingPlaceholders.Any())
            {
                throw new InvalidOperationException(
                    $"Unresolved placeholders: {string.Join(", ", remainingPlaceholders)}");
            }

            return result;
        }

        /// <summary>
        /// 테스트용 기본 빌더 생성
        /// </summary>
        public static PromptBuilder CreateForTesting() => new()
            .WithContent("This is test content for metadata extraction.")
            .WithContext("Test context information");
    }

    /// <summary>
    /// 프롬프트 버전 관리
    /// </summary>
    public static class Versions
    {
        public const string V1_Basic = "v1.0";
        public const string V2_Enhanced = "v2.0";
        public const string V3_Structured = "v3.0";

        /// <summary>
        /// 버전별 프롬프트 반환
        /// </summary>
        public static string GetPrompt(string version) => version switch
        {
            V1_Basic => ExtractionPrompt,
            V2_Enhanced => BatchExtractionPrompt,
            V3_Structured => DomainSpecificPrompt,
            _ => ExtractionPrompt
        };
    }

    /// <summary>
    /// 토큰 수 추정 (테스트 및 비용 계산용)
    /// </summary>
    public static int EstimateTokenCount(string prompt)
    {
        // 간단한 토큰 수 추정 (실제로는 tiktoken 라이브러리 사용 권장)
        return (prompt.Length / 4) + 50; // 대략적인 추정
    }

    /// <summary>
    /// 프롬프트 검증 (테스트용)
    /// </summary>
    public static bool IsValidPrompt(string prompt)
    {
        return !string.IsNullOrWhiteSpace(prompt) &&
               prompt.Contains("{content}") &&
               prompt.Length > 100 &&
               prompt.Length < 8000;
    }
}