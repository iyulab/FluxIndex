using System.Collections;
using System.Reflection;
using FluxIndex.Domain.Entities;
using FluxIndex.Extensions.FileFlux.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Extensions.FileFlux.Adapters;

/// <summary>
/// Adapter that processes FileFlux output dynamically without direct dependency
/// </summary>
public class DynamicChunkAdapter : IFileFluxAdapter
{
    private readonly ILogger<DynamicChunkAdapter> _logger;
    private readonly ChunkTypeDetector _typeDetector;
    private readonly MetadataExtractor _metadataExtractor;

    public DynamicChunkAdapter(ILogger<DynamicChunkAdapter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _typeDetector = new ChunkTypeDetector();
        _metadataExtractor = new MetadataExtractor();
    }

    public async Task<IEnumerable<Document>> AdaptChunksAsync(
        dynamic fileFluxChunks,
        CancellationToken cancellationToken = default)
    {
        var documents = new List<Document>();

        try
        {
            // Handle different input types
            if (fileFluxChunks == null)
            {
                _logger.LogWarning("Received null FileFlux chunks");
                return documents;
            }

            // Check if it's enumerable
            if (fileFluxChunks is IEnumerable enumerable)
            {
                foreach (var chunk in enumerable)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    try
                    {
                        var document = await ConvertToDocumentAsync(chunk, cancellationToken);
                        if (document != null)
                        {
                            documents.Add(document);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to convert chunk to document");
                    }
                }
            }
            else
            {
                // Single chunk
                var document = await ConvertToDocumentAsync(fileFluxChunks, cancellationToken);
                if (document != null)
                {
                    documents.Add(document);
                }
            }

            _logger.LogInformation("Adapted {Count} chunks to documents", documents.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adapting FileFlux chunks");
            throw;
        }

        return documents;
    }

    public ChunkingMetadata ExtractMetadata(dynamic chunk)
    {
        return _metadataExtractor.Extract(chunk);
    }

    public IndexingStrategy DetermineStrategy(ChunkingMetadata metadata)
    {
        // Determine strategy based on metadata characteristics
        if (metadata.QualityScore.HasValue && metadata.QualityScore > 0.8)
        {
            return IndexingStrategy.HighQuality;
        }

        if (!string.IsNullOrEmpty(metadata.ChunkingStrategy))
        {
            return metadata.ChunkingStrategy.ToLowerInvariant() switch
            {
                "intelligent" => IndexingStrategy.Semantic,
                "smart" => IndexingStrategy.Hybrid,
                "semantic" => IndexingStrategy.Semantic,
                "fixedsize" => IndexingStrategy.Keyword,
                "paragraph" => IndexingStrategy.Standard,
                _ => IndexingStrategy.Standard
            };
        }

        return IndexingStrategy.Standard;
    }

    private async Task<Document?> ConvertToDocumentAsync(
        dynamic chunk,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Async for future extensions

        try
        {
            // Extract content
            string content = ExtractContent(chunk);
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogDebug("Skipping chunk with empty content");
                return null;
            }

            // Extract ID
            string id = ExtractId(chunk) ?? Guid.NewGuid().ToString();

            // Extract and enhance metadata
            var metadata = ExtractMetadata(chunk);
            var metadataDict = ConvertMetadataToDict(metadata);

            // Add indexing strategy as metadata
            var strategy = DetermineStrategy(metadata);
            metadataDict["indexing_strategy"] = strategy.ToString();
            metadataDict["source"] = "FileFlux";

            // Add quality metrics if available
            if (metadata.QualityScore.HasValue)
            {
                metadataDict["quality_score"] = metadata.QualityScore.Value.ToString("F2");
            }

            // Create document (Id is auto-generated and read-only)
            var documentMetadata = new DocumentMetadata(
                brand: metadataDict.GetValueOrDefault("brand", ""),
                model: metadataDict.GetValueOrDefault("model", ""),
                category: metadataDict.GetValueOrDefault("category", "text"),
                language: metadataDict.GetValueOrDefault("language", "ko"),
                version: metadataDict.GetValueOrDefault("version", ""),
                publishedDate: DateTime.UtcNow);

            // Add custom fields to metadata
            foreach (var kvp in metadataDict)
            {
                documentMetadata.Properties[kvp.Key] = kvp.Value;
            }

            var document = Document.Create(id);
            document.SetContent(content);
            document.UpdateMetadata(documentMetadata);


            return document;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting chunk to document");
            return null;
        }
    }

    private string ExtractContent(dynamic chunk)
    {
        // Try different property names
        var contentProperties = new[] { "Content", "Text", "Data", "Value", "Body" };
        
        foreach (var prop in contentProperties)
        {
            var value = GetPropertyValue(chunk, prop);
            if (value != null)
            {
                return value.ToString() ?? "";
            }
        }

        // Fallback to ToString
        return chunk?.ToString() ?? "";
    }

    private string? ExtractId(dynamic chunk)
    {
        var idProperties = new[] { "Id", "ID", "Guid", "Key", "Identifier" };
        
        foreach (var prop in idProperties)
        {
            var value = GetPropertyValue(chunk, prop);
            if (value != null)
            {
                return value.ToString();
            }
        }

        return null;
    }

    private object? GetPropertyValue(dynamic obj, string propertyName)
    {
        if (obj == null) return null;

        try
        {
            Type type = obj.GetType();
            
            // Try property
            PropertyInfo? property = type.GetProperty(propertyName, 
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null)
            {
                return property.GetValue(obj);
            }

            // Try field
            FieldInfo? field = type.GetField(propertyName, 
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null)
            {
                return field.GetValue(obj);
            }

            // Try indexer (for dictionary-like objects)
            if (obj is IDictionary dict && dict.Contains(propertyName))
            {
                return dict[propertyName];
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Error getting property {PropertyName}", propertyName);
        }

        return null;
    }

    private Dictionary<string, string> ConvertMetadataToDict(ChunkingMetadata metadata)
    {
        var dict = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(metadata.ChunkingStrategy))
            dict["chunking_strategy"] = metadata.ChunkingStrategy;
        
        dict["chunk_index"] = metadata.ChunkIndex.ToString();
        dict["chunk_size"] = metadata.ChunkSize.ToString();
        
        if (metadata.OverlapSize.HasValue)
            dict["overlap_size"] = metadata.OverlapSize.Value.ToString();
        
        if (metadata.QualityScore.HasValue)
            dict["quality_score"] = metadata.QualityScore.Value.ToString("F2");
        
        if (metadata.BoundaryQuality.HasValue)
            dict["boundary_quality"] = metadata.BoundaryQuality.Value.ToString("F2");
        
        if (metadata.Completeness.HasValue)
            dict["completeness"] = metadata.Completeness.Value.ToString("F2");
        
        if (!string.IsNullOrEmpty(metadata.SourceFile))
            dict["source_file"] = metadata.SourceFile;
        
        if (!string.IsNullOrEmpty(metadata.FileType))
            dict["file_type"] = metadata.FileType;

        // Add custom properties
        foreach (var prop in metadata.Properties)
        {
            dict[$"custom_{prop.Key}"] = prop.Value?.ToString() ?? "";
        }

        return dict;
    }
}