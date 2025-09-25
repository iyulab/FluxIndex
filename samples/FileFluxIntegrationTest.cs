using FluxIndex.Extensions.FileFlux;
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Samples;

/// <summary>
/// Simple test program for FileFlux integration
/// </summary>
class FileFluxIntegrationTest
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== FluxIndex FileFlux Integration Test ===\n");

        try
        {
            // Setup services
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole());
            services.AddFluxIndex()
                .UseSQLiteVectorStore();

            // Add FileFlux integration
            services.AddFileFluxIntegration(options =>
            {
                options.DefaultChunkingStrategy = "Auto";
                options.DefaultMaxChunkSize = 256;  // Smaller chunks for testing
                options.DefaultOverlapSize = 32;
            });

            var serviceProvider = services.BuildServiceProvider();
            var fileFluxIntegration = serviceProvider.GetRequiredService<FileFluxIntegration>();
            var retriever = serviceProvider.GetRequiredService<Retriever>();

            // Test file processing and indexing
            var testFilePath = Path.GetFullPath("test_document.txt");
            if (!File.Exists(testFilePath))
            {
                Console.WriteLine($"❌ Test file not found: {testFilePath}");
                return;
            }

            Console.WriteLine($"📄 Processing file: {testFilePath}");

            // Process and index the document
            var documentId = await fileFluxIntegration.ProcessAndIndexAsync(testFilePath, new ProcessingOptions
            {
                ChunkingStrategy = "Auto",
                MaxChunkSize = 256,
                OverlapSize = 32
            });

            Console.WriteLine($"✅ Document processed and indexed: {documentId}\n");

            // Test search functionality
            Console.WriteLine("🔍 Testing search...");
            var searchResults = await retriever.SearchAsync("FileFlux integration", 5);

            if (searchResults.Any())
            {
                Console.WriteLine($"Found {searchResults.Count()} results:");
                foreach (var result in searchResults)
                {
                    Console.WriteLine($"  📝 Score: {result.Score:F3} | Content: {result.Content[..Math.Min(100, result.Content.Length)]}...");
                }
            }
            else
            {
                Console.WriteLine("❌ No search results found");
            }

            Console.WriteLine("\n=== FileFlux Integration Test Completed Successfully! ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed with error: {ex.Message}");
            Console.WriteLine($"Details: {ex}");
        }
    }
}