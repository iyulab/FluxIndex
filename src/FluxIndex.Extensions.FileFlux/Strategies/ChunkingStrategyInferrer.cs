using FluxIndex.Extensions.FileFlux.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Extensions.FileFlux.Strategies;

/// <summary>
/// Infers chunking strategy from chunk characteristics
/// </summary>
public class ChunkingStrategyInferrer
{
    private readonly ILogger<ChunkingStrategyInferrer> _logger;

    public ChunkingStrategyInferrer(ILogger<ChunkingStrategyInferrer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Infer chunking strategy from chunk metadata
    /// </summary>
    public ChunkingStrategy InferStrategy(ChunkingMetadata metadata)
    {
        // If strategy is explicitly specified, use it
        if (!string.IsNullOrEmpty(metadata.ChunkingStrategy))
        {
            var explicitStrategy = ParseExplicitStrategy(metadata.ChunkingStrategy);
            if (explicitStrategy != ChunkingStrategy.Auto)
            {
                _logger.LogDebug("Using explicit strategy: {Strategy}", explicitStrategy);
                return explicitStrategy;
            }
        }

        // Analyze chunk features
        var features = ExtractFeatures(metadata);
        
        // Rule-based inference
        var inferredStrategy = InferFromFeatures(features);
        
        _logger.LogDebug("Inferred strategy: {Strategy} from features", inferredStrategy);
        return inferredStrategy;
    }

    /// <summary>
    /// Infer strategy from multiple chunks for better accuracy
    /// </summary>
    public ChunkingStrategy InferFromMultipleChunks(IEnumerable<ChunkingMetadata> metadataList)
    {
        var strategies = new Dictionary<ChunkingStrategy, int>();
        var features = new AggregatedFeatures();

        foreach (var metadata in metadataList)
        {
            var strategy = InferStrategy(metadata);
            strategies[strategy] = strategies.GetValueOrDefault(strategy) + 1;
            
            // Aggregate features
            features.Update(ExtractFeatures(metadata));
        }

        // If one strategy dominates, use it
        var dominantStrategy = strategies
            .OrderByDescending(kvp => kvp.Value)
            .FirstOrDefault();

        if (dominantStrategy.Value > metadataList.Count() * 0.6)
        {
            return dominantStrategy.Key;
        }

        // Otherwise, use aggregated features
        return InferFromAggregatedFeatures(features);
    }

    private ChunkingStrategy ParseExplicitStrategy(string strategyName)
    {
        return strategyName.ToLowerInvariant() switch
        {
            "auto" => ChunkingStrategy.Auto,
            "smart" => ChunkingStrategy.Smart,
            "intelligent" => ChunkingStrategy.Intelligent,
            "memoryoptimized" => ChunkingStrategy.MemoryOptimized,
            "memoryoptimizedintelligent" => ChunkingStrategy.MemoryOptimized,
            "semantic" => ChunkingStrategy.Semantic,
            "paragraph" => ChunkingStrategy.Paragraph,
            "fixedsize" => ChunkingStrategy.FixedSize,
            "fixed" => ChunkingStrategy.FixedSize,
            _ => ChunkingStrategy.Auto
        };
    }

    private ChunkFeatures ExtractFeatures(ChunkingMetadata metadata)
    {
        return new ChunkFeatures
        {
            HasQualityScore = metadata.QualityScore.HasValue,
            QualityScore = metadata.QualityScore ?? 0,
            HasBoundaryQuality = metadata.BoundaryQuality.HasValue,
            BoundaryQuality = metadata.BoundaryQuality ?? 0,
            HasCompleteness = metadata.Completeness.HasValue,
            Completeness = metadata.Completeness ?? 0,
            HasOverlap = metadata.OverlapSize.HasValue && metadata.OverlapSize > 0,
            OverlapSize = metadata.OverlapSize ?? 0,
            ChunkSize = metadata.ChunkSize,
            ChunkIndex = metadata.ChunkIndex,
            HasFileType = !string.IsNullOrEmpty(metadata.FileType),
            FileType = metadata.FileType,
            IsSizeUniform = CheckSizeUniformity(metadata.ChunkSize),
            HasCustomProperties = metadata.Properties.Any()
        };
    }

    private ChunkingStrategy InferFromFeatures(ChunkFeatures features)
    {
        // High quality with boundary markers → Intelligent
        if (features.HasQualityScore && features.QualityScore > 0.8 &&
            features.HasBoundaryQuality && features.BoundaryQuality > 0.7)
        {
            return ChunkingStrategy.Intelligent;
        }

        // Good quality with completeness → Smart
        if (features.HasQualityScore && features.QualityScore > 0.6 &&
            features.HasCompleteness && features.Completeness > 0.7)
        {
            return ChunkingStrategy.Smart;
        }

        // Has overlap → Semantic
        if (features.HasOverlap && features.OverlapSize > 20)
        {
            return ChunkingStrategy.Semantic;
        }

        // Uniform size → FixedSize
        if (features.IsSizeUniform)
        {
            return ChunkingStrategy.FixedSize;
        }

        // Markdown or structured text → Paragraph
        if (features.FileType?.ToLowerInvariant() is "md" or "markdown" or "txt")
        {
            if (features.ChunkSize > 200 && features.ChunkSize < 2000)
            {
                return ChunkingStrategy.Paragraph;
            }
        }

        // Small chunks with overlap → MemoryOptimized
        if (features.ChunkSize < 512 && features.HasOverlap)
        {
            return ChunkingStrategy.MemoryOptimized;
        }

        // Default
        return ChunkingStrategy.Auto;
    }

    private ChunkingStrategy InferFromAggregatedFeatures(AggregatedFeatures features)
    {
        // Check average quality scores
        if (features.AverageQualityScore > 0.8)
        {
            return ChunkingStrategy.Intelligent;
        }

        if (features.AverageQualityScore > 0.6 && features.AverageCompleteness > 0.7)
        {
            return ChunkingStrategy.Smart;
        }

        // Check size variance
        if (features.SizeVariance < 0.1) // Low variance = uniform
        {
            return ChunkingStrategy.FixedSize;
        }

        // Check overlap consistency
        if (features.OverlapConsistency > 0.8)
        {
            return ChunkingStrategy.Semantic;
        }

        return ChunkingStrategy.Auto;
    }

    private bool CheckSizeUniformity(int size, int tolerance = 50)
    {
        // Common fixed chunk sizes
        int[] commonSizes = { 128, 256, 384, 512, 768, 1024, 1536, 2048, 3072, 4096 };
        
        return commonSizes.Any(commonSize => Math.Abs(size - commonSize) <= tolerance);
    }

    private class ChunkFeatures
    {
        public bool HasQualityScore { get; set; }
        public double QualityScore { get; set; }
        public bool HasBoundaryQuality { get; set; }
        public double BoundaryQuality { get; set; }
        public bool HasCompleteness { get; set; }
        public double Completeness { get; set; }
        public bool HasOverlap { get; set; }
        public int OverlapSize { get; set; }
        public int ChunkSize { get; set; }
        public int ChunkIndex { get; set; }
        public bool IsSizeUniform { get; set; }
        public bool HasFileType { get; set; }
        public string? FileType { get; set; }
        public bool HasCustomProperties { get; set; }
    }

    private class AggregatedFeatures
    {
        private readonly List<double> _qualityScores = new();
        private readonly List<double> _completenessScores = new();
        private readonly List<int> _chunkSizes = new();
        private readonly List<int> _overlapSizes = new();

        public double AverageQualityScore => _qualityScores.Any() ? _qualityScores.Average() : 0;
        public double AverageCompleteness => _completenessScores.Any() ? _completenessScores.Average() : 0;
        public double SizeVariance => CalculateVariance(_chunkSizes);
        public double OverlapConsistency => CalculateConsistency(_overlapSizes);

        public void Update(ChunkFeatures features)
        {
            if (features.HasQualityScore)
                _qualityScores.Add(features.QualityScore);
            
            if (features.HasCompleteness)
                _completenessScores.Add(features.Completeness);
            
            _chunkSizes.Add(features.ChunkSize);
            
            if (features.HasOverlap)
                _overlapSizes.Add(features.OverlapSize);
        }

        private double CalculateVariance(List<int> values)
        {
            if (!values.Any()) return 0;
            
            double mean = values.Average();
            double variance = values.Select(v => Math.Pow(v - mean, 2)).Average();
            double coefficientOfVariation = Math.Sqrt(variance) / mean;
            
            return coefficientOfVariation;
        }

        private double CalculateConsistency(List<int> values)
        {
            if (values.Count < 2) return 0;
            
            var uniqueValues = values.Distinct().Count();
            return 1.0 - (double)(uniqueValues - 1) / values.Count;
        }
    }
}

/// <summary>
/// Chunking strategies detected from FileFlux output
/// </summary>
public enum ChunkingStrategy
{
    Auto,
    Smart,
    Intelligent,
    MemoryOptimized,
    Semantic,
    Paragraph,
    FixedSize
}