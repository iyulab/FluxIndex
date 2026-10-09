using FluxIndex.Core.Domain.ValueObjects;

namespace FluxIndex.Core.Domain.Entities;

/// <summary>
/// 문서 청크 엔티티 - 고도화된 RAG를 위한 메타데이터 포함
/// </summary>
public class DocumentChunk
{
    public string Id { get; set; }

    /// <summary>
    /// The id this chunk is stored under: its own, or — when it carries none — a fresh one that is
    /// written onto the chunk, so the instance a caller hands to a store carries the identity the
    /// store will answer to (docs/REFERENCE.md, "Chunk identity"). Every <c>IVectorStore</c> resolves
    /// the id through here, so a generated id lands on the instance in all of them alike.
    /// </summary>
    public string EnsureId()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString();
        return Id;
    }
    public string DocumentId { get; set; }
    public string Content { get; set; }
    public int ChunkIndex { get; set; }
    public int TotalChunks { get; set; }
    public float[]? Embedding { get; set; }
    public Dictionary<string, object> Properties { get; private set; }
    public DateTime CreatedAt { get; set; }
    public int TokenCount { get; set; }
    public float? Score { get; set; }
    /// <summary>
    /// Caller metadata. On read every store returns plain values: a string is a <see cref="string"/>, an integral
    /// number a <see cref="long"/>, any other number a <see cref="double"/>, a boolean a <see cref="bool"/>, an array a
    /// <c>List&lt;object?&gt;</c> and an object a <c>Dictionary&lt;string, object&gt;</c>, never a
    /// <see cref="System.Text.Json.JsonElement"/> (see <see cref="Application.Utilities.MetadataValues"/>). The Qdrant
    /// store keeps every value as text, so there a number or boolean reads back as its string.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    // Modern RAG 메타데이터
    public ChunkMetadata ChunkMetadata { get; private set; }
    public ChunkQuality Quality { get; private set; }

    public DocumentChunk()
    {
        Id = Guid.NewGuid().ToString();
        DocumentId = string.Empty;
        Content = string.Empty;
        Properties = new Dictionary<string, object>();
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

    /// <summary>
    /// A copy of this chunk carrying <paramref name="content"/> in place of its own — every other field,
    /// the id included, is the same. For a caller that must hand out rewritten text (a sanitized search
    /// result) without rewriting the instance a store or cache still holds. Collections are copied, not
    /// shared; the value objects it holds (chunk metadata, quality) are shared.
    /// </summary>
    public DocumentChunk WithContent(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var copy = new DocumentChunk
        {
            Id = Id,
            DocumentId = DocumentId,
            Content = content,
            ChunkIndex = ChunkIndex,
            TotalChunks = TotalChunks,
            Embedding = Embedding,
            Properties = new Dictionary<string, object>(Properties),
            CreatedAt = CreatedAt,
            TokenCount = TokenCount,
            Score = Score,
            Metadata = Metadata is null ? null : new Dictionary<string, object>(Metadata),
            ChunkMetadata = ChunkMetadata,
            Quality = Quality,
        };
        return copy;
    }

    public void SetEmbedding(EmbeddingVector embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);
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
/// How two chunks relate — the type of a hierarchy graph's <see cref="Models.ChunkRelationship"/>.
/// </summary>
/// <remarks>
/// The names and values are fixed. The graph stores keep the member name, and earlier releases stored the numeric value in
/// chunk metadata, so neither may change meaning. Add new members at the end with the next value.
/// </remarks>
public enum RelationshipType
{
    /// <summary>순차적 관계 (이전/다음 청크)</summary>
    Sequential = 0,

    /// <summary>의미적 관계 (주제/개념 유사성)</summary>
    Semantic = 1,

    /// <summary>참조 관계 (명시적 언급)</summary>
    Reference = 2,

    /// <summary>인과 관계 (원인-결과)</summary>
    Causal = 3,

    /// <summary>계층 관계 (부모/자식)</summary>
    Hierarchical = 4,

    /// <summary>내용 유사성</summary>
    Similarity = 5,

    /// <summary>상반된 내용</summary>
    Contradiction = 6,

    /// <summary>보충 설명</summary>
    Elaboration = 7,

    /// <summary>대비 관계 (비교/대조)</summary>
    Contrastive = 8,

    /// <summary>보완 관계 (상호 보완)</summary>
    Complementary = 9,
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

