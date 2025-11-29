using System.Text;
using FileFlux;
using FileFlux.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChunkingQualityTest;

class DetailedChunkAnalysis
{
    public static async Task RunAsync(string filePath)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine($"=== Detailed Chunk Analysis ===\n");
        Console.WriteLine($"File: {filePath}\n");

        // Setup DI
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddFileFlux();

        var serviceProvider = services.BuildServiceProvider();
        var processor = serviceProvider.GetRequiredService<IDocumentProcessor>();

        // Test with Intelligent strategy + Korean language profile
        var options = new ChunkingOptions
        {
            Strategy = ChunkingStrategies.Intelligent,
            MaxChunkSize = 1024,
            OverlapSize = 128
        };

        // Apply Korean language setting for better sentence boundary detection
        options.CustomProperties["language"] = "ko";

        Console.WriteLine($"Strategy: {options.Strategy}");
        Console.WriteLine($"MaxChunkSize: {options.MaxChunkSize}");
        Console.WriteLine($"OverlapSize: {options.OverlapSize}\n");
        Console.WriteLine(new string('=', 80));

        var chunks = await processor.ProcessAsync(filePath, options);
        var chunkList = chunks.ToList();

        Console.WriteLine($"\nTotal Chunks: {chunkList.Count}\n");

        for (int i = 0; i < chunkList.Count; i++)
        {
            var chunk = chunkList[i];
            Console.WriteLine($"\n{'=',-80}");
            Console.WriteLine($"CHUNK {i + 1}/{chunkList.Count}");
            Console.WriteLine($"{'=',-80}");
            Console.WriteLine($"Length: {chunk.Content?.Length ?? 0} chars");
            Console.WriteLine($"Metadata: {chunk.Metadata}");
            Console.WriteLine($"{'-',-80}");
            Console.WriteLine(chunk.Content);
            Console.WriteLine($"{'-',-80}");
        }

        // Quality metrics
        Console.WriteLine($"\n\n{'=',-80}");
        Console.WriteLine("QUALITY METRICS");
        Console.WriteLine($"{'=',-80}\n");

        var sizes = chunkList.Select(c => c.Content?.Length ?? 0).ToList();
        Console.WriteLine($"Chunk Count: {chunkList.Count}");
        Console.WriteLine($"Total Characters: {sizes.Sum():N0}");
        Console.WriteLine($"Average Size: {sizes.Average():N0} chars");
        Console.WriteLine($"Min Size: {sizes.Min():N0} chars");
        Console.WriteLine($"Max Size: {sizes.Max():N0} chars");
        Console.WriteLine($"Std Dev: {StdDev(sizes):N0} chars");

        // Size distribution
        Console.WriteLine($"\nSize Distribution:");
        Console.WriteLine($"  < 200 chars:    {sizes.Count(s => s < 200)}");
        Console.WriteLine($"  200-500 chars:  {sizes.Count(s => s >= 200 && s < 500)}");
        Console.WriteLine($"  500-800 chars:  {sizes.Count(s => s >= 500 && s < 800)}");
        Console.WriteLine($"  800-1024 chars: {sizes.Count(s => s >= 800 && s <= 1024)}");
        Console.WriteLine($"  > 1024 chars:   {sizes.Count(s => s > 1024)}");

        // Quality issues
        Console.WriteLine($"\nPotential Quality Issues:");
        for (int i = 0; i < chunkList.Count; i++)
        {
            var content = chunkList[i].Content ?? "";
            var issues = new List<string>();

            if (content.Length < 100)
                issues.Add("Too short (< 100 chars)");
            if (content.Length > 1024)
                issues.Add($"Exceeds max size ({content.Length} > 1024)");
            if (content.StartsWith("-") || content.StartsWith("ㅇ"))
                issues.Add("Starts with bullet/marker (possible mid-section cut)");
            if (content.EndsWith("-") || content.EndsWith(","))
                issues.Add("Ends with incomplete marker");
            if (CountIncompleteLines(content) > 0)
                issues.Add($"Contains {CountIncompleteLines(content)} potentially incomplete lines");

            if (issues.Count > 0)
            {
                Console.WriteLine($"  Chunk {i + 1}: {string.Join(", ", issues)}");
            }
        }
    }

    static double StdDev(List<int> values)
    {
        var avg = values.Average();
        var sumOfSquares = values.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumOfSquares / values.Count);
    }

    static int CountIncompleteLines(string content)
    {
        var lines = content.Split('\n');
        return lines.Count(l => l.TrimEnd().EndsWith(",") || l.TrimEnd().EndsWith("-"));
    }
}
