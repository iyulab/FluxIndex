using System.Diagnostics;
using System.Text;
using FileFlux;
using FileFlux.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChunkingQualityTest;

class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Run detailed analysis for a specific file
        if (args.Length > 0 && args[0] == "--detailed")
        {
            var targetFile = args.Length > 1 ? args[1] : @"D:\test-data\한국자동기술산업.txt";
            await DetailedChunkAnalysis.RunAsync(targetFile);
            return;
        }

        Console.WriteLine("=== FluxIndex Intelligent Chunking Quality Test ===\n");

        var testDataPath = args.Length > 0 ? args[0] : @"D:\test-data";

        if (!Directory.Exists(testDataPath))
        {
            Console.WriteLine($"Test data directory not found: {testDataPath}");
            return;
        }

        // Setup DI
        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .AddConsole()
            .SetMinimumLevel(LogLevel.Warning));
        services.AddFileFlux();

        var serviceProvider = services.BuildServiceProvider();
        var processor = serviceProvider.GetRequiredService<IDocumentProcessor>();

        // Get all test files
        var supportedExtensions = new[] { ".pdf", ".docx", ".txt", ".md", ".html" };
        var testFiles = Directory.GetFiles(testDataPath)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLower()))
            .ToList();

        Console.WriteLine($"Found {testFiles.Count} test files:\n");
        foreach (var file in testFiles)
        {
            var fileInfo = new FileInfo(file);
            Console.WriteLine($"  - {fileInfo.Name} ({FormatFileSize(fileInfo.Length)})");
        }
        Console.WriteLine();

        // Test each chunking strategy
        var strategies = new[]
        {
            (Name: "Intelligent", Strategy: ChunkingStrategies.Intelligent),
            (Name: "Semantic", Strategy: ChunkingStrategies.Semantic),
            (Name: "Smart", Strategy: ChunkingStrategies.Smart),
            (Name: "Auto", Strategy: ChunkingStrategies.Auto),
        };

        var results = new List<ChunkingResult>();

        foreach (var file in testFiles)
        {
            var fileName = Path.GetFileName(file);
            Console.WriteLine($"\n{'=',-60}");
            Console.WriteLine($"Processing: {fileName}");
            Console.WriteLine($"{'=',-60}");

            foreach (var (strategyName, strategy) in strategies)
            {
                try
                {
                    var result = await ProcessFileWithStrategy(processor, file, strategy, strategyName);
                    results.Add(result);
                    PrintStrategyResult(result);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [{strategyName}] ERROR: {ex.Message}");
                }
            }
        }

        // Print summary
        PrintSummary(results);
    }

    static async Task<ChunkingResult> ProcessFileWithStrategy(
        IDocumentProcessor processor,
        string filePath,
        string strategy,
        string strategyName)
    {
        var options = new ChunkingOptions
        {
            Strategy = strategy,
            MaxChunkSize = 1024,
            OverlapSize = 128
        };

        var sw = Stopwatch.StartNew();
        var chunks = await processor.ProcessAsync(filePath, options);
        sw.Stop();

        var chunkList = chunks.ToList();

        return new ChunkingResult
        {
            FileName = Path.GetFileName(filePath),
            Strategy = strategyName,
            ChunkCount = chunkList.Count,
            TotalCharacters = chunkList.Sum(c => c.Content?.Length ?? 0),
            AvgChunkSize = chunkList.Count > 0 ? chunkList.Average(c => c.Content?.Length ?? 0) : 0,
            MinChunkSize = chunkList.Count > 0 ? chunkList.Min(c => c.Content?.Length ?? 0) : 0,
            MaxChunkSize = chunkList.Count > 0 ? chunkList.Max(c => c.Content?.Length ?? 0) : 0,
            ProcessingTimeMs = sw.ElapsedMilliseconds,
            Chunks = chunkList
        };
    }

    static void PrintStrategyResult(ChunkingResult result)
    {
        Console.WriteLine($"\n  [{result.Strategy}]");
        Console.WriteLine($"    Chunks: {result.ChunkCount}");
        Console.WriteLine($"    Total chars: {result.TotalCharacters:N0}");
        Console.WriteLine($"    Avg size: {result.AvgChunkSize:N0} chars");
        Console.WriteLine($"    Min/Max: {result.MinChunkSize:N0} / {result.MaxChunkSize:N0} chars");
        Console.WriteLine($"    Time: {result.ProcessingTimeMs}ms");

        // Show first 3 chunks preview
        if (result.Chunks.Count > 0)
        {
            Console.WriteLine($"    --- Sample chunks ---");
            for (int i = 0; i < Math.Min(3, result.Chunks.Count); i++)
            {
                var chunk = result.Chunks[i];
                var preview = (chunk.Content ?? "").Replace("\n", " ").Replace("\r", "");
                if (preview.Length > 100) preview = preview.Substring(0, 100) + "...";
                Console.WriteLine($"    [{i+1}] {preview}");
            }
        }
    }

    static void PrintSummary(List<ChunkingResult> results)
    {
        Console.WriteLine($"\n\n{'=',-80}");
        Console.WriteLine("SUMMARY - Chunking Quality Analysis");
        Console.WriteLine($"{'=',-80}\n");

        var groupedByFile = results.GroupBy(r => r.FileName);

        Console.WriteLine($"{"File",-45} {"Strategy",-12} {"Chunks",8} {"Avg Size",10} {"Time(ms)",10}");
        Console.WriteLine(new string('-', 90));

        foreach (var fileGroup in groupedByFile)
        {
            foreach (var result in fileGroup)
            {
                Console.WriteLine($"{result.FileName,-45} {result.Strategy,-12} {result.ChunkCount,8} {result.AvgChunkSize,10:N0} {result.ProcessingTimeMs,10}");
            }
            Console.WriteLine();
        }

        // Best strategy analysis
        Console.WriteLine("\nBest Strategy per File (by chunk quality):");
        Console.WriteLine(new string('-', 60));

        foreach (var fileGroup in groupedByFile)
        {
            // Prefer strategy with reasonable chunk count and good average size
            var best = fileGroup
                .Where(r => r.ChunkCount > 0)
                .OrderByDescending(r =>
                {
                    // Score: prefer 300-800 char chunks, penalize too small or too large
                    var avgScore = r.AvgChunkSize >= 300 && r.AvgChunkSize <= 800 ? 100 :
                                   r.AvgChunkSize >= 200 && r.AvgChunkSize <= 1000 ? 70 : 40;
                    // Penalize extreme variance
                    var variance = r.MaxChunkSize - r.MinChunkSize;
                    var varianceScore = variance < 500 ? 100 : variance < 1000 ? 70 : 40;
                    return avgScore + varianceScore;
                })
                .FirstOrDefault();

            if (best != null)
            {
                Console.WriteLine($"  {best.FileName}: {best.Strategy} ({best.ChunkCount} chunks, avg {best.AvgChunkSize:N0} chars)");
            }
        }
    }

    static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

class ChunkingResult
{
    public string FileName { get; set; } = "";
    public string Strategy { get; set; } = "";
    public int ChunkCount { get; set; }
    public int TotalCharacters { get; set; }
    public double AvgChunkSize { get; set; }
    public int MinChunkSize { get; set; }
    public int MaxChunkSize { get; set; }
    public long ProcessingTimeMs { get; set; }
    public List<DocumentChunk> Chunks { get; set; } = new();
}
