using System.Text.Json;
using System.Text.Json.Serialization;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;

namespace FluxIndex.Core.Application.Utilities;

/// <summary>
/// The stored form of a semantic cache entry's results and metadata, shared by the caches that keep them in a text or
/// JSON column (SQLite, PostgreSQL). Reading is strict: text that is not this form — a row written by an earlier
/// release, or by something else — is reported as unreadable, so the cache can treat the entry as a miss instead of
/// serving results it cannot vouch for. Metadata values read back as plain .NET values (<see cref="MetadataValues"/>),
/// the same values a search that missed the cache returns.
/// </summary>
public static class SemanticCacheJson
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>The stored form of <paramref name="results"/>.</summary>
    public static string SerializeResults(IReadOnlyList<CacheDocumentChunk> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return JsonSerializer.Serialize(results, s_options);
    }

    /// <summary>
    /// Reads results written by <see cref="SerializeResults"/>. False when <paramref name="json"/> is missing, is not a
    /// JSON array of chunks, holds a member a chunk does not have, or holds a chunk without an id.
    /// </summary>
    public static bool TryDeserializeResults(string? json, out IReadOnlyList<CacheDocumentChunk> results)
    {
        results = [];
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            var chunks = JsonSerializer.Deserialize<List<CacheDocumentChunk>>(json, s_options);
            // A chunk without an id was not written by SerializeResults (e.g. a legacy "[{}]").
            if (chunks is null || chunks.Any(chunk => chunk is null || string.IsNullOrEmpty(chunk.Id)))
                return false;

            foreach (var chunk in chunks)
                MetadataValues.ToPlain(chunk.Metadata);

            results = chunks;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The stored form of <paramref name="metadata"/>; null for none.</summary>
    public static string? SerializeMetadata(SearchMetadata? metadata)
        => metadata is null ? null : JsonSerializer.Serialize(metadata, s_options);

    /// <summary>
    /// Reads metadata written by <see cref="SerializeMetadata"/>; null when there is none or it is not that form (the
    /// entry's results stay usable — metadata only describes them).
    /// </summary>
    public static SearchMetadata? DeserializeMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var metadata = JsonSerializer.Deserialize<SearchMetadata>(json, s_options);
            if (metadata is not null)
                MetadataValues.ToPlain(metadata.AdditionalProperties);
            return metadata;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
