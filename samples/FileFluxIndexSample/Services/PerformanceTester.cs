using System.Diagnostics;
using FluxIndex.Extensions.FileFlux;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FileFluxIndexSample;

/// <summary>
/// 성능 테스트 서비스
/// </summary>
public class PerformanceTester
{
    private readonly IFileFluxIntegration _integration;
    private readonly ILogger<PerformanceTester> _logger;
    private readonly IConfiguration _configuration;

    public PerformanceTester(
        IFileFluxIntegration integration,
        ILogger<PerformanceTester> logger,
        IConfiguration configuration)
    {
        _integration = integration;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<List<ProcessingResult>> TestBatchProcessing(string[] filePaths)
    {
        var results = new List<ProcessingResult>();
        var totalStopwatch = Stopwatch.StartNew();

        // 병렬 처리 옵션
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        await Parallel.ForEachAsync(filePaths, options, async (filePath, ct) =>
        {
            var fileStopwatch = Stopwatch.StartNew();
            
            try
            {
                var result = await _integration.ProcessAndIndexAsync(filePath, new ProcessingOptions
                {
                    ChunkingStrategy = "Auto",
                    MaxChunkSize = 512,
                    EnableQualityScoring = true
                });

                fileStopwatch.Stop();

                lock (results)
                {
                    results.Add(new ProcessingResult
                    {
                        FileName = filePath,
                        ChunkCount = result.ChunkCount,
                        ProcessingTimeMs = fileStopwatch.ElapsedMilliseconds,
                        Success = true,
                        AverageQuality = result.AverageQualityScore
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process {FilePath}", filePath);
                
                lock (results)
                {
                    results.Add(new ProcessingResult
                    {
                        FileName = filePath,
                        ProcessingTimeMs = fileStopwatch.ElapsedMilliseconds,
                        Success = false,
                        Error = ex.Message
                    });
                }
            }
        });

        totalStopwatch.Stop();

        _logger.LogInformation(
            "Batch processing completed: {FileCount} files in {TotalTime}ms",
            results.Count,
            totalStopwatch.ElapsedMilliseconds);

        return results;
    }

    public async Task<MemoryTestResult> TestMemoryUsage(string filePath)
    {
        var initialMemory = GC.GetTotalMemory(true);
        var peakMemory = initialMemory;
        var memoryMonitor = new Timer(_ =>
        {
            var current = GC.GetTotalMemory(false);
            if (current > peakMemory)
                peakMemory = current;
        }, null, 0, 100);

        var stopwatch = Stopwatch.StartNew();
        
        await _integration.ProcessAndIndexAsync(filePath);
        
        stopwatch.Stop();
        memoryMonitor.Dispose();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(true);

        return new MemoryTestResult
        {
            InitialMemoryMB = initialMemory / (1024.0 * 1024.0),
            PeakMemoryMB = peakMemory / (1024.0 * 1024.0),
            FinalMemoryMB = finalMemory / (1024.0 * 1024.0),
            ProcessingTimeMs = stopwatch.ElapsedMilliseconds
        };
    }

    public async Task<ThroughputResult> TestThroughput(string[] filePaths, int durationSeconds = 60)
    {
        var endTime = DateTime.UtcNow.AddSeconds(durationSeconds);
        var processedCount = 0;
        var totalChunks = 0;
        var errors = 0;

        while (DateTime.UtcNow < endTime)
        {
            foreach (var filePath in filePaths)
            {
                if (DateTime.UtcNow >= endTime) break;

                try
                {
                    var result = await _integration.ProcessAndIndexAsync(filePath);
                    processedCount++;
                    totalChunks += result.ChunkCount;
                }
                catch
                {
                    errors++;
                }
            }
        }

        return new ThroughputResult
        {
            DocumentsProcessed = processedCount,
            ChunksProcessed = totalChunks,
            Errors = errors,
            DurationSeconds = durationSeconds,
            DocumentsPerSecond = processedCount / (double)durationSeconds,
            ChunksPerSecond = totalChunks / (double)durationSeconds
        };
    }
}

public class ProcessingResult
{
    public string FileName { get; set; } = string.Empty;
    public int ChunkCount { get; set; }
    public long ProcessingTimeMs { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public double AverageQuality { get; set; }
}

public class MemoryTestResult
{
    public double InitialMemoryMB { get; set; }
    public double PeakMemoryMB { get; set; }
    public double FinalMemoryMB { get; set; }
    public long ProcessingTimeMs { get; set; }
    public double MemoryUsageMB => PeakMemoryMB - InitialMemoryMB;
}

public class ThroughputResult
{
    public int DocumentsProcessed { get; set; }
    public int ChunksProcessed { get; set; }
    public int Errors { get; set; }
    public int DurationSeconds { get; set; }
    public double DocumentsPerSecond { get; set; }
    public double ChunksPerSecond { get; set; }
    public double ErrorRate => Errors / (double)(DocumentsProcessed + Errors);
}