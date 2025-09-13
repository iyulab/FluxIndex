using System;
using System.Collections.Generic;

namespace FluxIndex.Core.Domain.Entities;

/// <summary>
/// 문서 청크 엔티티 - 고도화된 RAG를 위한 메타데이터 포함
/// </summary>
public class DocumentChunk
{
    public string Id { get; set; }
    public string DocumentId { get; set; }
    public string Content { get; private set; }
    public int ChunkIndex { get; private set; }
    public int TotalChunks { get; private set; }
    public float[]? Embedding { get; set; }
    public Dictionary<string, object> Properties { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public int TokenCount { get; set; }
    public float? Score { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    
    // Modern RAG 메타데이터
    public ChunkMetadata ChunkMetadata { get; private set; }
    public List<ChunkRelationship> Relationships { get; private set; }
    public ChunkQuality Quality { get; private set; }

    private DocumentChunk()
    {
        Id = Guid.NewGuid().ToString();
        DocumentId = string.Empty;
        Content = string.Empty;
        Properties = new Dictionary<string, object>();
        Relationships = new List<ChunkRelationship>();
        ChunkMetadata = new ChunkMetadata();
        Quality = new ChunkQuality();
        Metadata = new Dictionary<string, object>();
    }

    public DocumentChunk(string content, int chunkIndex) : this()
    {
        Content = content;
        ChunkIndex = chunkIndex;
        CreatedAt = DateTime.UtcNow;
    }

    public static DocumentChunk Create(
        string documentId, 
        string content, 
        int chunkIndex, 
        int totalChunks)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty", nameof(content));
        if (chunkIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(chunkIndex), "Chunk index must be non-negative");
        if (totalChunks <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalChunks), "Total chunks must be positive");
        if (chunkIndex >= totalChunks)
            throw new ArgumentException("Chunk index must be less than total chunks");

        return new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = documentId,
            Content = content,
            ChunkIndex = chunkIndex,
            TotalChunks = totalChunks,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void SetEmbedding(EmbeddingVector embedding)
    {
        if (embedding == null) throw new ArgumentNullException(nameof(embedding));
        Embedding = embedding.Values;
    }

    public void SetEmbedding(float[] embedding)
    {
        Embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
    }

    public void AddProperty(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Property key cannot be empty", nameof(key));
        
        Properties[key] = value;
    }

    public void SetMetadata(ChunkMetadata metadata)
    {
        ChunkMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    public void AddRelationship(ChunkRelationship relationship)
    {
        if (relationship == null) throw new ArgumentNullException(nameof(relationship));
        Relationships.Add(relationship);
    }

    public void SetQuality(ChunkQuality quality)
    {
        Quality = quality ?? throw new ArgumentNullException(nameof(quality));
    }
}

/// <summary>
/// 청크 메타데이터 - RAG 성능 최적화를 위한 풍부한 컨텍스트
/// </summary>
public class ChunkMetadata
{
    // 텍스트 분석 메타데이터
    public int TokenCount { get; set; }
    public int CharacterCount { get; set; }
    public int SentenceCount { get; set; }
    public double ReadabilityScore { get; set; }
    public string Language { get; set; } = "ko";
    
    // 의미적 메타데이터
    public List<string> Keywords { get; set; } = new();
    public List<string> Entities { get; set; } = new();
    public List<string> Topics { get; set; } = new();
    public string ContentType { get; set; } = "text"; // text, code, table, list
    
    // 구조적 메타데이터
    public int SectionLevel { get; set; } // H1=1, H2=2, etc.
    public string SectionTitle { get; set; } = string.Empty;
    public List<string> Headings { get; set; } = new();
    public string ContextBefore { get; set; } = string.Empty; // 이전 청크 요약
    public string ContextAfter { get; set; } = string.Empty;  // 다음 청크 요약
    
    // 검색 최적화 메타데이터
    public double ImportanceScore { get; set; } // 0.0-1.0
    public List<string> SearchableTerms { get; set; } = new();
    public Dictionary<string, float> KeywordWeights { get; set; } = new();
}

/// <summary>
/// 청크 간 관계 - 그래프 기반 검색을 위한 관계 정보
/// </summary>
public class ChunkRelationship
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SourceChunkId { get; set; } = string.Empty;
    public string TargetChunkId { get; set; } = string.Empty;
    public RelationshipType Type { get; set; }
    public double Strength { get; set; } // 0.0-1.0
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 관계 유형
/// </summary>
public enum RelationshipType
{
    Sequential,     // 순차적 관계 (이전/다음 청크)
    Semantic,       // 의미적 유사성
    Reference,      // 참조 관계
    Causal,         // 인과 관계
    Hierarchical,   // 계층 관계 (부모/자식)
    Similarity,     // 내용 유사성
    Contradiction,  // 상반된 내용
    Elaboration    // 보충 설명
}

/// <summary>
/// 청크 품질 메트릭 - 리랭킹 최적화용
/// </summary>
public class ChunkQuality
{
    // 콘텐츠 품질
    public double ContentCompleteness { get; set; } // 0.0-1.0, 내용 완성도
    public double InformationDensity { get; set; }  // 0.0-1.0, 정보 밀도
    public double Coherence { get; set; }          // 0.0-1.0, 응집성
    public double Uniqueness { get; set; }         // 0.0-1.0, 고유성
    
    // 검색 관련 품질
    public double QueryRelevanceScore { get; set; } // 쿼리 관련성 (동적 계산)
    public double ContextualRelevance { get; set; } // 주변 맥락 관련성
    public double AuthorityScore { get; set; }      // 권위도 점수
    public double FreshnessScore { get; set; }      // 최신성 점수
    
    // 사용자 피드백
    public int PositiveFeedback { get; set; }
    public int NegativeFeedback { get; set; }
    public double UserRating { get; set; } // 평균 사용자 평점
    
    // 성능 메트릭
    public int RetrievalCount { get; set; }    // 검색된 횟수
    public double ClickThroughRate { get; set; } // 클릭률
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 검색 결과 - 고도화된 메타데이터 포함
/// </summary>
public class EnhancedSearchResult
{
    public DocumentChunk? Chunk { get; set; }
    public double SimilarityScore { get; set; }
    public double BM25Score { get; set; }
    public double HybridScore { get; set; }
    public double RerankedScore { get; set; }
    public List<string> MatchedTerms { get; set; } = new();
    public List<ChunkRelationship> RelatedChunks { get; set; } = new();
    public string HighlightedContent { get; set; } = string.Empty;
    public Dictionary<string, object> ExplanationMetadata { get; set; } = new();
}