using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Utilities;

/// <summary>
/// Helper utilities for chunk metadata management.
/// Ensures consistent metadata handling across all VectorStore implementations.
/// </summary>
public static class MetadataHelper
{
    /// <summary>
    /// Standard metadata keys used by FluxIndex for RAG source citation.
    /// </summary>
    public static class StandardKeys
    {
        public const string DocumentId = "documentId";
        public const string FileName = "fileName";
        public const string FilePath = "filePath";
        public const string Title = "title";
        public const string ChunkIndex = "chunkIndex";
        public const string PageNumber = "pageNumber";
        public const string StoredAt = "storedAt";
        public const string UpdatedAt = "updatedAt";
    }

    /// <summary>
    /// Ensures metadata dictionary is initialized (never null).
    /// Returns the same dictionary if already initialized, or creates a new one.
    /// </summary>
    public static Dictionary<string, object> EnsureInitialized(Dictionary<string, object>? metadata)
    {
        return metadata ?? new Dictionary<string, object>();
    }

    /// <summary>
    /// Adds standard RAG fields to metadata if not already present.
    /// This ensures consistent metadata across all storage providers.
    /// </summary>
    public static void AddStandardFields(Dictionary<string, object> metadata, DocumentChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(chunk);

        // Always set documentId and chunkIndex
        metadata.TryAdd(StandardKeys.DocumentId, chunk.DocumentId);
        metadata.TryAdd(StandardKeys.ChunkIndex, chunk.ChunkIndex);

        // Set storedAt timestamp if not present
        metadata.TryAdd(StandardKeys.StoredAt, DateTime.UtcNow.ToString("O"));
    }

    /// <summary>
    /// Adds or updates the "updatedAt" timestamp in metadata.
    /// </summary>
    public static void SetUpdatedTimestamp(Dictionary<string, object> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        metadata[StandardKeys.UpdatedAt] = DateTime.UtcNow.ToString("O");
    }

    /// <summary>
    /// Merges source metadata into target, preserving existing values in target.
    /// </summary>
    public static void MergeMetadata(Dictionary<string, object> target, Dictionary<string, object>? source)
    {
        if (source == null)
            return;

        foreach (var kvp in source)
        {
            target.TryAdd(kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// Tries to get a typed value from metadata.
    /// Returns default if key not found or type mismatch.
    /// </summary>
    public static T? GetValue<T>(Dictionary<string, object>? metadata, string key, T? defaultValue = default)
    {
        if (metadata == null || !metadata.TryGetValue(key, out var value))
            return defaultValue;

        if (value is T typedValue)
            return typedValue;

        // Try conversion for common types
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Gets DocumentId from metadata, falling back to chunk.DocumentId.
    /// </summary>
    public static string GetDocumentId(Dictionary<string, object>? metadata, string fallback)
    {
        return GetValue<string>(metadata, StandardKeys.DocumentId, fallback) ?? fallback;
    }

    /// <summary>
    /// Creates a copy of metadata dictionary for safe modification.
    /// </summary>
    public static Dictionary<string, object> CloneMetadata(Dictionary<string, object>? metadata)
    {
        if (metadata == null)
            return new Dictionary<string, object>();

        return new Dictionary<string, object>(metadata);
    }

    /// <summary>
    /// Filters metadata to only include specified keys.
    /// Useful for projecting minimal metadata in search results.
    /// </summary>
    public static Dictionary<string, object> FilterKeys(Dictionary<string, object>? metadata, IEnumerable<string> keys)
    {
        if (metadata == null)
            return new Dictionary<string, object>();

        var keySet = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return metadata
            .Where(kvp => keySet.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Removes keys with null or empty values from metadata.
    /// </summary>
    public static void RemoveEmptyValues(Dictionary<string, object> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var keysToRemove = metadata
            .Where(kvp => kvp.Value == null || (kvp.Value is string s && string.IsNullOrEmpty(s)))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            metadata.Remove(key);
        }
    }
}
