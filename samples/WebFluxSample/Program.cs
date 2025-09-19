using FluxIndex.SDK;
using FluxIndex.Extensions.WebFlux;
using FluxIndex.Extensions.WebFlux.Models;
using Microsoft.Extensions.Logging;
using WebFluxIndexingStatus = FluxIndex.Extensions.WebFlux.IndexingStatus;

namespace WebFluxSample;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🌐 FluxIndex WebFlux Integration Sample");
        Console.WriteLine("========================================");

        try
        {
            // Create FluxIndex client with WebFlux integration
            var client = new FluxIndexClientBuilder()
                .UseSQLiteInMemory()                    // Use SQLite in-memory for testing
                .UseWebFluxWithMockAI()                 // Add WebFlux with mock AI services
                .WithChunking("Auto", 512, 64)          // Configure chunking
                .WithLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                })
                .Build();

            Console.WriteLine("✅ FluxIndex client initialized with WebFlux support");

            // Test website URLs
            var testUrls = new[]
            {
                "https://docs.microsoft.com/en-us/dotnet/",
                "https://github.com/microsoft/semantic-kernel",
                "https://learn.microsoft.com/en-us/azure/"
            };

            // Index a single website with progress tracking
            Console.WriteLine("\\n📄 Indexing single website with progress...");

            try
            {
                await foreach (var progress in client.IndexWebsiteWithProgressAsync(testUrls[0]))
                {
                    Console.WriteLine($"🔄 {progress.Status}: {progress.Message}");

                    if (progress.Status == WebFluxIndexingStatus.Completed)
                    {
                        Console.WriteLine($"✅ Completed! Document ID: {progress.DocumentId}");
                        Console.WriteLine($"📊 Chunks processed: {progress.ChunksProcessed}");
                        Console.WriteLine($"⏱️ Duration: {progress.Duration?.TotalSeconds:F1}s");
                    }
                    else if (progress.Status == WebFluxIndexingStatus.Failed)
                    {
                        Console.WriteLine($"❌ Failed: {progress.Message}");
                        if (progress.Error != null)
                        {
                            Console.WriteLine($"   Error: {progress.Error.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error indexing website: {ex.Message}");
            }

            // Index multiple websites
            Console.WriteLine("\\n📄 Indexing multiple websites...");

            try
            {
                var documentIds = await client.IndexWebsitesAsync(
                    testUrls.Take(2),
                    new CrawlOptions
                    {
                        MaxDepth = 2,
                        MaxPages = 10,
                        DelayBetweenRequests = TimeSpan.FromMilliseconds(1000)
                    },
                    maxConcurrency: 2);

                Console.WriteLine($"✅ Successfully indexed {documentIds.Count()} websites");
                foreach (var docId in documentIds)
                {
                    Console.WriteLine($"   📄 Document ID: {docId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error indexing multiple websites: {ex.Message}");
            }

            // Test search functionality
            Console.WriteLine("\\n🔍 Testing search functionality...");

            try
            {
                var searchResults = await client.SearchAsync("Microsoft Azure cloud services");

                Console.WriteLine($"📊 Found {searchResults.Count} results:");
                foreach (var result in searchResults.Take(3))
                {
                    Console.WriteLine($"   📄 Score: {result.Score:F3} | Content: {result.Content.Substring(0, Math.Min(100, result.Content.Length))}...");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error searching: {ex.Message}");
            }

            Console.WriteLine("\\n🎉 Sample completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 Fatal error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("\\nPress any key to exit...");
        Console.ReadKey();
    }
}