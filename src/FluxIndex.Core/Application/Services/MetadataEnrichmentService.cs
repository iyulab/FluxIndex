using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// 메타데이터 풍부화 서비스 - Modern RAG를 위한 고급 메타데이터 추출
/// </summary>
public class MetadataEnrichmentService : IMetadataEnrichmentService
{
    private readonly ITextAnalysisService _textAnalysisService;
    private readonly IEntityExtractionService _entityExtractionService;
    private readonly IKeywordExtractionService _keywordExtractionService;
    private readonly ILogger<MetadataEnrichmentService> _logger;

    public MetadataEnrichmentService(
        ITextAnalysisService textAnalysisService,
        IEntityExtractionService entityExtractionService,
        IKeywordExtractionService keywordExtractionService,
        ILogger<MetadataEnrichmentService> logger)
    {
        _textAnalysisService = textAnalysisService;
        _entityExtractionService = entityExtractionService;
        _keywordExtractionService = keywordExtractionService;
        _logger = logger;
    }

    /// <summary>
    /// 청크 메타데이터 풍부화
    /// </summary>
    public async Task<ChunkMetadata> EnrichMetadataAsync(
        string content,
        int chunkIndex,
        string? previousChunkContent = null,
        string? nextChunkContent = null,
        Dictionary<string, object>? documentMetadata = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Enriching metadata for chunk {ChunkIndex}", chunkIndex);

        var metadata = new ChunkMetadata();

        // 기본 텍스트 메트릭
        await EnrichTextMetricsAsync(metadata, content);

        // 의미적 메타데이터
        await EnrichSemanticMetadataAsync(metadata, content, cancellationToken);

        // 구조적 메타데이터
        await EnrichStructuralMetadataAsync(metadata, content, previousChunkContent, nextChunkContent);

        // 검색 최적화 메타데이터
        await EnrichSearchMetadataAsync(metadata, content, documentMetadata);

        _logger.LogDebug("Metadata enrichment completed for chunk {ChunkIndex}", chunkIndex);
        return metadata;
    }

    /// <summary>
    /// 청크 관계 분석
    /// </summary>
    public async Task<List<ChunkRelationship>> AnalyzeRelationshipsAsync(
        DocumentChunk sourceChunk,
        IEnumerable<DocumentChunk> candidateChunks,
        CancellationToken cancellationToken = default)
    {
        var relationships = new List<ChunkRelationship>();

        foreach (var candidate in candidateChunks)
        {
            if (candidate.Id == sourceChunk.Id) continue;

            // 순차적 관계 확인
            if (IsSequentialRelationship(sourceChunk, candidate))
            {
                relationships.Add(CreateRelationship(
                    sourceChunk.Id, candidate.Id, 
                    RelationshipType.Sequential, 
                    0.9, "Sequential chunk in document"));
            }

            // 의미적 유사성 확인
            var semanticSimilarity = await CalculateSemanticSimilarityAsync(
                sourceChunk.Content, candidate.Content, cancellationToken);
            
            if (semanticSimilarity > 0.7)
            {
                relationships.Add(CreateRelationship(
                    sourceChunk.Id, candidate.Id,
                    RelationshipType.Semantic,
                    semanticSimilarity,
                    $"High semantic similarity ({semanticSimilarity:F2})"));
            }

            // 계층적 관계 확인
            if (IsHierarchicalRelationship(sourceChunk, candidate))
            {
                relationships.Add(CreateRelationship(
                    sourceChunk.Id, candidate.Id,
                    RelationshipType.Hierarchical,
                    0.8, "Hierarchical structure relationship"));
            }

            // 참조 관계 확인
            if (HasReferenceRelationship(sourceChunk.Content, candidate.Content))
            {
                relationships.Add(CreateRelationship(
                    sourceChunk.Id, candidate.Id,
                    RelationshipType.Reference,
                    0.75, "Content reference detected"));
            }
        }

        return relationships;
    }

    /// <summary>
    /// 청크 품질 평가
    /// </summary>
    public async Task<ChunkQuality> EvaluateQualityAsync(
        DocumentChunk chunk,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var quality = new ChunkQuality();

        // 콘텐츠 품질 평가
        quality.ContentCompleteness = EvaluateContentCompleteness(chunk.Content);
        quality.InformationDensity = EvaluateInformationDensity(chunk.Content);
        quality.Coherence = await EvaluateCoherenceAsync(chunk.Content, cancellationToken);
        quality.Uniqueness = EvaluateUniqueness(chunk.Content);

        // 쿼리 관련성 (쿼리가 제공된 경우)
        if (!string.IsNullOrEmpty(query))
        {
            quality.QueryRelevanceScore = await CalculateQueryRelevanceAsync(
                chunk.Content, query, cancellationToken);
        }

        // 맥락적 관련성
        quality.ContextualRelevance = EvaluateContextualRelevance(chunk);

        // 권위도 점수 (메타데이터 기반)
        quality.AuthorityScore = EvaluateAuthorityScore(chunk.ChunkMetadata);

        // 최신성 점수
        quality.FreshnessScore = EvaluateFreshnessScore(chunk.CreatedAt);

        return quality;
    }

    #region Private Methods

    private async Task EnrichTextMetricsAsync(ChunkMetadata metadata, string content)
    {
        metadata.CharacterCount = content.Length;
        metadata.TokenCount = EstimateTokenCount(content);
        metadata.SentenceCount = CountSentences(content);
        metadata.ReadabilityScore = await CalculateReadabilityScoreAsync(content);
        metadata.Language = DetectLanguage(content);
    }

    private async Task EnrichSemanticMetadataAsync(
        ChunkMetadata metadata, 
        string content, 
        CancellationToken cancellationToken)
    {
        // 키워드 추출
        if (_keywordExtractionService != null)
        {
            var keywords = await _keywordExtractionService.ExtractKeywordsAsync(content, cancellationToken);
            metadata.Keywords = keywords.Take(10).ToList();

            // 키워드 가중치 계산
            metadata.KeywordWeights = keywords.Take(10).ToDictionary(
                k => k, k => CalculateKeywordWeight(k, content));
        }

        // 엔터티 추출
        if (_entityExtractionService != null)
        {
            var entities = await _entityExtractionService.ExtractEntitiesAsync(content, cancellationToken);
            metadata.Entities = entities.Take(20).ToList();
        }

        // 토픽 분류
        metadata.Topics = await ClassifyTopicsAsync(content, cancellationToken);

        // 콘텐츠 타입 분류
        metadata.ContentType = ClassifyContentType(content);
    }

    private async Task EnrichStructuralMetadataAsync(
        ChunkMetadata metadata,
        string content,
        string? previousContent,
        string? nextContent)
    {
        // 섹션 레벨 및 제목 추출
        ExtractSectionInfo(metadata, content);

        // 제목 추출
        metadata.Headings = ExtractHeadings(content);

        // 맥락 요약 생성
        if (!string.IsNullOrEmpty(previousContent))
        {
            metadata.ContextBefore = await SummarizeContentAsync(previousContent, 50);
        }

        if (!string.IsNullOrEmpty(nextContent))
        {
            metadata.ContextAfter = await SummarizeContentAsync(nextContent, 50);
        }
    }

    private async Task EnrichSearchMetadataAsync(
        ChunkMetadata metadata,
        string content,
        Dictionary<string, object>? documentMetadata)
    {
        // 중요도 점수 계산
        metadata.ImportanceScore = await CalculateImportanceScoreAsync(content, documentMetadata);

        // 검색 가능 용어 추출
        metadata.SearchableTerms = ExtractSearchableTerms(content);
    }

    private bool IsSequentialRelationship(DocumentChunk source, DocumentChunk candidate)
    {
        if (source.DocumentId != candidate.DocumentId) return false;
        return Math.Abs(source.ChunkIndex - candidate.ChunkIndex) == 1;
    }

    private async Task<double> CalculateSemanticSimilarityAsync(
        string content1, 
        string content2, 
        CancellationToken cancellationToken)
    {
        if (_textAnalysisService != null)
        {
            return await _textAnalysisService.CalculateSimilarityAsync(content1, content2, cancellationToken);
        }
        
        // Fallback: 단순 단어 기반 유사도
        return CalculateSimpleWordSimilarity(content1, content2);
    }

    private bool IsHierarchicalRelationship(DocumentChunk source, DocumentChunk candidate)
    {
        return source.ChunkMetadata.SectionLevel != candidate.ChunkMetadata.SectionLevel &&
               source.DocumentId == candidate.DocumentId;
    }

    private bool HasReferenceRelationship(string sourceContent, string candidateContent)
    {
        // 참조 패턴 감지 (예: "위에서 언급한", "앞서 설명한" 등)
        var referencePatterns = new[]
        {
            @"위에서\s+(언급|설명|말)한",
            @"앞서\s+(언급|설명|말)한",
            @"이전\s+(장|절|부분)에서",
            @"다음\s+(장|절|부분)에서"
        };

        return referencePatterns.Any(pattern => 
            Regex.IsMatch(sourceContent, pattern, RegexOptions.IgnoreCase));
    }

    private ChunkRelationship CreateRelationship(
        string sourceId, 
        string targetId, 
        RelationshipType type, 
        double strength, 
        string description)
    {
        return new ChunkRelationship
        {
            SourceChunkId = sourceId,
            TargetChunkId = targetId,
            Type = type,
            Strength = strength,
            Description = description
        };
    }

    private double EvaluateContentCompleteness(string content)
    {
        // 완성도 평가: 문장 완성도, 구두점 사용 등
        var sentences = content.Split('.', '!', '?').Where(s => !string.IsNullOrWhiteSpace(s));
        var completeSentences = sentences.Count(s => s.Trim().Length > 10);
        return Math.Min(1.0, (double)completeSentences / Math.Max(1, sentences.Count()));
    }

    private double EvaluateInformationDensity(string content)
    {
        // 정보 밀도: 명사, 동사 비율 등
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var meaningfulWords = words.Count(w => w.Length > 3);
        return Math.Min(1.0, (double)meaningfulWords / Math.Max(1, words.Length));
    }

    private async Task<double> EvaluateCoherenceAsync(string content, CancellationToken cancellationToken)
    {
        // 응집성 평가: 문장 간 연결성, 주제 일관성 등
        if (_textAnalysisService != null)
        {
            return await _textAnalysisService.EvaluateCoherenceAsync(content, cancellationToken);
        }
        
        // Fallback: 단순 휴리스틱
        return CalculateSimpleCoherence(content);
    }

    private double EvaluateUniqueness(string content)
    {
        // 고유성 평가: 반복 패턴 감지
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var uniqueWords = words.Distinct().Count();
        return (double)uniqueWords / Math.Max(1, words.Length);
    }

    private async Task<double> CalculateQueryRelevanceAsync(
        string content, 
        string query, 
        CancellationToken cancellationToken)
    {
        if (_textAnalysisService != null)
        {
            return await _textAnalysisService.CalculateRelevanceAsync(content, query, cancellationToken);
        }

        // Fallback: 단순 키워드 매칭
        return CalculateSimpleRelevance(content, query);
    }

    private double EvaluateContextualRelevance(DocumentChunk chunk)
    {
        // 맥락적 관련성: 주변 청크와의 관련성
        var relationshipStrength = chunk.Relationships
            .Where(r => r.Type == RelationshipType.Semantic || r.Type == RelationshipType.Sequential)
            .Average(r => r.Strength);
        
        return double.IsNaN(relationshipStrength) ? 0.5 : relationshipStrength;
    }

    private double EvaluateAuthorityScore(ChunkMetadata metadata)
    {
        // 권위도 점수: 키워드 품질, 엔터티 수 등
        var keywordScore = Math.Min(1.0, metadata.Keywords.Count / 10.0);
        var entityScore = Math.Min(1.0, metadata.Entities.Count / 20.0);
        return (keywordScore + entityScore) / 2.0;
    }

    private double EvaluateFreshnessScore(DateTime createdAt)
    {
        var daysSinceCreation = (DateTime.UtcNow - createdAt).TotalDays;
        return Math.Max(0.1, 1.0 - (daysSinceCreation / 365.0)); // 1년 후 0.1점
    }

    #region Utility Methods

    private int EstimateTokenCount(string content) => content.Length / 4;
    
    private int CountSentences(string content) => 
        content.Split('.', '!', '?').Count(s => !string.IsNullOrWhiteSpace(s));

    private async Task<double> CalculateReadabilityScoreAsync(string content)
    {
        // 간단한 가독성 점수 (Flesch Reading Ease 근사)
        var sentences = CountSentences(content);
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var syllables = EstimateSyllables(content);

        if (sentences == 0 || words == 0) return 0.5;

        var avgWordsPerSentence = (double)words / sentences;
        var avgSyllablesPerWord = (double)syllables / words;

        await Task.CompletedTask; // Placeholder for future async readability analysis
        return Math.Max(0.0, Math.Min(1.0,
            (206.835 - (1.015 * avgWordsPerSentence) - (84.6 * avgSyllablesPerWord)) / 100.0));
    }

    private int EstimateSyllables(string content)
    {
        // 한국어 음절 추정 (간단한 휴리스틱)
        return content.Where(c => IsKoreanSyllable(c) || IsVowel(c)).Count();
    }

    private bool IsKoreanSyllable(char c) => c >= 0xAC00 && c <= 0xD7AF;
    private bool IsVowel(char c) => "aeiouAEIOU".Contains(c);

    private string DetectLanguage(string content)
    {
        var koreanChars = content.Count(IsKoreanSyllable);
        var totalChars = content.Length;
        return koreanChars > totalChars * 0.3 ? "ko" : "en";
    }

    private async Task<List<string>> ClassifyTopicsAsync(string content, CancellationToken cancellationToken)
    {
        // 간단한 토픽 분류 (실제로는 ML 모델 사용)
        var topics = new List<string>();

        var topicKeywords = new Dictionary<string, string[]>
        {
            ["기술"] = new[] { "기술", "개발", "소프트웨어", "AI", "머신러닝", "데이터" },
            ["비즈니스"] = new[] { "비즈니스", "경영", "전략", "마케팅", "수익", "고객" },
            ["교육"] = new[] { "교육", "학습", "강의", "수업", "학생", "선생님" },
            ["건강"] = new[] { "건강", "의료", "병원", "치료", "약물", "환자" }
        };

        foreach (var (topic, keywords) in topicKeywords)
        {
            if (keywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                topics.Add(topic);
            }
        }

        await Task.CompletedTask; // Placeholder for future async topic classification
        return topics;
    }

    private string ClassifyContentType(string content)
    {
        if (Regex.IsMatch(content, @"```|public\s+class|function\s+\w+\("))
            return "code";
        if (Regex.IsMatch(content, @"\|\s*\w+\s*\|\s*\w+\s*\|"))
            return "table";
        if (Regex.IsMatch(content, @"^\s*[-*+]\s", RegexOptions.Multiline))
            return "list";
        return "text";
    }

    private void ExtractSectionInfo(ChunkMetadata metadata, string content)
    {
        var headingMatch = Regex.Match(content, @"^(#+)\s+(.+)$", RegexOptions.Multiline);
        if (headingMatch.Success)
        {
            metadata.SectionLevel = headingMatch.Groups[1].Value.Length;
            metadata.SectionTitle = headingMatch.Groups[2].Value.Trim();
        }
    }

    private List<string> ExtractHeadings(string content)
    {
        var headings = new List<string>();
        var matches = Regex.Matches(content, @"^(#+)\s+(.+)$", RegexOptions.Multiline);
        
        foreach (Match match in matches)
        {
            headings.Add(match.Groups[2].Value.Trim());
        }
        
        return headings;
    }

    private async Task<string> SummarizeContentAsync(string content, int maxLength)
    {
        if (content.Length <= maxLength) return content;

        // 간단한 요약: 첫 문장 추출
        var firstSentence = content.Split('.', '!', '?').FirstOrDefault()?.Trim();
        await Task.CompletedTask; // Placeholder for future async summarization
        return firstSentence?.Substring(0, Math.Min(firstSentence.Length, maxLength)) ?? "";
    }

    private async Task<double> CalculateImportanceScoreAsync(
        string content,
        Dictionary<string, object>? documentMetadata)
    {
        var score = 0.5; // 기본 점수

        // 길이 기반 가중치
        if (content.Length > 500) score += 0.1;
        if (content.Length > 1000) score += 0.1;

        // 키워드 밀도
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var meaningfulWords = words.Count(w => w.Length > 3);
        score += Math.Min(0.2, meaningfulWords / (double)words.Length);

        await Task.CompletedTask; // Placeholder for future async importance calculation
        return Math.Min(1.0, score);
    }

    private List<string> ExtractSearchableTerms(string content)
    {
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .ToList();
            
        return words;
    }

    private float CalculateKeywordWeight(string keyword, string content)
    {
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var count = words.Count(w => w.Equals(keyword, StringComparison.OrdinalIgnoreCase));
        return Math.Min(1.0f, count / (float)words.Length * 100);
    }

    private double CalculateSimpleWordSimilarity(string content1, string content2)
    {
        var words1 = content1.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var words2 = content2.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        
        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();
        
        return union > 0 ? (double)intersection / union : 0.0;
    }

    private double CalculateSimpleCoherence(string content)
    {
        // 간단한 응집성 계산: 반복 단어 비율
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wordGroups = words.GroupBy(w => w.ToLowerInvariant())
            .Where(g => g.Count() > 1 && g.Key.Length > 3);
            
        return Math.Min(1.0, wordGroups.Count() / (double)words.Length * 10);
    }

    private double CalculateSimpleRelevance(string content, string query)
    {
        var contentWords = content.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var queryWords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        var matchCount = queryWords.Count(qw => contentWords.Contains(qw));
        return (double)matchCount / Math.Max(1, queryWords.Length);
    }

    #endregion

    #endregion
}