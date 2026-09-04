using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// 하이브리드 검색 결과 (벡터 + FTS5 결합)
/// </summary>
public class HybridSearchResult
{
    /// <summary>
    /// 검색된 문서 청크
    /// </summary>
    public DocumentChunk Chunk { get; set; } = null!;

    /// <summary>
    /// RRF (Reciprocal Rank Fusion) 결합 점수
    /// </summary>
    public float RrfScore { get; set; }

    /// <summary>
    /// 벡터 검색에서의 순위 (null이면 벡터 검색 결과에 포함되지 않음)
    /// </summary>
    public int? VectorRank { get; set; }

    /// <summary>
    /// FTS5 검색에서의 순위 (null이면 텍스트 검색 결과에 포함되지 않음)
    /// </summary>
    public int? FtsRank { get; set; }

    /// <summary>
    /// 벡터 유사도 점수 (null이면 벡터 검색 결과에 포함되지 않음)
    /// </summary>
    public float? VectorScore { get; set; }

    /// <summary>
    /// BM25 점수 (null이면 텍스트 검색 결과에 포함되지 않음)
    /// </summary>
    public float? Bm25Score { get; set; }

    /// <summary>
    /// 검색 결과가 벡터 검색에서 찾아졌는지 여부
    /// </summary>
    public bool FoundInVectorSearch => VectorRank.HasValue;

    /// <summary>
    /// 검색 결과가 텍스트 검색에서 찾아졌는지 여부
    /// </summary>
    public bool FoundInTextSearch => FtsRank.HasValue;

    /// <summary>
    /// 양쪽 검색에서 모두 찾아졌는지 여부
    /// </summary>
    public bool FoundInBoth => FoundInVectorSearch && FoundInTextSearch;
}
