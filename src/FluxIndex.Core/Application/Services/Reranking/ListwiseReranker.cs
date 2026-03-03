using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;

namespace FluxIndex.Core.Application.Services.Reranking;

/// <summary>
/// Listwise reranker implementation supporting multiple ranking methods.
/// Implements sliding window, tournament, attention-based, and hybrid approaches.
///
/// Reference: "Learning to Rank: From Pairwise Approach to Listwise Approach"
/// https://www.microsoft.com/en-us/research/publication/learning-to-rank-from-pairwise-approach-to-listwise-approach/
/// </summary>
public partial class ListwiseReranker : IListwiseReranker
{
    private static readonly char[] RankingSplitSeparators = [',', ' ', '\n', '\r'];
    private static readonly char[] TokenizeSeparators = [' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?'];

    private readonly ITextCompletionService? _llmService;
    private readonly IEmbeddingService? _embeddingService;
    private readonly ILogger<ListwiseReranker> _logger;
    private readonly Random _random;

    public ListwiseReranker(
        ITextCompletionService? llmService = null,
        IEmbeddingService? embeddingService = null,
        ILogger<ListwiseReranker>? logger = null)
    {
        _llmService = llmService;
        _embeddingService = embeddingService;
        _logger = logger ?? NullLogger<ListwiseReranker>.Instance;
        _random = new Random();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ListwiseRerankResult>> RerankAsync(
        string query,
        IEnumerable<RetrievalCandidate> candidates,
        ListwiseRerankOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ListwiseRerankOptions();
        var candidateList = candidates.ToList();

        if (candidateList.Count == 0)
        {
            LogListwiseReranker11(_logger);
            return Array.Empty<ListwiseRerankResult>();
        }

        if (_logger.IsEnabled(LogLevel.Warning))
            LogListwiseReranker10(_logger, candidateList.Count, options.Method);

        var results = options.Method switch
        {
            ListwiseMethod.SlidingWindow => await RerankSlidingWindowAsync(query, candidateList, options, cancellationToken),
            ListwiseMethod.Tournament => await RerankTournamentAsync(query, candidateList, options, cancellationToken),
            ListwiseMethod.DirectLlm => await RerankDirectLlmAsync(query, candidateList, options, cancellationToken),
            ListwiseMethod.AttentionBased => await RerankAttentionBasedAsync(query, candidateList, options, cancellationToken),
            ListwiseMethod.Hybrid => await RerankHybridAsync(query, candidateList, options, cancellationToken),
            _ => throw new ArgumentException($"Unknown listwise method: {options.Method}")
        };

        // Apply thresholds and limits
        var finalResults = results
            .Where(r => r.ListwiseScore >= options.ScoreThreshold)
            .OrderByDescending(r => r.ListwiseScore)
            .Take(options.TopN)
            .Select((r, i) => r with { NewRank = i + 1 })
            .ToList();

        LogListwiseReranker9(_logger, candidateList.Count, finalResults.Count);

        return finalResults;
    }

    /// <inheritdoc />
    public async Task<PairwisePreference> ComputePairwisePreferenceAsync(
        string query,
        string docA,
        string docB,
        CancellationToken cancellationToken = default)
    {
        if (_llmService != null)
        {
            return await ComputeLlmPairwisePreferenceAsync(query, docA, docB, cancellationToken);
        }

        // Fall back to embedding-based comparison
        if (_embeddingService != null)
        {
            return await ComputeEmbeddingPairwisePreferenceAsync(query, docA, docB, cancellationToken);
        }

        // Fall back to lexical similarity
        return ComputeLexicalPairwisePreference(query, docA, docB);
    }

    /// <inheritdoc />
    public ListwiseRerankModelInfo GetModelInfo()
    {
        var supportedMethods = new List<ListwiseMethod> { ListwiseMethod.AttentionBased };

        if (_llmService != null)
        {
            supportedMethods.Add(ListwiseMethod.SlidingWindow);
            supportedMethods.Add(ListwiseMethod.Tournament);
            supportedMethods.Add(ListwiseMethod.DirectLlm);
        }

        if (_llmService != null || _embeddingService != null)
        {
            supportedMethods.Add(ListwiseMethod.Hybrid);
        }

        return new ListwiseRerankModelInfo
        {
            Name = "FluxIndex Listwise Reranker",
            Version = "1.0.0",
            SupportedMethods = supportedMethods,
            LlmAvailable = _llmService != null,
            EmbeddingsAvailable = _embeddingService != null,
            MaxCandidatesPerPass = _llmService != null ? 20 : 100,
            EstimatedLatencyPerCandidateMs = _llmService != null ? 50 : 5,
            Capabilities = new Dictionary<string, object>
            {
                ["sliding_window"] = _llmService != null,
                ["tournament"] = _llmService != null,
                ["attention_scoring"] = _embeddingService != null,
                ["pairwise_comparison"] = true
            }
        };
    }

    #region Sliding Window Method

    /// <summary>
    /// Sliding window reranking - processes documents in overlapping windows
    /// and merges rankings using position averaging.
    /// </summary>
    private async Task<List<ListwiseRerankResult>> RerankSlidingWindowAsync(
        string query,
        List<RetrievalCandidate> candidates,
        ListwiseRerankOptions options,
        CancellationToken cancellationToken)
    {
        if (_llmService == null)
        {
            LogListwiseReranker8(_logger);
            return await RerankAttentionBasedAsync(query, candidates, options, cancellationToken);
        }

        var windowSize = options.WindowSize;
        var step = options.WindowStep;
        var positionScores = new Dictionary<string, List<int>>();

        // Initialize position tracking
        foreach (var candidate in candidates)
        {
            positionScores[candidate.Id] = new List<int>();
        }

        // Process overlapping windows
        for (int start = 0; start < candidates.Count; start += step)
        {
            var window = candidates
                .Skip(start)
                .Take(windowSize)
                .ToList();

            if (window.Count < 2)
                break;

            cancellationToken.ThrowIfCancellationRequested();

            var windowRanking = await RankWindowWithLlmAsync(query, window, options, cancellationToken);

            // Record positions in this window
            for (int i = 0; i < windowRanking.Count; i++)
            {
                var id = windowRanking[i];
                positionScores[id].Add(i + 1);
            }
        }

        // Calculate average position and convert to scores
        var results = candidates.Select(candidate =>
        {
            var positions = positionScores[candidate.Id];
            var avgPosition = positions.Count != 0 ? positions.Average() : candidates.Count;
            var score = (float)(1.0 - (avgPosition - 1) / Math.Max(1, candidates.Count - 1));

            // Blend with initial score
            var blendedScore = (1 - options.InitialScoreWeight) * score +
                               options.InitialScoreWeight * candidate.InitialScore;

            return new ListwiseRerankResult
            {
                Id = candidate.Id,
                DocumentId = candidate.DocumentId,
                ChunkId = candidate.ChunkId,
                Content = candidate.Content,
                InitialScore = candidate.InitialScore,
                InitialRank = candidate.InitialRank,
                ListwiseScore = (float)blendedScore,
                Confidence = positions.Count != 0 ? 1.0f / (1 + StandardDeviation(positions)) : 0.5f,
                Components = new ListwiseScoreComponents
                {
                    SlidingWindowScore = score,
                    InitialScore = candidate.InitialScore,
                    Weights = new Dictionary<string, float>
                    {
                        ["sliding_window"] = 1 - options.InitialScoreWeight,
                        ["initial"] = options.InitialScoreWeight
                    }
                },
                Metadata = candidate.Metadata
            };
        }).ToList();

        return results;
    }

    private async Task<List<string>> RankWindowWithLlmAsync(
        string query,
        List<RetrievalCandidate> window,
        ListwiseRerankOptions options,
        CancellationToken cancellationToken)
    {
        var prompt = BuildWindowRankingPrompt(query, window, options.MaxContentLength);

        try
        {
            var response = await _llmService!.CompleteAsync(
                prompt, new Flux.Abstractions.TextCompletionOptions { MaxTokens = 500, Temperature = options.LlmTemperature }, cancellationToken);

            return ParseRankingResponse(response, window);
        }
        catch (Exception ex)
        {
            LogListwiseReranker7(_logger, ex);
            return window.Select(c => c.Id).ToList();
        }
    }

    private static string BuildWindowRankingPrompt(string query, List<RetrievalCandidate> window, int maxContentLength)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Given the query below, rank the following documents from most to least relevant.");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Query: {query}");
        sb.AppendLine();
        sb.AppendLine("Documents:");

        for (int i = 0; i < window.Count; i++)
        {
            var content = TruncateContent(window[i].Content, maxContentLength);
            sb.AppendLine(CultureInfo.InvariantCulture, $"[{i + 1}] {content}");
            sb.AppendLine();
        }

        sb.AppendLine("Output the document numbers in order of relevance, separated by commas.");
        sb.AppendLine("Example: 3, 1, 2, 5, 4");
        sb.AppendLine();
        sb.AppendLine("Ranking:");

        return sb.ToString();
    }

    private static List<string> ParseRankingResponse(string response, List<RetrievalCandidate> window)
    {
        var numbers = response
            .Split(RankingSplitSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => int.TryParse(s, out _))
            .Select(int.Parse)
            .Where(n => n >= 1 && n <= window.Count)
            .Distinct()
            .ToList();

        // Map numbers to IDs
        var rankedIds = numbers
            .Select(n => window[n - 1].Id)
            .ToList();

        // Add any missing IDs at the end
        var missingIds = window
            .Select(c => c.Id)
            .Except(rankedIds)
            .ToList();

        rankedIds.AddRange(missingIds);

        return rankedIds;
    }

    #endregion

    #region Tournament Method

    /// <summary>
    /// Tournament-style reranking using pairwise comparisons.
    /// Aggregates results using Bradley-Terry model.
    /// </summary>
    private async Task<List<ListwiseRerankResult>> RerankTournamentAsync(
        string query,
        List<RetrievalCandidate> candidates,
        ListwiseRerankOptions options,
        CancellationToken cancellationToken)
    {
        var wins = new Dictionary<string, int>();
        var comparisons = new Dictionary<string, int>();

        foreach (var candidate in candidates)
        {
            wins[candidate.Id] = 0;
            comparisons[candidate.Id] = 0;
        }

        // Generate comparison pairs
        var pairs = GenerateComparisonPairs(candidates, options.MaxPairwiseComparisons);

        LogListwiseReranker6(_logger, pairs.Count);

        // Process comparisons in batches
        var tasks = new List<Task<(string winnerId, string loserId, float confidence)>>();
        var batchSize = Math.Max(1, pairs.Count / options.ParallelBatches);

        foreach (var pair in pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var docA = candidates.First(c => c.Id == pair.Item1);
            var docB = candidates.First(c => c.Id == pair.Item2);

            tasks.Add(ComputeComparisonAsync(query, docA, docB, cancellationToken));
        }

        var results = await Task.WhenAll(tasks);

        // Aggregate results
        foreach (var (winnerId, loserId, confidence) in results)
        {
            if (!string.IsNullOrEmpty(winnerId))
            {
                wins[winnerId]++;
            }
            comparisons[winnerId]++;
            comparisons[loserId]++;
        }

        // Calculate Bradley-Terry scores
        var btScores = CalculateBradleyTerryScores(wins, comparisons);

        // Create results
        var rerankResults = candidates.Select(candidate =>
        {
            var winCount = wins[candidate.Id];
            var compCount = comparisons[candidate.Id];
            var winRate = compCount > 0 ? (float)winCount / compCount : 0.5f;
            var btScore = btScores.GetValueOrDefault(candidate.Id, 0.5f);

            var blendedScore = (1 - options.InitialScoreWeight) * btScore +
                               options.InitialScoreWeight * candidate.InitialScore;

            return new ListwiseRerankResult
            {
                Id = candidate.Id,
                DocumentId = candidate.DocumentId,
                ChunkId = candidate.ChunkId,
                Content = candidate.Content,
                InitialScore = candidate.InitialScore,
                InitialRank = candidate.InitialRank,
                ListwiseScore = (float)blendedScore,
                WinRate = winRate,
                Wins = winCount,
                ComparisonCount = compCount,
                Confidence = Math.Min(1.0f, compCount / 5.0f), // More comparisons = higher confidence
                Components = new ListwiseScoreComponents
                {
                    TournamentScore = btScore,
                    InitialScore = candidate.InitialScore,
                    Weights = new Dictionary<string, float>
                    {
                        ["tournament"] = 1 - options.InitialScoreWeight,
                        ["initial"] = options.InitialScoreWeight
                    }
                },
                Metadata = candidate.Metadata
            };
        }).ToList();

        return rerankResults;
    }

    private List<(string, string)> GenerateComparisonPairs(List<RetrievalCandidate> candidates, int maxComparisons)
    {
        var pairs = new List<(string, string)>();

        // Always compare top candidates with each other
        var topN = Math.Min(10, candidates.Count);
        for (int i = 0; i < topN; i++)
        {
            for (int j = i + 1; j < topN; j++)
            {
                pairs.Add((candidates[i].Id, candidates[j].Id));
            }
        }

        // Add random pairs from remaining candidates
        var remaining = maxComparisons - pairs.Count;
        if (remaining > 0 && candidates.Count > topN)
        {
            for (int i = 0; i < remaining; i++)
            {
                var a = _random.Next(candidates.Count);
                var b = _random.Next(candidates.Count);
                if (a != b)
                {
                    pairs.Add((candidates[a].Id, candidates[b].Id));
                }
            }
        }

        return pairs.Take(maxComparisons).Distinct().ToList();
    }

    private async Task<(string winnerId, string loserId, float confidence)> ComputeComparisonAsync(
        string query,
        RetrievalCandidate docA,
        RetrievalCandidate docB,
        CancellationToken cancellationToken)
    {
        var preference = await ComputePairwisePreferenceAsync(
            query, docA.Content, docB.Content, cancellationToken);

        return preference.Preference switch
        {
            1 => (docA.Id, docB.Id, preference.Confidence),
            -1 => (docB.Id, docA.Id, preference.Confidence),
            _ => (string.Empty, string.Empty, preference.Confidence)
        };
    }

    private static Dictionary<string, float> CalculateBradleyTerryScores(
        Dictionary<string, int> wins,
        Dictionary<string, int> comparisons)
    {
        var scores = new Dictionary<string, float>();
        var total = comparisons.Values.Sum() / 2.0; // Each comparison counted twice

        foreach (var id in wins.Keys)
        {
            var w = wins[id];
            var n = comparisons[id];
            // Simple Bradley-Terry estimate: wins / (wins + losses)
            // with Laplace smoothing
            scores[id] = (w + 1.0f) / (n + 2.0f);
        }

        return scores;
    }

    #endregion

    #region Direct LLM Method

    /// <summary>
    /// Direct LLM ranking - presents all documents and asks for complete ordering.
    /// Best for small candidate sets that fit in context window.
    /// </summary>
    private async Task<List<ListwiseRerankResult>> RerankDirectLlmAsync(
        string query,
        List<RetrievalCandidate> candidates,
        ListwiseRerankOptions options,
        CancellationToken cancellationToken)
    {
        if (_llmService == null)
        {
            LogListwiseReranker5(_logger);
            return await RerankAttentionBasedAsync(query, candidates, options, cancellationToken);
        }

        // Limit candidates for context window
        var maxCandidates = Math.Min(20, candidates.Count);
        var limitedCandidates = candidates.Take(maxCandidates).ToList();

        var ranking = await RankWindowWithLlmAsync(query, limitedCandidates, options, cancellationToken);

        // Create results based on LLM ranking
        var results = new List<ListwiseRerankResult>();
        for (int i = 0; i < ranking.Count; i++)
        {
            var id = ranking[i];
            var candidate = candidates.First(c => c.Id == id);
            var position = i + 1;
            var score = (float)(1.0 - (position - 1.0) / Math.Max(1, ranking.Count - 1));

            var blendedScore = (1 - options.InitialScoreWeight) * score +
                               options.InitialScoreWeight * candidate.InitialScore;

            results.Add(new ListwiseRerankResult
            {
                Id = candidate.Id,
                DocumentId = candidate.DocumentId,
                ChunkId = candidate.ChunkId,
                Content = candidate.Content,
                InitialScore = candidate.InitialScore,
                InitialRank = candidate.InitialRank,
                ListwiseScore = (float)blendedScore,
                Confidence = 0.9f, // High confidence for direct LLM ranking
                Components = new ListwiseScoreComponents
                {
                    InitialScore = candidate.InitialScore,
                    Weights = new Dictionary<string, float>
                    {
                        ["direct_llm"] = 1 - options.InitialScoreWeight,
                        ["initial"] = options.InitialScoreWeight
                    }
                },
                Metadata = candidate.Metadata
            });
        }

        // Add any candidates not included in LLM ranking
        var includedIds = new HashSet<string>(ranking);
        foreach (var candidate in candidates.Where(c => !includedIds.Contains(c.Id)))
        {
            results.Add(new ListwiseRerankResult
            {
                Id = candidate.Id,
                DocumentId = candidate.DocumentId,
                ChunkId = candidate.ChunkId,
                Content = candidate.Content,
                InitialScore = candidate.InitialScore,
                InitialRank = candidate.InitialRank,
                ListwiseScore = candidate.InitialScore * 0.5f, // Penalize unranked
                Confidence = 0.3f,
                Metadata = candidate.Metadata
            });
        }

        return results;
    }

    #endregion

    #region Attention-Based Method

    /// <summary>
    /// Attention-based reranking using query-document attention scores.
    /// Fast and does not require LLM.
    /// </summary>
    private async Task<List<ListwiseRerankResult>> RerankAttentionBasedAsync(
        string query,
        List<RetrievalCandidate> candidates,
        ListwiseRerankOptions options,
        CancellationToken cancellationToken)
    {
        float[]? queryEmbedding = null;
        if (_embeddingService != null)
        {
            try
            {
                queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
            }
            catch (Exception ex)
            {
                LogListwiseReranker4(_logger, ex);
            }
        }

        var results = new List<ListwiseRerankResult>();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            float attentionScore;
            if (queryEmbedding != null && _embeddingService != null)
            {
                attentionScore = await CalculateAttentionScoreAsync(
                    queryEmbedding, candidate.Content, cancellationToken);
            }
            else
            {
                // Fallback to lexical similarity
                attentionScore = CalculateLexicalSimilarity(query, candidate.Content);
            }

            var blendedScore = (1 - options.InitialScoreWeight) * attentionScore +
                               options.InitialScoreWeight * candidate.InitialScore;

            results.Add(new ListwiseRerankResult
            {
                Id = candidate.Id,
                DocumentId = candidate.DocumentId,
                ChunkId = candidate.ChunkId,
                Content = candidate.Content,
                InitialScore = candidate.InitialScore,
                InitialRank = candidate.InitialRank,
                ListwiseScore = (float)blendedScore,
                AttentionScore = attentionScore,
                Confidence = 0.7f,
                Components = new ListwiseScoreComponents
                {
                    AttentionScore = attentionScore,
                    InitialScore = candidate.InitialScore,
                    Weights = new Dictionary<string, float>
                    {
                        ["attention"] = 1 - options.InitialScoreWeight,
                        ["initial"] = options.InitialScoreWeight
                    }
                },
                Metadata = candidate.Metadata
            });
        }

        return results;
    }

    private async Task<float> CalculateAttentionScoreAsync(
        float[] queryEmbedding,
        string content,
        CancellationToken cancellationToken)
    {
        try
        {
            var contentEmbedding = await _embeddingService!.GenerateEmbeddingAsync(content, cancellationToken);

            // Cosine similarity as attention score
            return CalculateCosineSimilarity(queryEmbedding, contentEmbedding);
        }
        catch (Exception ex)
        {
            LogListwiseReranker3(_logger, ex);
            return 0.5f;
        }
    }

    #endregion

    #region Hybrid Method

    /// <summary>
    /// Hybrid approach combining multiple methods with score fusion.
    /// </summary>
    private async Task<List<ListwiseRerankResult>> RerankHybridAsync(
        string query,
        List<RetrievalCandidate> candidates,
        ListwiseRerankOptions options,
        CancellationToken cancellationToken)
    {
        var scores = new Dictionary<string, List<(string method, float score)>>();
        foreach (var candidate in candidates)
        {
            scores[candidate.Id] = new List<(string, float)>();
        }

        // Get attention scores
        var attentionResults = await RerankAttentionBasedAsync(
            query, candidates,
            new ListwiseRerankOptions { InitialScoreWeight = 0 },
            cancellationToken);

        foreach (var result in attentionResults)
        {
            scores[result.Id].Add(("attention", result.ListwiseScore));
        }

        // Get tournament scores if LLM available
        if (_llmService != null && candidates.Count <= 20)
        {
            var tournamentResults = await RerankTournamentAsync(
                query, candidates,
                new ListwiseRerankOptions
                {
                    MaxPairwiseComparisons = Math.Min(30, candidates.Count * 3),
                    InitialScoreWeight = 0
                },
                cancellationToken);

            foreach (var result in tournamentResults)
            {
                scores[result.Id].Add(("tournament", result.ListwiseScore));
            }
        }

        // Fuse scores using reciprocal rank fusion
        var fusedScores = FuseScores(scores);

        // Create final results
        var results = candidates.Select(candidate =>
        {
            var fusedScore = fusedScores.GetValueOrDefault(candidate.Id, 0.5f);
            var blendedScore = (1 - options.InitialScoreWeight) * fusedScore +
                               options.InitialScoreWeight * candidate.InitialScore;

            var componentScores = scores[candidate.Id];

            return new ListwiseRerankResult
            {
                Id = candidate.Id,
                DocumentId = candidate.DocumentId,
                ChunkId = candidate.ChunkId,
                Content = candidate.Content,
                InitialScore = candidate.InitialScore,
                InitialRank = candidate.InitialRank,
                ListwiseScore = (float)blendedScore,
                Confidence = Math.Min(1.0f, componentScores.Count * 0.3f + 0.4f),
                Components = new ListwiseScoreComponents
                {
                    AttentionScore = componentScores.FirstOrDefault(s => s.method == "attention").score,
                    TournamentScore = componentScores.FirstOrDefault(s => s.method == "tournament").score,
                    InitialScore = candidate.InitialScore,
                    Weights = new Dictionary<string, float>
                    {
                        ["hybrid"] = 1 - options.InitialScoreWeight,
                        ["initial"] = options.InitialScoreWeight
                    }
                },
                Metadata = candidate.Metadata
            };
        }).ToList();

        return results;
    }

    private static Dictionary<string, float> FuseScores(Dictionary<string, List<(string method, float score)>> scores)
    {
        const float k = 60.0f; // RRF constant

        var fusedScores = new Dictionary<string, float>();

        foreach (var (id, methodScores) in scores)
        {
            if (methodScores.Count == 0)
            {
                fusedScores[id] = 0.5f;
                continue;
            }

            // Calculate RRF score across methods
            var rrfScore = 0.0f;
            foreach (var (method, score) in methodScores)
            {
                // Convert score to rank (higher score = lower rank number)
                var rank = 1.0f / (score + 0.01f);
                rrfScore += 1.0f / (k + rank);
            }

            // Normalize
            fusedScores[id] = rrfScore / methodScores.Count;
        }

        // Normalize to 0-1 range
        var maxScore = fusedScores.Values.Max();
        var minScore = fusedScores.Values.Min();
        var range = maxScore - minScore;

        if (range > 0)
        {
            foreach (var id in fusedScores.Keys.ToList())
            {
                fusedScores[id] = (fusedScores[id] - minScore) / range;
            }
        }

        return fusedScores;
    }

    #endregion

    #region Pairwise Preference Helpers

    private async Task<PairwisePreference> ComputeLlmPairwisePreferenceAsync(
        string query,
        string docA,
        string docB,
        CancellationToken cancellationToken)
    {
        var prompt = $"""
            Given the query: "{query}"

            Which document is more relevant?

            Document A:
            {TruncateContent(docA, 300)}

            Document B:
            {TruncateContent(docB, 300)}

            Reply with only "A", "B", or "TIE".
            """;

        try
        {
            var response = await _llmService!.CompleteAsync(
                prompt, new Flux.Abstractions.TextCompletionOptions { MaxTokens = 10, Temperature = 0.0f }, cancellationToken);

            var choice = response.Trim().ToUpperInvariant();

            return new PairwisePreference
            {
                Preference = choice.Contains('A') ? 1 : choice.Contains('B') ? -1 : 0,
                Confidence = 0.9f,
                Reason = $"LLM preference: {choice}"
            };
        }
        catch (Exception ex)
        {
            LogListwiseReranker2(_logger, ex);
            return new PairwisePreference { Preference = 0, Confidence = 0.0f };
        }
    }

    private async Task<PairwisePreference> ComputeEmbeddingPairwisePreferenceAsync(
        string query,
        string docA,
        string docB,
        CancellationToken cancellationToken)
    {
        try
        {
            var queryEmb = await _embeddingService!.GenerateEmbeddingAsync(query, cancellationToken);
            var docAEmb = await _embeddingService.GenerateEmbeddingAsync(docA, cancellationToken);
            var docBEmb = await _embeddingService.GenerateEmbeddingAsync(docB, cancellationToken);

            var simA = CalculateCosineSimilarity(queryEmb, docAEmb);
            var simB = CalculateCosineSimilarity(queryEmb, docBEmb);

            var diff = simA - simB;

            return new PairwisePreference
            {
                Preference = diff > 0.05f ? 1 : diff < -0.05f ? -1 : 0,
                Confidence = Math.Min(1.0f, Math.Abs(diff) * 5),
                Reason = $"Embedding similarity: A={simA:F3}, B={simB:F3}"
            };
        }
        catch (Exception ex)
        {
            LogListwiseReranker1(_logger, ex);
            return new PairwisePreference { Preference = 0, Confidence = 0.0f };
        }
    }

    private static PairwisePreference ComputeLexicalPairwisePreference(string query, string docA, string docB)
    {
        var simA = CalculateLexicalSimilarity(query, docA);
        var simB = CalculateLexicalSimilarity(query, docB);
        var diff = simA - simB;

        return new PairwisePreference
        {
            Preference = diff > 0.1f ? 1 : diff < -0.1f ? -1 : 0,
            Confidence = Math.Min(1.0f, Math.Abs(diff) * 3),
            Reason = $"Lexical similarity: A={simA:F3}, B={simB:F3}"
        };
    }

    #endregion

    #region Utility Methods

    private static float CalculateCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;

        var dot = 0.0;
        var magA = 0.0;
        var magB = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var denom = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denom > 0 ? (float)(dot / denom) : 0;
    }

    private static float CalculateLexicalSimilarity(string query, string content)
    {
        var queryTokens = Tokenize(query.ToLowerInvariant());
        var contentTokens = new HashSet<string>(Tokenize(content.ToLowerInvariant()));

        if (queryTokens.Count == 0 || contentTokens.Count == 0) return 0;

        var matches = queryTokens.Count(t => contentTokens.Contains(t));
        return (float)matches / queryTokens.Count;
    }

    private static List<string> Tokenize(string text)
    {
        return text.Split(TokenizeSeparators,
                          StringSplitOptions.RemoveEmptyEntries)
                   .Where(t => t.Length > 1)
                   .ToList();
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (content.Length <= maxLength) return content;
        return string.Concat(content.AsSpan(0, maxLength), "...");
    }

    private static float StandardDeviation(List<int> values)
    {
        if (values.Count == 0) return 0;
        var avg = values.Average();
        var variance = values.Average(v => Math.Pow(v - avg, 2));
        return (float)Math.Sqrt(variance);
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Warning, Message = "No candidates provided for listwise reranking")]
    private static partial void LogListwiseReranker11(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Listwise reranking {Count} candidates using {Method}")]
    private static partial void LogListwiseReranker10(ILogger logger, int count, ListwiseMethod method);
    [LoggerMessage(Level = LogLevel.Information, Message = "Listwise reranking complete: {Input} → {Output} results")]
    private static partial void LogListwiseReranker9(ILogger logger, int input, int output);
    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM not available for sliding window, falling back to attention-based")]
    private static partial void LogListwiseReranker8(ILogger logger);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to rank window with LLM, using initial order")]
    private static partial void LogListwiseReranker7(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Running {Count} pairwise comparisons")]
    private static partial void LogListwiseReranker6(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM not available for direct ranking, falling back to attention-based")]
    private static partial void LogListwiseReranker5(ILogger logger);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to generate query embedding")]
    private static partial void LogListwiseReranker4(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to calculate attention score")]
    private static partial void LogListwiseReranker3(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed LLM pairwise comparison")]
    private static partial void LogListwiseReranker2(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed embedding pairwise comparison")]
    private static partial void LogListwiseReranker1(ILogger logger, Exception exception);

    #endregion
}
