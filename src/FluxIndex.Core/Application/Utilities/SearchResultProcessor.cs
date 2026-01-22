using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Utilities;

/// <summary>
/// Utilities for processing and filtering vector search results.
/// Centralizes common search result manipulation logic.
/// </summary>
public static class SearchResultProcessor
{
    /// <summary>
    /// Filters and sorts search results by score, returning top-K chunks.
    /// </summary>
    public static IEnumerable<DocumentChunk> FilterAndSort(
        IEnumerable<VectorSearchResult> results,
        float minScore,
        int topK)
    {
        ArgumentNullException.ThrowIfNull(results);

        return results
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .Select(r => r.Chunk);
    }

    /// <summary>
    /// Filters and sorts search results, returning results with scores.
    /// </summary>
    public static IEnumerable<VectorSearchResult> FilterAndSortWithScores(
        IEnumerable<VectorSearchResult> results,
        float minScore,
        int topK)
    {
        ArgumentNullException.ThrowIfNull(results);

        return results
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(topK);
    }

    /// <summary>
    /// Applies Reciprocal Rank Fusion (RRF) to merge multiple result sets.
    /// Commonly used for hybrid search (vector + keyword).
    /// </summary>
    /// <param name="resultSets">Multiple result sets to fuse.</param>
    /// <param name="topK">Maximum results to return.</param>
    /// <param name="k">RRF constant (default: 60).</param>
    public static IEnumerable<VectorSearchResult> ApplyRRF(
        IEnumerable<IEnumerable<VectorSearchResult>> resultSets,
        int topK,
        int k = 60)
    {
        ArgumentNullException.ThrowIfNull(resultSets);

        var scores = new Dictionary<string, (DocumentChunk Chunk, double RRFScore)>();

        foreach (var resultSet in resultSets)
        {
            var rankedResults = resultSet.ToList();
            for (int rank = 0; rank < rankedResults.Count; rank++)
            {
                var result = rankedResults[rank];
                var chunkId = result.Chunk.Id;
                var rrfScore = 1.0 / (k + rank + 1);

                if (scores.TryGetValue(chunkId, out var existing))
                {
                    scores[chunkId] = (existing.Chunk, existing.RRFScore + rrfScore);
                }
                else
                {
                    scores[chunkId] = (result.Chunk, rrfScore);
                }
            }
        }

        return scores.Values
            .OrderByDescending(x => x.RRFScore)
            .Take(topK)
            .Select(x => new VectorSearchResult(x.Chunk, (float)x.RRFScore));
    }

    /// <summary>
    /// Applies dynamic threshold adjustment for efficient filtering during search.
    /// Returns true if the candidate list should be trimmed.
    /// </summary>
    public static bool ShouldTrimCandidates(
        int currentCount,
        int targetTopK,
        float multiplier = 2.0f)
    {
        return currentCount > (int)(targetTopK * multiplier);
    }

    /// <summary>
    /// Trims candidate list to maintain efficient processing.
    /// </summary>
    public static List<VectorSearchResult> TrimCandidates(
        List<VectorSearchResult> candidates,
        int targetTopK)
    {
        if (candidates.Count <= targetTopK)
            return candidates;

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        return candidates.Take(targetTopK).ToList();
    }

    /// <summary>
    /// Gets the minimum score from current candidate set for dynamic threshold.
    /// </summary>
    public static float GetDynamicThreshold(
        List<VectorSearchResult> candidates,
        int targetTopK,
        float fallbackMinScore)
    {
        if (candidates.Count < targetTopK)
            return fallbackMinScore;

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        return candidates.Count >= targetTopK
            ? candidates[targetTopK - 1].Score
            : fallbackMinScore;
    }

    /// <summary>
    /// Deduplicates results by document ID, keeping the highest-scoring chunk per document.
    /// </summary>
    public static IEnumerable<VectorSearchResult> DeduplicateByDocument(
        IEnumerable<VectorSearchResult> results)
    {
        return results
            .GroupBy(r => r.Chunk.DocumentId)
            .Select(g => g.OrderByDescending(r => r.Score).First());
    }

    /// <summary>
    /// Sets the Score property on each chunk based on search result scores.
    /// </summary>
    public static void SetChunkScores(IEnumerable<VectorSearchResult> results)
    {
        foreach (var result in results)
        {
            result.Chunk.Score = result.Score;
        }
    }
}

/// <summary>
/// Represents a vector search result with chunk and similarity score.
/// </summary>
/// <param name="Chunk">The document chunk.</param>
/// <param name="Score">Similarity score (typically 0-1 for cosine similarity).</param>
public readonly record struct VectorSearchResult(DocumentChunk Chunk, float Score);
