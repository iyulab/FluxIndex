using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Base;
using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL with pgvector storage implementation for FluxIndex.
/// Uses pgvector's native cosine distance for efficient vector search.
/// </summary>
public class PostgreSQLVectorStore : VectorStoreBase
{
    private readonly FluxIndexDbContext _context;
    private readonly PostgreSQLOptions _options;

    public PostgreSQLVectorStore(
        FluxIndexDbContext context,
        ILogger<PostgreSQLVectorStore> logger,
        IOptions<PostgreSQLOptions> options) : base(logger)
    {
        _context = context;
        _options = options.Value;
    }

    #region VectorStoreBase Core Implementations

    protected override async Task<string> StoreCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString();
        var entity = new VectorEntity
        {
            Id = Guid.Parse(id),
            DocumentId = chunk.DocumentId,
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content,
            Embedding = chunk.Embedding is not null ? new Vector(chunk.Embedding.ToArray()) : new Vector(Array.Empty<float>()),
            TokenCount = chunk.TokenCount,
            Metadata = chunk.Metadata ?? new()
        };

        _context.Vectors.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return id;
    }

    protected override async Task<DocumentChunk?> GetCoreAsync(string id, CancellationToken cancellationToken)
    {
        var entity = await _context.Vectors
            .FirstOrDefaultAsync(v => v.Id == Guid.Parse(id), cancellationToken);

        return entity == null ? null : MapToChunk(entity);
    }

    protected override async Task<IEnumerable<VectorSearchResult>> SearchCoreAsync(
        float[] queryEmbedding,
        int topK,
        Dictionary<string, object>? filters,
        CancellationToken cancellationToken)
    {
        var queryVector = new Vector(queryEmbedding);

        // Push metadata filters down to SQL (jsonb @> containment, GIN-indexable) BEFORE the
        // candidate trim — otherwise higher-scoring non-matching rows crowd matching rows out of
        // the topK*3 window (multi-tenant recall loss).
        var query = _context.Vectors.AsQueryable();
        if (filters is { Count: > 0 })
        {
            query = query.Where(BuildMetadataPredicate(filters));
        }

        // Use pgvector's native cosine distance for efficient search
        var candidates = await query
            .OrderBy(v => v.Embedding.CosineDistance(queryVector))
            .Take(topK * 3) // Get 3x results to filter by similarity
            .Select(v => new
            {
                Distance = v.Embedding.CosineDistance(queryVector),
                Entity = v
            })
            .ToListAsync(cancellationToken);

        // Convert cosine distance (0-2) to cosine similarity (1 to -1)
        return candidates.Select(c => new VectorSearchResult(
            MapToChunk(c.Entity),
            VectorMathUtilities.DistanceToSimilarity((float)c.Distance, DistanceType.Cosine)));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Executes a single SQL DELETE using jsonb containment (<c>Metadata @&gt; filters</c>),
    /// matching the same semantics as the search-path filter pushdown.
    /// </remarks>
    public override async Task<int> DeleteByFilterAsync(
        Dictionary<string, object> filters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.Count == 0)
            throw new ArgumentException(
                "Filter must contain at least one key/value; use ClearAsync to remove all vectors.",
                nameof(filters));

        return await _context.Vectors
            .Where(BuildMetadataPredicate(filters))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Builds the jsonb pushdown predicate for a filter dictionary, following the IVectorStore
    /// filter contract: keys AND-combine; a scalar value is a single containment check; a
    /// collection value OR-combines one containment check per element (MatchAny); unsupported
    /// value types throw (validated via <see cref="VectorStoreBase.ExpandFilterValue"/>).
    /// Raw (un-normalized) values are serialized so containment matches native jsonb types
    /// (numbers stay numbers); each branch is the same GIN-indexable <c>@&gt;</c> as before.
    /// </summary>
    internal static System.Linq.Expressions.Expression<Func<VectorEntity, bool>> BuildMetadataPredicate(
        Dictionary<string, object> filters)
    {
        // Contract validation (throws on unsupported / empty-collection values — fail-loud).
        foreach (var (key, value) in filters)
            ExpandFilterValue(key, value);

        System.Linq.Expressions.Expression<Func<VectorEntity, bool>>? predicate = null;
        var scalars = new Dictionary<string, object?>();

        foreach (var (key, value) in filters)
        {
            var rawAlternatives = EnumerateRawAlternatives(value);
            if (rawAlternatives is null)
            {
                scalars[key] = value;
                continue;
            }

            System.Linq.Expressions.Expression<Func<VectorEntity, bool>>? keyPredicate = null;
            foreach (var raw in rawAlternatives)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(
                    new Dictionary<string, object?> { [key] = raw });
                System.Linq.Expressions.Expression<Func<VectorEntity, bool>> branch =
                    v => EF.Functions.JsonContains(v.Metadata, json);
                keyPredicate = keyPredicate is null ? branch : CombineOr(keyPredicate, branch);
            }

            predicate = predicate is null ? keyPredicate : CombineAnd(predicate, keyPredicate!);
        }

        if (scalars.Count > 0)
        {
            var scalarJson = System.Text.Json.JsonSerializer.Serialize(scalars);
            System.Linq.Expressions.Expression<Func<VectorEntity, bool>> scalarPredicate =
                v => EF.Functions.JsonContains(v.Metadata, scalarJson);
            predicate = predicate is null ? scalarPredicate : CombineAnd(predicate, scalarPredicate);
        }

        return predicate!;
    }

    /// <summary>
    /// Returns the raw elements of a collection-typed filter value, or null when the value is a
    /// scalar. Values are NOT normalized to strings here — jsonb containment must compare against
    /// the natively-typed JSON stored in the metadata column.
    /// </summary>
    private static List<object?>? EnumerateRawAlternatives(object? value)
    {
        switch (value)
        {
            case string or bool or null:
                return null;
            case System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } je:
                var fromJson = new List<object?>();
                foreach (var item in je.EnumerateArray())
                    fromJson.Add(item);
                return fromJson;
            case System.Text.Json.JsonElement:
                return null;
            case System.Collections.IEnumerable enumerable:
                var raw = new List<object?>();
                foreach (var item in enumerable)
                    raw.Add(item);
                return raw;
            default:
                return null;
        }
    }

    private static System.Linq.Expressions.Expression<Func<VectorEntity, bool>> CombineAnd(
        System.Linq.Expressions.Expression<Func<VectorEntity, bool>> left,
        System.Linq.Expressions.Expression<Func<VectorEntity, bool>> right)
        => Combine(left, right, System.Linq.Expressions.Expression.AndAlso);

    private static System.Linq.Expressions.Expression<Func<VectorEntity, bool>> CombineOr(
        System.Linq.Expressions.Expression<Func<VectorEntity, bool>> left,
        System.Linq.Expressions.Expression<Func<VectorEntity, bool>> right)
        => Combine(left, right, System.Linq.Expressions.Expression.OrElse);

    private static System.Linq.Expressions.Expression<Func<VectorEntity, bool>> Combine(
        System.Linq.Expressions.Expression<Func<VectorEntity, bool>> left,
        System.Linq.Expressions.Expression<Func<VectorEntity, bool>> right,
        Func<System.Linq.Expressions.Expression, System.Linq.Expressions.Expression, System.Linq.Expressions.BinaryExpression> merge)
    {
        var parameter = left.Parameters[0];
        var rewrittenRight = new ParameterReplaceVisitor(right.Parameters[0], parameter).Visit(right.Body);
        return System.Linq.Expressions.Expression.Lambda<Func<VectorEntity, bool>>(
            merge(left.Body, rewrittenRight), parameter);
    }

    private sealed class ParameterReplaceVisitor(
        System.Linq.Expressions.ParameterExpression from,
        System.Linq.Expressions.ParameterExpression to) : System.Linq.Expressions.ExpressionVisitor
    {
        protected override System.Linq.Expressions.Expression VisitParameter(
            System.Linq.Expressions.ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }

    protected override async Task<bool> DeleteCoreAsync(string id, CancellationToken cancellationToken)
    {
        var entity = await _context.Vectors
            .FirstOrDefaultAsync(v => v.Id == Guid.Parse(id), cancellationToken);

        if (entity == null) return false;

        _context.Vectors.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    protected override async Task<bool> UpdateCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken)
    {
        var entity = await _context.Vectors
            .FirstOrDefaultAsync(v => v.Id == Guid.Parse(chunk.Id), cancellationToken);

        if (entity == null) return false;

        entity.Content = chunk.Content;
        entity.Embedding = chunk.Embedding is not null ? new Vector(chunk.Embedding.ToArray()) : new Vector(Array.Empty<float>());
        entity.TokenCount = chunk.TokenCount;
        entity.Metadata = chunk.Metadata ?? new();

        // Explicitly mark Metadata as modified for EF Core change tracking
        _context.Entry(entity).Property(e => e.Metadata).IsModified = true;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    protected override async Task<IEnumerable<DocumentChunk>> GetByDocumentIdCoreAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        var entities = await _context.Vectors
            .Where(v => v.DocumentId == documentId)
            .OrderBy(v => v.ChunkIndex)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToChunk);
    }

    protected override async Task<bool> DeleteByDocumentIdCoreAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        var entities = await _context.Vectors
            .Where(v => v.DocumentId == documentId)
            .ToListAsync(cancellationToken);

        if (entities.Count == 0) return false;

        _context.Vectors.RemoveRange(entities);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    protected override async Task<int> CountCoreAsync(CancellationToken cancellationToken)
    {
        return await _context.Vectors.CountAsync(cancellationToken);
    }

    public override async Task<int> GetDistinctDocumentCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Vectors
            .Select(v => v.DocumentId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    protected override async Task ClearCoreAsync(CancellationToken cancellationToken)
    {
        await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE vectors", cancellationToken);
    }

    #endregion

    #region Overrides for Batch Optimization

    public override async Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        var guids = ids.Select(Guid.Parse).ToList();
        var entities = await _context.Vectors
            .Where(v => guids.Contains(v.Id))
            .ToListAsync(cancellationToken);

        return entities.Select(MapToChunk);
    }

    public override async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        return await _context.Vectors.AnyAsync(v => v.Id == Guid.Parse(id), cancellationToken);
    }

    #endregion

    #region Private Helper Methods

    private DocumentChunk MapToChunk(VectorEntity entity)
    {
        var chunk = new DocumentChunk
        {
            Id = entity.Id.ToString(),
            DocumentId = entity.DocumentId,
            ChunkIndex = entity.ChunkIndex,
            Content = entity.Content,
            Embedding = entity.Embedding.ToArray(),
            TokenCount = entity.TokenCount,
            Metadata = entity.Metadata
        };

        // Include standard fields in metadata for consumer apps (RAG source citation)
        chunk.Metadata = MetadataHelper.EnsureInitialized(chunk.Metadata);
        chunk.Metadata["chunkIndex"] = chunk.ChunkIndex;
        chunk.Metadata["totalChunks"] = chunk.TotalChunks;
        chunk.Metadata["tokenCount"] = chunk.TokenCount;

        RestoreRichMetadata(chunk);
        return chunk;
    }

    #endregion
}

/// <summary>
/// Vector entity for PostgreSQL storage
/// </summary>
public class VectorEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DocumentId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public Vector Embedding { get; set; } = new Vector(Array.Empty<float>());
    public int TokenCount { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
