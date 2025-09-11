using System.Reflection;
using System.Collections;
using FluxIndex.Extensions.FileFlux.Interfaces;

namespace FluxIndex.Extensions.FileFlux.Adapters;

/// <summary>
/// Extracts metadata from FileFlux chunks
/// </summary>
public class MetadataExtractor
{
    private readonly Dictionary<string, string[]> _propertyMappings;

    public MetadataExtractor()
    {
        // Define property name mappings for different FileFlux versions/formats
        _propertyMappings = new Dictionary<string, string[]>
        {
            ["ChunkingStrategy"] = new[] { "Strategy", "ChunkingStrategy", "ChunkStrategy", "StrategyName" },
            ["ChunkIndex"] = new[] { "ChunkIndex", "Index", "Position", "ChunkNumber" },
            ["ChunkSize"] = new[] { "ChunkSize", "Size", "Length", "ContentLength" },
            ["OverlapSize"] = new[] { "OverlapSize", "Overlap", "OverlapWithNext", "OverlapLength" },
            ["QualityScore"] = new[] { "QualityScore", "Quality", "Score", "OverallScore" },
            ["BoundaryQuality"] = new[] { "BoundaryQuality", "BoundaryScore", "EdgeQuality" },
            ["Completeness"] = new[] { "Completeness", "CompletenessScore", "ContentCompleteness" },
            ["SourceFile"] = new[] { "SourceFile", "FileName", "Source", "FilePath" },
            ["FileType"] = new[] { "FileType", "FileExtension", "DocumentType", "ContentType" }
        };
    }

    /// <summary>
    /// Extract metadata from a dynamic chunk
    /// </summary>
    public ChunkingMetadata Extract(dynamic chunk)
    {
        var metadata = new ChunkingMetadata();

        if (chunk == null) return metadata;

        try
        {
            // Extract standard properties
            metadata.ChunkingStrategy = ExtractStringProperty(chunk, "ChunkingStrategy");
            metadata.ChunkIndex = ExtractIntProperty(chunk, "ChunkIndex");
            metadata.ChunkSize = ExtractChunkSize(chunk);
            metadata.OverlapSize = ExtractNullableIntProperty(chunk, "OverlapSize");
            metadata.QualityScore = ExtractNullableDoubleProperty(chunk, "QualityScore");
            metadata.BoundaryQuality = ExtractNullableDoubleProperty(chunk, "BoundaryQuality");
            metadata.Completeness = ExtractNullableDoubleProperty(chunk, "Completeness");
            metadata.SourceFile = ExtractStringProperty(chunk, "SourceFile");
            metadata.FileType = ExtractStringProperty(chunk, "FileType");

            // Extract metadata object if exists
            var metadataObj = GetPropertyValue(chunk, "Metadata");
            if (metadataObj != null)
            {
                ExtractFromMetadataObject(metadataObj, metadata);
            }

            // Extract additional properties
            ExtractCustomProperties(chunk, metadata);
        }
        catch (Exception ex)
        {
            // Log error but don't throw - return partial metadata
            metadata.Properties["extraction_error"] = ex.Message;
        }

        return metadata;
    }

    private string? ExtractStringProperty(dynamic chunk, string propertyKey)
    {
        if (!_propertyMappings.TryGetValue(propertyKey, out var propertyNames))
            return null;

        foreach (var propName in propertyNames)
        {
            var value = GetPropertyValue(chunk, propName);
            if (value != null)
            {
                return value.ToString();
            }
        }

        return null;
    }

    private int ExtractIntProperty(dynamic chunk, string propertyKey, int defaultValue = 0)
    {
        if (!_propertyMappings.TryGetValue(propertyKey, out var propertyNames))
            return defaultValue;

        foreach (var propName in propertyNames)
        {
            var value = GetPropertyValue(chunk, propName);
            if (value != null)
            {
                if (int.TryParse(value.ToString(), out int result))
                    return result;
            }
        }

        return defaultValue;
    }

    private int? ExtractNullableIntProperty(dynamic chunk, string propertyKey)
    {
        var value = ExtractIntProperty(chunk, propertyKey, int.MinValue);
        return value == int.MinValue ? null : value;
    }

    private double? ExtractNullableDoubleProperty(dynamic chunk, string propertyKey)
    {
        if (!_propertyMappings.TryGetValue(propertyKey, out var propertyNames))
            return null;

        foreach (var propName in propertyNames)
        {
            var value = GetPropertyValue(chunk, propName);
            if (value != null)
            {
                if (double.TryParse(value.ToString(), out double result))
                    return result;
            }
        }

        return null;
    }

    private int ExtractChunkSize(dynamic chunk)
    {
        // First try explicit size properties
        var size = ExtractIntProperty(chunk, "ChunkSize");
        if (size > 0) return size;

        // Try to get size from content
        var contentProps = new[] { "Content", "Text", "Data", "Value" };
        foreach (var prop in contentProps)
        {
            var content = GetPropertyValue(chunk, prop);
            if (content != null)
            {
                return content.ToString()?.Length ?? 0;
            }
        }

        return 0;
    }

    private void ExtractFromMetadataObject(dynamic metadataObj, ChunkingMetadata metadata)
    {
        if (metadataObj == null) return;

        try
        {
            // If it's a dictionary
            if (metadataObj is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                {
                    var key = entry.Key?.ToString();
                    var value = entry.Value;
                    
                    if (!string.IsNullOrEmpty(key) && value != null)
                    {
                        UpdateMetadataFromKeyValue(key, value, metadata);
                    }
                }
            }
            else
            {
                // Try to extract as object properties
                Type type = metadataObj.GetType();
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                
                foreach (var prop in properties)
                {
                    var value = prop.GetValue(metadataObj);
                    if (value != null)
                    {
                        UpdateMetadataFromKeyValue(prop.Name, value, metadata);
                    }
                }
            }
        }
        catch
        {
            // Ignore metadata extraction errors
        }
    }

    private void UpdateMetadataFromKeyValue(string key, object value, ChunkingMetadata metadata)
    {
        var lowerKey = key.ToLowerInvariant();
        
        // Map to standard properties
        if (lowerKey.Contains("strategy"))
            metadata.ChunkingStrategy ??= value.ToString();
        else if (lowerKey.Contains("index"))
            metadata.ChunkIndex = ParseInt(value, metadata.ChunkIndex);
        else if (lowerKey.Contains("quality") && lowerKey.Contains("score"))
            metadata.QualityScore ??= ParseDouble(value);
        else if (lowerKey.Contains("boundary"))
            metadata.BoundaryQuality ??= ParseDouble(value);
        else if (lowerKey.Contains("complete"))
            metadata.Completeness ??= ParseDouble(value);
        else if (lowerKey.Contains("overlap"))
            metadata.OverlapSize ??= ParseInt(value);
        else if (lowerKey.Contains("source") || lowerKey.Contains("file"))
            metadata.SourceFile ??= value.ToString();
        else
        {
            // Store as custom property
            metadata.Properties[key] = value;
        }
    }

    private void ExtractCustomProperties(dynamic chunk, ChunkingMetadata metadata)
    {
        try
        {
            Type type = chunk.GetType();
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            
            // Known standard properties to skip
            var standardProps = _propertyMappings.Values.SelectMany(v => v).ToHashSet(StringComparer.OrdinalIgnoreCase);
            standardProps.Add("Content");
            standardProps.Add("Text");
            standardProps.Add("Data");
            standardProps.Add("Metadata");
            
            foreach (var prop in properties)
            {
                if (!standardProps.Contains(prop.Name))
                {
                    var value = prop.GetValue(chunk);
                    if (value != null && !metadata.Properties.ContainsKey(prop.Name))
                    {
                        metadata.Properties[prop.Name] = value;
                    }
                }
            }
        }
        catch
        {
            // Ignore custom property extraction errors
        }
    }

    private object? GetPropertyValue(dynamic obj, string propertyName)
    {
        if (obj == null) return null;

        try
        {
            Type type = obj.GetType();
            var property = type.GetProperty(propertyName, 
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return property?.GetValue(obj);
        }
        catch
        {
            return null;
        }
    }

    private int ParseInt(object? value, int defaultValue = 0)
    {
        if (value == null) return defaultValue;
        
        if (value is int intValue) return intValue;
        if (int.TryParse(value.ToString(), out int result))
            return result;
        
        return defaultValue;
    }

    private int? ParseInt(object? value)
    {
        if (value == null) return null;
        
        if (value is int intValue) return intValue;
        if (int.TryParse(value.ToString(), out int result))
            return result;
        
        return null;
    }

    private double? ParseDouble(object? value)
    {
        if (value == null) return null;
        
        if (value is double doubleValue) return doubleValue;
        if (value is float floatValue) return floatValue;
        if (double.TryParse(value.ToString(), out double result))
            return result;
        
        return null;
    }
}