using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Services.Graph;

/// <summary>
/// Turns what the graph store holds back into the in-memory graph shapes a build produces, so that
/// an index loaded from the store (<c>GraphRAGService.LoadIndexAsync</c>) and a build that reuses
/// stored extractions (<c>EntityGraphService.BuildEntityGraphAsync</c>) agree on the conversion.
/// </summary>
internal static class StoredGraphConversions
{
    public static EntityNode ToEntityNode(GraphEntity stored) => new()
    {
        Id = stored.Id,
        Name = stored.Name,
        // Query-entity matching compares normalized names; an empty one would match every query.
        NormalizedName = string.IsNullOrEmpty(stored.NormalizedName) ? stored.Name.ToLowerInvariant().Trim() : stored.NormalizedName,
        Type = stored.Type,
        SurfaceForms = stored.SurfaceForms.Count > 0 ? stored.SurfaceForms : [stored.Name],
        Confidence = stored.Confidence,
        ImportanceScore = stored.ImportanceScore,
        MentionCount = stored.MentionCount,
        Embedding = stored.Embedding,
        ExternalLinks = stored.ExternalLinks,
        Properties = ToPlainProperties(stored.Properties)
    };

    /// <summary>
    /// Property values as the build wrote them, whatever the store handed back. Every store keeps
    /// <see cref="GraphEntity.Properties"/> as JSON and deserializes it to
    /// <c>Dictionary&lt;string, object&gt;</c>, so a string written as <c>"desk"</c> comes back as a
    /// <see cref="JsonElement"/> of kind String - equal to nothing the build compares it with. Entity
    /// identity reads <c>"subtype"</c> from here; left as a <see cref="JsonElement"/>, every stored
    /// node would carry no subtype at all and a re-index would write a second node beside each one.
    /// Primitives are unwrapped; objects and arrays are kept as the element they are.
    /// </summary>
    internal static IReadOnlyDictionary<string, object> ToPlainProperties(IReadOnlyDictionary<string, object> stored)
    {
        if (stored.Count == 0 || !stored.Values.Any(v => v is JsonElement))
        {
            return stored;
        }

        var plain = new Dictionary<string, object>(stored.Count);
        foreach (var (key, value) in stored)
        {
            if (value is not JsonElement element)
            {
                plain[key] = value;
                continue;
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    plain[key] = element.GetString()!;
                    break;
                case JsonValueKind.Number:
                    plain[key] = element.TryGetInt64(out var integer) ? integer : element.GetDouble();
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    plain[key] = element.GetBoolean();
                    break;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    break;
                default:
                    plain[key] = element;
                    break;
            }
        }
        return plain;
    }

    /// <summary>
    /// One mapping per stored chunk id that is in scope. Per-chunk mention counts and positions are
    /// not persisted; a reconstituted mapping carries the entity's confidence as its relevance so
    /// that chunk ranking stays non-zero and comparable.
    /// </summary>
    public static IEnumerable<EntityChunkMapping> ToChunkMappings(
        GraphEntity stored,
        IReadOnlyDictionary<string, DocumentChunk> chunkLookup)
    {
        foreach (var chunkId in stored.ChunkIds.Where(chunkLookup.ContainsKey).Distinct())
        {
            yield return new EntityChunkMapping
            {
                EntityId = stored.Id,
                ChunkId = chunkId,
                DocumentId = chunkLookup[chunkId].DocumentId,
                MentionCount = 1,
                RelevanceScore = stored.Confidence
            };
        }
    }

    public static EntityEdge ToEntityEdge(GraphRelationship stored) => new()
    {
        Id = stored.Id,
        SourceEntityId = stored.SourceEntityId,
        TargetEntityId = stored.TargetEntityId,
        RelationType = stored.Type,
        Label = stored.Label,
        Confidence = stored.Confidence,
        Weight = stored.Weight,
        IsDirectional = stored.IsDirectional,
        EvidenceChunkIds = stored.EvidenceChunkIds,
        EvidenceTexts = stored.EvidenceTexts,
        Properties = stored.Properties
    };
}
