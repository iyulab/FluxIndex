using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Application.Interfaces;

/// <summary>
/// Interface for optimizing vector index parameters based on dataset characteristics
/// </summary>
public interface IVectorIndexOptimizer
{
    /// <summary>
    /// Optimizes HNSW parameters based on dataset characteristics and target requirements
    /// </summary>
    /// <param name="datasetSize">Total number of vectors in the dataset</param>
    /// <param name="dimensions">Vector dimensionality</param>
    /// <param name="target">Optimization target (speed, balanced, or quality)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Optimized HNSW parameters</returns>
    Task<HNSWParameters> OptimizeParametersAsync(
        int datasetSize,
        int dimensions,
        QualityTarget target,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimates memory usage for given HNSW parameters
    /// </summary>
    /// <param name="parameters">HNSW parameters</param>
    /// <param name="datasetSize">Number of vectors</param>
    /// <param name="dimensions">Vector dimensions</param>
    /// <returns>Estimated memory usage in bytes</returns>
    long EstimateMemoryUsage(HNSWParameters parameters, int datasetSize, int dimensions);

    /// <summary>
    /// Estimates index construction time for given parameters
    /// </summary>
    /// <param name="parameters">HNSW parameters</param>
    /// <param name="datasetSize">Number of vectors</param>
    /// <returns>Estimated construction time in seconds</returns>
    double EstimateConstructionTime(HNSWParameters parameters, int datasetSize);

    /// <summary>
    /// Estimates query latency for given parameters
    /// </summary>
    /// <param name="parameters">HNSW parameters</param>
    /// <param name="dimensions">Vector dimensions</param>
    /// <returns>Estimated query latency in milliseconds</returns>
    double EstimateQueryLatency(HNSWParameters parameters, int dimensions);

    /// <summary>
    /// Validates HNSW parameters for practical constraints
    /// </summary>
    /// <param name="parameters">Parameters to validate</param>
    /// <returns>Validation result with any issues found</returns>
    ParameterValidationResult ValidateParameters(HNSWParameters parameters);
}

/// <summary>
/// HNSW (Hierarchical Navigable Small World) parameters for vector index optimization
/// </summary>
public class HNSWParameters
{
    /// <summary>
    /// Maximum number of bi-directional links for each node during construction
    /// Higher values = better accuracy, more memory, slower construction
    /// Typical range: 8-64, common values: 16 (speed), 32 (balanced), 48 (quality)
    /// </summary>
    public int M { get; set; } = 32;

    /// <summary>
    /// Size of the dynamic candidate list used during construction
    /// Higher values = better index quality, slower construction
    /// Typical range: 40-500, should be >= M, common: 100-200
    /// </summary>
    public int EfConstruction { get; set; } = 200;

    /// <summary>
    /// Size of the dynamic candidate list used during search
    /// Higher values = better recall, slower search
    /// Typical range: 50-800, should be >= TopK
    /// </summary>
    public int EfSearch { get; set; } = 100;

    /// <summary>
    /// Maximum layer level (ml parameter)
    /// Controls the probability of selecting higher layers
    /// Typical value: 1/ln(2) ≈ 1.44
    /// </summary>
    public double MaxLayerFactor { get; set; } = 1.44;

    /// <summary>
    /// Random seed for reproducible index construction
    /// </summary>
    public int? RandomSeed { get; set; }

    /// <summary>
    /// Custom distance metric if different from default cosine similarity
    /// </summary>
    public DistanceMetric DistanceMetric { get; set; } = DistanceMetric.Cosine;

    /// <summary>
    /// Validates that parameters are within reasonable bounds
    /// </summary>
    public bool IsValid()
    {
        return M >= 4 && M <= 128 &&
               EfConstruction >= M && EfConstruction <= 2000 &&
               EfSearch >= 10 && EfSearch <= 2000 &&
               MaxLayerFactor > 0 && MaxLayerFactor <= 5.0;
    }

    /// <summary>
    /// Creates a copy of the current parameters
    /// </summary>
    public HNSWParameters Clone()
    {
        return new HNSWParameters
        {
            M = M,
            EfConstruction = EfConstruction,
            EfSearch = EfSearch,
            MaxLayerFactor = MaxLayerFactor,
            RandomSeed = RandomSeed,
            DistanceMetric = DistanceMetric
        };
    }

    public override string ToString()
    {
        return $"M={M}, EfConstruction={EfConstruction}, EfSearch={EfSearch}, " +
               $"MaxLayerFactor={MaxLayerFactor:F2}, Distance={DistanceMetric}";
    }
}

/// <summary>
/// Optimization targets for HNSW parameter tuning
/// </summary>
public enum QualityTarget
{
    /// <summary>
    /// Prioritize search speed over accuracy
    /// Suitable for: real-time applications, high QPS scenarios
    /// Typical settings: M=16, EfConstruction=100, EfSearch=50
    /// </summary>
    Speed,

    /// <summary>
    /// Balance between speed and accuracy
    /// Suitable for: most production applications
    /// Typical settings: M=32, EfConstruction=200, EfSearch=100
    /// </summary>
    Balanced,

    /// <summary>
    /// Prioritize accuracy over speed
    /// Suitable for: offline batch processing, research applications
    /// Typical settings: M=48, EfConstruction=400, EfSearch=200
    /// </summary>
    Quality,

    /// <summary>
    /// Custom optimization based on specific requirements
    /// Allows for fine-tuned parameter selection
    /// </summary>
    Custom
}

/// <summary>
/// Distance metrics supported for HNSW indexing
/// </summary>
public enum DistanceMetric
{
    /// <summary>
    /// Cosine similarity (1 - cosine distance)
    /// Best for: normalized embeddings, text similarity
    /// </summary>
    Cosine,

    /// <summary>
    /// Euclidean (L2) distance
    /// Best for: general purpose, geometric data
    /// </summary>
    Euclidean,

    /// <summary>
    /// Manhattan (L1) distance
    /// Best for: high-dimensional sparse data
    /// </summary>
    Manhattan,

    /// <summary>
    /// Dot product similarity
    /// Best for: when magnitude matters
    /// </summary>
    DotProduct
}

/// <summary>
/// Result of parameter validation with potential issues
/// </summary>
public class ParameterValidationResult
{
    /// <summary>
    /// Whether the parameters are valid
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// List of validation warnings
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// List of validation errors
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Suggested parameter adjustments
    /// </summary>
    public Dictionary<string, object> Suggestions { get; set; } = new();

    /// <summary>
    /// Creates a successful validation result
    /// </summary>
    public static ParameterValidationResult Success()
    {
        return new ParameterValidationResult { IsValid = true };
    }

    /// <summary>
    /// Creates a validation result with errors
    /// </summary>
    public static ParameterValidationResult WithErrors(params string[] errors)
    {
        return new ParameterValidationResult
        {
            IsValid = false,
            Errors = errors.ToList()
        };
    }

    /// <summary>
    /// Creates a validation result with warnings
    /// </summary>
    public static ParameterValidationResult WithWarnings(params string[] warnings)
    {
        return new ParameterValidationResult
        {
            IsValid = true,
            Warnings = warnings.ToList()
        };
    }
}

/// <summary>
/// Performance characteristics for HNSW index configuration
/// </summary>
public class HNSWPerformanceProfile
{
    /// <summary>
    /// Expected recall@10 for the configuration
    /// </summary>
    public double ExpectedRecall { get; set; }

    /// <summary>
    /// Expected queries per second
    /// </summary>
    public double ExpectedQPS { get; set; }

    /// <summary>
    /// Expected memory usage in MB
    /// </summary>
    public double ExpectedMemoryMB { get; set; }

    /// <summary>
    /// Expected index construction time in minutes
    /// </summary>
    public double ExpectedConstructionMinutes { get; set; }

    /// <summary>
    /// Expected query latency in milliseconds (P95)
    /// </summary>
    public double ExpectedLatencyP95Ms { get; set; }

    /// <summary>
    /// Confidence level of the estimates (0.0 to 1.0)
    /// </summary>
    public double ConfidenceLevel { get; set; } = 0.85;
}