using System.Text;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Base;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services.Graph;

/// <summary>
/// <see cref="IGraphChunkRetriever"/> over an <see cref="IGraphStore"/> and the <see cref="IVectorStore"/> that holds the
/// chunks the graph points at.
/// </summary>
public sealed partial class GraphChunkRetriever : IGraphChunkRetriever
{
    /// <summary>Words in the longest name a run of query words is matched against.</summary>
    internal const int MaxSpanWords = 4;

    /// <summary>Longest word whose substrings are looked up; a longer one contributes itself only.</summary>
    internal const int MaxSubstringWordLength = 32;

    /// <summary>Candidates fetched from the chunk store per result asked for — room for the scope to drop some.</summary>
    internal const int CandidatesPerResult = 4;

    private readonly IGraphStore _graphStore;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<GraphChunkRetriever> _logger;

    /// <summary>Creates the retriever.</summary>
    public GraphChunkRetriever(IGraphStore graphStore, IVectorStore vectorStore, ILogger<GraphChunkRetriever> logger)
    {
        _graphStore = graphStore ?? throw new ArgumentNullException(nameof(graphStore));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphChunkHit>> RetrieveAsync(
        string query,
        GraphRetrievalOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        // Compiled before any store call: a malformed scope is the caller's error and must not depend on whether a seed matched.
        var scope = MetadataFilterMatcher.Compile(options.Filters);

        if (string.IsNullOrWhiteSpace(query))
            return [];

        var candidates = NameCandidates(query);
        if (candidates.Count == 0)
            return [];

        var matched = await _graphStore.GetEntitiesByNormalizedNamesAsync(candidates, options.Partition, cancellationToken);
        var seeds = SelectSeeds(query, matched, options.MaxSeedEntities);
        if (seeds.Count == 0)
        {
            LogNoSeed(_logger, candidates.Count);
            return [];
        }

        var reached = await WalkAsync(seeds, options, cancellationToken);
        var ranked = RankChunks(reached, scope.DocumentIds, options.MaxResults * CandidatesPerResult);
        if (ranked.Count == 0)
            return [];

        var fetched = await _vectorStore.GetChunksByIdsAsync(ranked.Select(r => r.ChunkId), cancellationToken);
        var byId = new Dictionary<string, Domain.Entities.DocumentChunk>(StringComparer.Ordinal);
        foreach (var chunk in fetched)
        {
            if (scope.IsMatchAll || scope.Matches(chunk.DocumentId, chunk.Metadata))
                byId.TryAdd(chunk.Id, chunk);
        }

        var hits = new List<GraphChunkHit>(Math.Min(options.MaxResults, byId.Count));
        foreach (var candidate in ranked)
        {
            if (!byId.TryGetValue(candidate.ChunkId, out var chunk))
                continue;
            hits.Add(new GraphChunkHit(chunk, candidate.Score, hits.Count + 1, candidate.SeedIds, candidate.Hops));
            if (hits.Count == options.MaxResults)
                break;
        }

        LogRetrieved(_logger, seeds.Count, reached.Count, ranked.Count, hits.Count);
        return hits;
    }

    private static void Validate(GraphRetrievalOptions options)
    {
        if (options.MaxSeedEntities < 1)
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxSeedEntities, "GraphRetrievalOptions.MaxSeedEntities must be at least 1.");
        if (options.MaxHops < 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxHops, "GraphRetrievalOptions.MaxHops must not be negative.");
        if (options.MaxNodesPerSeed < 1)
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxNodesPerSeed, "GraphRetrievalOptions.MaxNodesPerSeed must be at least 1.");
        if (options.HopDecay is <= 0 or > 1 || double.IsNaN(options.HopDecay))
            throw new ArgumentOutOfRangeException(nameof(options), options.HopDecay, "GraphRetrievalOptions.HopDecay must be greater than 0 and at most 1.");
        if (options.MaxResults < 1)
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxResults, "GraphRetrievalOptions.MaxResults must be at least 1.");
    }

    /// <summary>
    /// The names a query could be naming: runs of one to <see cref="MaxSpanWords"/> words, and every substring (length 2 or
    /// more) of a word with CJK or Hangul in it. Each in the query's own casing and lower-cased, since a stored
    /// <see cref="GraphEntity.NormalizedName"/> is lower-cased by the default extractors but kept as written by others.
    /// </summary>
    internal static IReadOnlyCollection<string> NameCandidates(string query)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(TrimWord)
            .Where(w => w.Length > 0)
            .ToList();

        var names = new HashSet<string>(StringComparer.Ordinal);
        void Add(string name)
        {
            if (name.Length < 2)
                return;
            names.Add(name);
            names.Add(name.ToLowerInvariant());
        }

        for (var start = 0; start < words.Count; start++)
        {
            var span = new StringBuilder();
            for (var length = 1; length <= MaxSpanWords && start + length <= words.Count; length++)
            {
                if (length > 1)
                    span.Append(' ');
                var prefix = span.ToString();
                var last = words[start + length - 1];
                span.Append(last);
                Add(span.ToString());
                // «Apple Inc's board» names «Apple Inc»: the possessive belongs to the run's last word.
                Add(prefix + WithoutPossessive(last));
            }

            var word = words[start];
            if (word.Length <= MaxSubstringWordLength && CjkBigramTextAnalyzer.ContainsCjk(word))
            {
                var runes = word.EnumerateRunes().Select(r => r.ToString()).ToArray();
                for (var from = 0; from < runes.Length; from++)
                {
                    for (var to = from + 2; to <= runes.Length; to++)
                        Add(string.Concat(runes[from..to]));
                }
            }
        }

        return names;
    }

    /// <summary>Strips the punctuation around a word («(Apple,» → «Apple»), keeping what is inside it («AT&amp;T», «U.S»).</summary>
    private static string TrimWord(string word)
    {
        var start = 0;
        var end = word.Length;
        while (start < end && !char.IsLetterOrDigit(word[start]))
            start++;
        while (end > start && !char.IsLetterOrDigit(word[end - 1]))
            end--;
        return word[start..end];
    }

    private static string WithoutPossessive(string word)
    {
        foreach (var suffix in PossessiveSuffixes)
        {
            if (word.Length > suffix.Length && word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return word[..^suffix.Length];
        }

        return word;
    }

    private static readonly string[] PossessiveSuffixes = ["'s", "’s"];

    /// <summary>
    /// Keeps the matches the query actually names: a match whose every occurrence in the query lies inside an occurrence of
    /// a longer match is dropped. Then longer names first, then the most mentioned, capped.
    /// </summary>
    internal static IReadOnlyList<GraphEntity> SelectSeeds(string query, IReadOnlyList<GraphEntity> matched, int max)
    {
        if (matched.Count == 0)
            return [];

        var lowerQuery = query.ToLowerInvariant();
        var occurrences = matched
            .GroupBy(e => e.NormalizedName.ToLowerInvariant(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => Occurrences(lowerQuery, g.Key), StringComparer.Ordinal);

        bool Covered(string name) =>
            occurrences[name].All(at => occurrences.Any(other =>
                other.Key.Length > name.Length &&
                other.Value.Any(o => o <= at && at + name.Length <= o + other.Key.Length)));

        return matched
            .Where(e =>
            {
                var name = e.NormalizedName.ToLowerInvariant();
                return occurrences[name].Count > 0 && !Covered(name);
            })
            .OrderByDescending(e => e.NormalizedName.Length)
            .ThenByDescending(e => e.MentionCount)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .Take(max)
            .ToList();
    }

    private static List<int> Occurrences(string text, string name)
    {
        var found = new List<int>();
        if (name.Length == 0)
            return found;
        for (var at = text.IndexOf(name, StringComparison.Ordinal); at >= 0; at = text.IndexOf(name, at + 1, StringComparison.Ordinal))
            found.Add(at);
        return found;
    }

    private sealed class Reached(GraphEntity entity, double weight, int hops)
    {
        public GraphEntity Entity { get; } = entity;
        public double Weight { get; set; } = weight;
        public int Hops { get; set; } = hops;
        public HashSet<string> SeedIds { get; } = new(StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, Reached>> WalkAsync(
        IReadOnlyList<GraphEntity> seeds,
        GraphRetrievalOptions options,
        CancellationToken cancellationToken)
    {
        var reached = new Dictionary<string, Reached>(StringComparer.Ordinal);
        void Visit(GraphEntity entity, int hops, string seedId)
        {
            var weight = Math.Pow(options.HopDecay, hops);
            if (!reached.TryGetValue(entity.Id, out var entry))
                reached[entity.Id] = entry = new Reached(entity, weight, hops);
            else if (hops < entry.Hops)
            {
                entry.Weight = weight;
                entry.Hops = hops;
            }
            entry.SeedIds.Add(seedId);
        }

        foreach (var seed in seeds)
            Visit(seed, 0, seed.Id);

        if (options.MaxHops == 0)
            return reached;

        var traversal = new GraphStoreTraversalOptions
        {
            MaxDepth = options.MaxHops,
            MaxNodes = options.MaxNodesPerSeed,
            Direction = TraversalDirection.Both,
        };

        foreach (var seed in seeds)
        {
            var walk = await _graphStore.TraverseAsync(seed.Id, traversal, cancellationToken);
            if (walk.WasTruncated)
                LogTraversalTruncated(_logger, seed.Id, options.MaxNodesPerSeed);

            foreach (var entity in walk.Entities)
            {
                if (string.Equals(entity.Id, seed.Id, StringComparison.Ordinal))
                    continue;
                var hops = walk.Paths.TryGetValue(entity.Id, out var path) && path.Length > 0 ? path.Length : 1;
                if (hops > options.MaxHops)
                    continue;
                Visit(entity, hops, seed.Id);
            }
        }

        return reached;
    }

    private sealed record RankedChunk(string ChunkId, double Score, IReadOnlyList<string> SeedIds, int Hops);

    private static List<RankedChunk> RankChunks(
        Dictionary<string, Reached> reached,
        IReadOnlyCollection<string>? documentScope,
        int limit)
    {
        var scores = new Dictionary<string, (double Score, HashSet<string> Seeds, int Hops)>(StringComparer.Ordinal);
        foreach (var entry in reached.Values)
        {
            // A document scope rules out an entity none of whose documents is in it; its chunks are never fetched. An entity
            // without recorded documents cannot be ruled out here — the chunk store's metadata decides.
            if (documentScope is not null && entry.Entity.DocumentIds.Count > 0 &&
                !entry.Entity.DocumentIds.Any(documentScope.Contains))
                continue;

            foreach (var chunkId in entry.Entity.ChunkIds)
            {
                if (!scores.TryGetValue(chunkId, out var score))
                    score = (0, new HashSet<string>(StringComparer.Ordinal), int.MaxValue);
                score.Seeds.UnionWith(entry.SeedIds);
                scores[chunkId] = (score.Score + entry.Weight, score.Seeds, Math.Min(score.Hops, entry.Hops));
            }
        }

        return scores
            .OrderByDescending(kv => kv.Value.Score)
            .ThenBy(kv => kv.Value.Hops)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(limit)
            .Select(kv => new RankedChunk(kv.Key, kv.Value.Score, kv.Value.Seeds.Order(StringComparer.Ordinal).ToList(), kv.Value.Hops))
            .ToList();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Graph retrieval: no stored entity matched {CandidateCount} name candidates")]
    private static partial void LogNoSeed(ILogger logger, int candidateCount);

    // Debug, not Warning: on a dense graph (sentence co-occurrence) a hub entity reaches the bound on most queries — measured on
    // MultiHop-RAG, nearly every hops-1 query logged it — and the bound is the configured behaviour, not a fault.
    [LoggerMessage(Level = LogLevel.Debug, Message = "Graph retrieval: traversal from seed {SeedId} stopped at {MaxNodes} entities (GraphRetrievalOptions.MaxNodesPerSeed)")]
    private static partial void LogTraversalTruncated(ILogger logger, string seedId, int maxNodes);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Graph retrieval: {SeedCount} seeds, {EntityCount} entities reached, {CandidateCount} chunk candidates, {HitCount} hits in scope")]
    private static partial void LogRetrieved(ILogger logger, int seedCount, int entityCount, int candidateCount, int hitCount);
}
