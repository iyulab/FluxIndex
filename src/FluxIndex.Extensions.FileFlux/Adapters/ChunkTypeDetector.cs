using System.Reflection;

namespace FluxIndex.Extensions.FileFlux.Adapters;

/// <summary>
/// Detects the type and structure of FileFlux chunks
/// </summary>
public class ChunkTypeDetector
{
    /// <summary>
    /// Detect chunk type from dynamic object
    /// </summary>
    public ChunkType DetectType(dynamic chunk)
    {
        if (chunk == null) return ChunkType.Unknown;

        try
        {
            Type type = chunk.GetType();
            
            // Check for known FileFlux chunk patterns
            if (HasProperties(type, "Content", "ChunkIndex", "Metadata"))
            {
                return ChunkType.StandardChunk;
            }
            
            if (HasProperties(type, "Text", "StartPosition", "EndPosition"))
            {
                return ChunkType.PositionalChunk;
            }
            
            if (HasProperties(type, "Content", "QualityScore", "BoundaryQuality"))
            {
                return ChunkType.QualityChunk;
            }
            
            if (HasProperties(type, "Content", "OverlapWithPrevious", "OverlapWithNext"))
            {
                return ChunkType.OverlapChunk;
            }

            // Check if it's a simple string
            if (type == typeof(string))
            {
                return ChunkType.SimpleText;
            }

            // Has content-like property
            if (HasAnyProperty(type, "Content", "Text", "Data", "Value"))
            {
                return ChunkType.GenericChunk;
            }
        }
        catch
        {
            // Fallback for dynamic objects that throw on reflection
        }

        return ChunkType.Unknown;
    }

    /// <summary>
    /// Infer chunking strategy from chunk characteristics
    /// </summary>
    public string InferStrategy(dynamic chunk, ChunkType type)
    {
        try
        {
            // Check for explicit strategy property
            var strategy = GetPropertyValue(chunk, "Strategy") ?? 
                          GetPropertyValue(chunk, "ChunkingStrategy");
            if (strategy != null)
            {
                return strategy.ToString() ?? "Auto";
            }

            // Infer from chunk type and properties
            switch (type)
            {
                case ChunkType.QualityChunk:
                    var qualityScore = GetPropertyValue<double?>(chunk, "QualityScore");
                    if (qualityScore > 0.8) return "Intelligent";
                    if (qualityScore > 0.6) return "Smart";
                    break;

                case ChunkType.OverlapChunk:
                    return "Semantic";

                case ChunkType.PositionalChunk:
                    // Check if positions are uniform
                    var size = GetChunkSize(chunk);
                    if (IsUniformSize(size)) return "FixedSize";
                    break;
            }

            // Check for structural hints
            if (HasProperty(chunk, "ParagraphBoundary") || HasProperty(chunk, "SectionBreak"))
            {
                return "Paragraph";
            }

            return "Auto";
        }
        catch
        {
            return "Auto";
        }
    }

    private bool HasProperties(Type type, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (!HasProperty(type, name))
                return false;
        }
        return true;
    }

    private bool HasAnyProperty(Type type, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (HasProperty(type, name))
                return true;
        }
        return false;
    }

    private bool HasProperty(Type type, string propertyName)
    {
        return type.GetProperty(propertyName, 
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) != null;
    }

    private bool HasProperty(dynamic obj, string propertyName)
    {
        try
        {
            Type type = obj.GetType();
            return HasProperty(type, propertyName);
        }
        catch
        {
            return false;
        }
    }

    private object? GetPropertyValue(dynamic obj, string propertyName)
    {
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

    private T? GetPropertyValue<T>(dynamic obj, string propertyName)
    {
        var value = GetPropertyValue(obj, propertyName);
        if (value == null) return default;
        
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    private int GetChunkSize(dynamic chunk)
    {
        var content = GetPropertyValue(chunk, "Content") ?? 
                     GetPropertyValue(chunk, "Text") ?? 
                     GetPropertyValue(chunk, "Data");
        
        return content?.ToString()?.Length ?? 0;
    }

    private bool IsUniformSize(int size, int tolerance = 50)
    {
        // Check if size is close to common fixed sizes
        int[] commonSizes = { 256, 512, 1024, 2048, 4096 };
        
        foreach (var commonSize in commonSizes)
        {
            if (Math.Abs(size - commonSize) <= tolerance)
                return true;
        }
        
        return false;
    }
}

/// <summary>
/// Types of chunks detected from FileFlux
/// </summary>
public enum ChunkType
{
    Unknown,
    StandardChunk,
    QualityChunk,
    OverlapChunk,
    PositionalChunk,
    GenericChunk,
    SimpleText
}