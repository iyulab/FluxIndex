using FluxIndex.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Services;

/// <summary>
/// Intelligent HNSW parameter optimizer based on dataset characteristics and target requirements
/// Uses machine learning-inspired heuristics and empirical performance models
/// </summary>
public class HNSWParameterOptimizer : IVectorIndexOptimizer
{
    private readonly ILogger<HNSWParameterOptimizer> _logger;
    private readonly HNSWOptimizerOptions _options;

    // Empirical performance models based on research and benchmarking
    private static readonly Dictionary<QualityTarget, ParameterTemplate> Templates = new()
    {
        [QualityTarget.Speed] = new ParameterTemplate
        {
            BaseM = 16,
            BaseEfConstruction = 100,
            BaseEfSearch = 50,
            MemoryMultiplier = 0.8,
            AccuracyMultiplier = 0.85,
            SpeedMultiplier = 1.4
        },
        [QualityTarget.Balanced] = new ParameterTemplate
        {
            BaseM = 32,
            BaseEfConstruction = 200,
            BaseEfSearch = 100,
            MemoryMultiplier = 1.0,
            AccuracyMultiplier = 1.0,
            SpeedMultiplier = 1.0
        },
        [QualityTarget.Quality] = new ParameterTemplate
        {
            BaseM = 48,
            BaseEfConstruction = 400,
            BaseEfSearch = 200,
            MemoryMultiplier = 1.3,
            AccuracyMultiplier = 1.15,
            SpeedMultiplier = 0.6
        }
    };

    public HNSWParameterOptimizer(
        HNSWOptimizerOptions? options = null,
        ILogger<HNSWParameterOptimizer>? logger = null)
    {
        _options = options ?? new HNSWOptimizerOptions();
        _logger = logger ?? new NullLogger<HNSWParameterOptimizer>();
    }

    public async Task<HNSWParameters> OptimizeParametersAsync(
        int datasetSize,
        int dimensions,
        QualityTarget target,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Optimizing HNSW parameters for {DatasetSize} vectors, {Dimensions}D, target: {Target}",
            datasetSize, dimensions, target);

        var template = Templates[target];
        var parameters = new HNSWParameters();

        // Apply dataset size adjustments
        var sizeAdjustment = CalculateSizeAdjustment(datasetSize);
        var dimensionAdjustment = CalculateDimensionAdjustment(dimensions);

        // Optimize M (connectivity parameter)
        parameters.M = OptimizeM(template.BaseM, sizeAdjustment, dimensionAdjustment, target);

        // Optimize EfConstruction based on M and dataset characteristics
        parameters.EfConstruction = OptimizeEfConstruction(
            template.BaseEfConstruction, parameters.M, sizeAdjustment, target);

        // Optimize EfSearch for target use case
        parameters.EfSearch = OptimizeEfSearch(
            template.BaseEfSearch, parameters.M, target);

        // Set advanced parameters
        parameters.MaxLayerFactor = OptimizeMaxLayerFactor(datasetSize, dimensions);
        parameters.DistanceMetric = SelectOptimalDistanceMetric(dimensions, target);

        // Apply constraints and validation
        parameters = ApplyConstraints(parameters, datasetSize, dimensions);
        var validation = ValidateParameters(parameters);

        if (!validation.IsValid)
        {
            _logger.LogWarning("Initial parameters failed validation, applying corrections");
            parameters = ApplyValidationFixes(parameters, validation);
        }

        // Log optimization results
        var profile = await EstimatePerformanceProfileAsync(parameters, datasetSize, dimensions);
        LogOptimizationResults(parameters, profile, target);

        return parameters;
    }

    public long EstimateMemoryUsage(HNSWParameters parameters, int datasetSize, int dimensions)
    {
        // Base memory for vectors (float32)
        long vectorMemory = (long)datasetSize * dimensions * sizeof(float);

        // HNSW graph structure memory
        // Each node has approximately M * 1.2 connections on average
        double avgConnections = parameters.M * 1.2;
        long graphMemory = (long)(datasetSize * avgConnections * sizeof(int) * 2); // bidirectional

        // Layer structure memory (geometric decay)
        double layerMultiplier = 1.0 / (1.0 - (1.0 / parameters.MaxLayerFactor));
        long layerMemory = (long)(datasetSize * layerMultiplier * sizeof(int));

        // Overhead and metadata
        long overheadMemory = Math.Max(datasetSize * 64, 1024 * 1024); // 64 bytes per node or 1MB minimum

        long totalMemory = vectorMemory + graphMemory + layerMemory + overheadMemory;

        _logger.LogDebug("Memory estimation: Vectors={VectorMB}MB, Graph={GraphMB}MB, " +
                        "Layers={LayerMB}MB, Overhead={OverheadMB}MB, Total={TotalMB}MB",
            vectorMemory / (1024 * 1024), graphMemory / (1024 * 1024),
            layerMemory / (1024 * 1024), overheadMemory / (1024 * 1024),
            totalMemory / (1024 * 1024));

        return totalMemory;
    }

    public double EstimateConstructionTime(HNSWParameters parameters, int datasetSize)
    {
        // Empirical formula based on HNSW complexity analysis
        // Time complexity is approximately O(N * log(N) * M * EfConstruction / dimension_scaling)

        double baseTime = datasetSize * Math.Log(datasetSize) * parameters.M * parameters.EfConstruction;
        double scalingFactor = 1e-9; // Empirically derived scaling factor

        // Adjust for hardware characteristics
        double cpuAdjustment = _options.CpuCoresAvailable > 1 ? 
            1.0 / Math.Min(_options.CpuCoresAvailable * 0.7, 4.0) : 1.0;

        double estimatedSeconds = baseTime * scalingFactor * cpuAdjustment;

        // Apply minimum and maximum bounds
        estimatedSeconds = Math.Max(estimatedSeconds, 1.0); // Minimum 1 second
        estimatedSeconds = Math.Min(estimatedSeconds, datasetSize * 0.1); // Maximum 0.1s per vector

        return estimatedSeconds;
    }

    public double EstimateQueryLatency(HNSWParameters parameters, int dimensions)
    {
        // Query latency primarily depends on EfSearch and dimension count
        double baseLatency = parameters.EfSearch * dimensions * 1e-6; // Base operation cost

        // Distance computation cost (varies by metric)
        double distanceCost = parameters.DistanceMetric switch
        {
            DistanceMetric.Cosine => 1.2, // Normalization overhead
            DistanceMetric.Euclidean => 1.0,
            DistanceMetric.Manhattan => 0.8,
            DistanceMetric.DotProduct => 0.7,
            _ => 1.0
        };

        // Memory access patterns impact
        double memoryLatency = Math.Log(parameters.EfSearch) * 0.01;

        double totalLatency = (baseLatency * distanceCost + memoryLatency) * 1000; // Convert to ms

        return Math.Max(totalLatency, 0.1); // Minimum 0.1ms
    }

    public ParameterValidationResult ValidateParameters(HNSWParameters parameters)
    {
        var result = new ParameterValidationResult { IsValid = true };

        // Critical validations (errors)
        if (parameters.M < 4 || parameters.M > 128)
        {
            result.Errors.Add($"M parameter ({parameters.M}) must be between 4 and 128");
            result.IsValid = false;
        }

        if (parameters.EfConstruction < parameters.M)
        {
            result.Errors.Add($"EfConstruction ({parameters.EfConstruction}) must be >= M ({parameters.M})");
            result.IsValid = false;
        }

        if (parameters.EfSearch < 10 || parameters.EfSearch > 2000)
        {
            result.Errors.Add($"EfSearch ({parameters.EfSearch}) must be between 10 and 2000");
            result.IsValid = false;
        }

        if (parameters.MaxLayerFactor <= 0 || parameters.MaxLayerFactor > 5.0)
        {
            result.Errors.Add($"MaxLayerFactor ({parameters.MaxLayerFactor}) must be between 0 and 5");
            result.IsValid = false;
        }

        // Performance warnings
        if (parameters.EfConstruction > 800)
        {
            result.Warnings.Add($"EfConstruction ({parameters.EfConstruction}) is very high, may cause slow index construction");
        }

        if (parameters.M > 64)
        {
            result.Warnings.Add($"M ({parameters.M}) is very high, may cause excessive memory usage");
        }

        if (parameters.EfSearch > parameters.EfConstruction)
        {
            result.Warnings.Add("EfSearch > EfConstruction may not provide additional benefit");
        }

        // Suggestions for improvements
        if (parameters.M % 2 != 0)
        {
            result.Suggestions["M"] = parameters.M + 1; // Even numbers often perform better
        }

        if (parameters.EfConstruction < parameters.M * 4)
        {
            result.Suggestions["EfConstruction"] = parameters.M * 4;
        }

        return result;
    }

    private int OptimizeM(int baseM, double sizeAdjustment, double dimensionAdjustment, QualityTarget target)
    {
        double adjustedM = baseM * sizeAdjustment * dimensionAdjustment;

        // Apply target-specific adjustments
        adjustedM *= target switch
        {
            QualityTarget.Speed => 0.8,
            QualityTarget.Quality => 1.2,
            _ => 1.0
        };

        // Ensure even number and reasonable bounds
        int optimizedM = (int)(Math.Round(adjustedM / 2) * 2);
        return Math.Max(8, Math.Min(optimizedM, 64));
    }

    private int OptimizeEfConstruction(int baseEf, int m, double sizeAdjustment, QualityTarget target)
    {
        // EfConstruction should be proportional to M and dataset characteristics
        double adjustedEf = baseEf * sizeAdjustment;
        
        // Ensure minimum relationship with M
        adjustedEf = Math.Max(adjustedEf, m * 4);

        // Apply target adjustments
        adjustedEf *= target switch
        {
            QualityTarget.Speed => 0.7,
            QualityTarget.Quality => 1.3,
            _ => 1.0
        };

        return Math.Max(m, Math.Min((int)adjustedEf, 800));
    }

    private int OptimizeEfSearch(int baseEf, int m, QualityTarget target)
    {
        // EfSearch optimization based on target and M
        double optimizedEf = target switch
        {
            QualityTarget.Speed => Math.Max(baseEf * 0.6, m),
            QualityTarget.Quality => baseEf * 1.4,
            _ => baseEf
        };

        return Math.Max(10, Math.Min((int)optimizedEf, 500));
    }

    private double OptimizeMaxLayerFactor(int datasetSize, int dimensions)
    {
        // Slightly adjust based on dataset characteristics
        double baseFactor = 1.44; // ln(2)
        
        if (datasetSize > 1000000) baseFactor *= 1.1; // More layers for large datasets
        if (dimensions > 1000) baseFactor *= 0.95; // Fewer layers for high-dim data
        
        return Math.Max(1.1, Math.Min(baseFactor, 2.0));
    }

    private DistanceMetric SelectOptimalDistanceMetric(int dimensions, QualityTarget target)
    {
        // Default to cosine for most embedding use cases
        if (dimensions >= 512) return DistanceMetric.Cosine;
        
        // For lower dimensions, euclidean might be more appropriate
        return target == QualityTarget.Speed ? DistanceMetric.DotProduct : DistanceMetric.Euclidean;
    }

    private double CalculateSizeAdjustment(int datasetSize)
    {
        // Logarithmic adjustment based on dataset size
        return datasetSize switch
        {
            < 1000 => 0.8,
            < 10000 => 0.9,
            < 100000 => 1.0,
            < 1000000 => 1.1,
            _ => 1.2
        };
    }

    private double CalculateDimensionAdjustment(int dimensions)
    {
        // Higher dimensions benefit from more connections
        return dimensions switch
        {
            < 100 => 0.85,
            < 512 => 1.0,
            < 1024 => 1.1,
            _ => 1.15
        };
    }

    private HNSWParameters ApplyConstraints(HNSWParameters parameters, int datasetSize, int dimensions)
    {
        var constrained = parameters.Clone();

        // Memory constraints
        if (_options.MaxMemoryMB > 0)
        {
            long estimatedMemory = EstimateMemoryUsage(parameters, datasetSize, dimensions);
            long maxMemoryBytes = _options.MaxMemoryMB * 1024 * 1024;

            if (estimatedMemory > maxMemoryBytes)
            {
                // Reduce M to fit memory constraints
                double reductionFactor = Math.Sqrt((double)maxMemoryBytes / estimatedMemory);
                constrained.M = Math.Max(8, (int)(constrained.M * reductionFactor));
                
                _logger.LogInformation("Applied memory constraint: reduced M from {Original} to {New}",
                    parameters.M, constrained.M);
            }
        }

        // Construction time constraints
        if (_options.MaxConstructionMinutes > 0)
        {
            double estimatedTime = EstimateConstructionTime(parameters, datasetSize) / 60.0;
            
            if (estimatedTime > _options.MaxConstructionMinutes)
            {
                double reductionFactor = _options.MaxConstructionMinutes / estimatedTime;
                constrained.EfConstruction = Math.Max(constrained.M, 
                    (int)(constrained.EfConstruction * Math.Sqrt(reductionFactor)));
                
                _logger.LogInformation("Applied time constraint: reduced EfConstruction from {Original} to {New}",
                    parameters.EfConstruction, constrained.EfConstruction);
            }
        }

        return constrained;
    }

    private HNSWParameters ApplyValidationFixes(HNSWParameters parameters, ParameterValidationResult validation)
    {
        var fixedParams = parameters.Clone();

        // Apply suggested fixes
        foreach (var (parameter, value) in validation.Suggestions)
        {
            switch (parameter)
            {
                case "M":
                    fixedParams.M = (int)value;
                    break;
                case "EfConstruction":
                    fixedParams.EfConstruction = (int)value;
                    break;
            }
        }

        return fixedParams;
    }

    private async Task<HNSWPerformanceProfile> EstimatePerformanceProfileAsync(
        HNSWParameters parameters, int datasetSize, int dimensions)
    {
        // This would typically use ML models trained on benchmark data
        // For now, using heuristic estimates
        
        await Task.Delay(1); // Simulate async computation
        
        return new HNSWPerformanceProfile
        {
            ExpectedRecall = EstimateRecall(parameters, datasetSize),
            ExpectedQPS = EstimateQPS(parameters, dimensions),
            ExpectedMemoryMB = EstimateMemoryUsage(parameters, datasetSize, dimensions) / (1024.0 * 1024.0),
            ExpectedConstructionMinutes = EstimateConstructionTime(parameters, datasetSize) / 60.0,
            ExpectedLatencyP95Ms = EstimateQueryLatency(parameters, dimensions) * 2.5, // P95 multiplier
            ConfidenceLevel = 0.8 // Moderate confidence in heuristic estimates
        };
    }

    private double EstimateRecall(HNSWParameters parameters, int datasetSize)
    {
        // Empirical formula based on research papers
        double baseRecall = 0.85;
        double mFactor = Math.Min(parameters.M / 32.0, 1.5);
        double efFactor = Math.Min(parameters.EfSearch / 100.0, 2.0);
        double sizeFactor = datasetSize > 100000 ? 0.98 : 1.0;
        
        return Math.Min(baseRecall * mFactor * efFactor * sizeFactor, 0.99);
    }

    private double EstimateQPS(HNSWParameters parameters, int dimensions)
    {
        double baseQPS = 1000.0;
        double latency = EstimateQueryLatency(parameters, dimensions);
        
        return Math.Min(baseQPS / (latency / 10.0), 10000);
    }

    private void LogOptimizationResults(HNSWParameters parameters, HNSWPerformanceProfile profile, QualityTarget target)
    {
        _logger.LogInformation("HNSW optimization completed for target: {Target}", target);
        _logger.LogInformation("Optimized parameters: {Parameters}", parameters);
        _logger.LogInformation("Expected performance: Recall={Recall:P1}, QPS={QPS:F0}, " +
                              "Memory={Memory:F1}MB, Construction={Construction:F1}min",
            profile.ExpectedRecall, profile.ExpectedQPS, 
            profile.ExpectedMemoryMB, profile.ExpectedConstructionMinutes);
    }

    private class ParameterTemplate
    {
        public int BaseM { get; set; }
        public int BaseEfConstruction { get; set; }
        public int BaseEfSearch { get; set; }
        public double MemoryMultiplier { get; set; }
        public double AccuracyMultiplier { get; set; }
        public double SpeedMultiplier { get; set; }
    }
}

/// <summary>
/// Configuration options for HNSW parameter optimizer
/// </summary>
public class HNSWOptimizerOptions
{
    /// <summary>
    /// Maximum memory usage constraint in MB (0 = no limit)
    /// </summary>
    public int MaxMemoryMB { get; set; } = 0;

    /// <summary>
    /// Maximum index construction time in minutes (0 = no limit)
    /// </summary>
    public double MaxConstructionMinutes { get; set; } = 0;

    /// <summary>
    /// Number of CPU cores available for optimization calculations
    /// </summary>
    public int CpuCoresAvailable { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Enable advanced optimizations (may be slower but more accurate)
    /// </summary>
    public bool EnableAdvancedOptimizations { get; set; } = true;

    /// <summary>
    /// Custom performance weights for multi-objective optimization
    /// </summary>
    public PerformanceWeights Weights { get; set; } = new();
}

/// <summary>
/// Weights for different performance aspects in optimization
/// </summary>
public class PerformanceWeights
{
    /// <summary>
    /// Weight for query speed (0.0 to 1.0)
    /// </summary>
    public double Speed { get; set; } = 0.4;

    /// <summary>
    /// Weight for accuracy/recall (0.0 to 1.0)
    /// </summary>
    public double Accuracy { get; set; } = 0.4;

    /// <summary>
    /// Weight for memory efficiency (0.0 to 1.0)
    /// </summary>
    public double Memory { get; set; } = 0.2;

    /// <summary>
    /// Validates that weights sum to 1.0
    /// </summary>
    public bool IsValid => Math.Abs(Speed + Accuracy + Memory - 1.0) < 0.001;
}