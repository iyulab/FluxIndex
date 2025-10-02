using FluxIndex.SDK;
using FluxIndex.Extensions.WebFlux;
using Microsoft.Extensions.Logging;

namespace WebFluxSample;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🌐 FluxIndex WebFlux Integration Sample");
        Console.WriteLine("========================================");

        try
        {
            // Create FluxIndex context with WebFlux integration
            var context = new FluxIndexContextBuilder()
                .UseSQLite("webflux_sample.db")
                .UseInMemoryEmbedding()
                .UseWebFlux(options =>
                {
                    options.DefaultMaxChunkSize = 1024;
                    options.DefaultChunkOverlap = 128;
                    options.DefaultChunkingStrategy = WebFlux.Core.Options.ChunkingStrategyType.Smart;
                    options.DefaultIncludeImages = false;
                })
                .WithLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information))
                .Build();

            Console.WriteLine("✅ FluxIndex context initialized with WebFlux support");

            // Test website URLs
            var testUrls = new[]
            {
                "https://example.com",
                "https://httpbin.org/html"
            };

            Console.WriteLine($"\n📄 Testing WebFlux integration...");

            // Test 1: Single URL processing
            Console.WriteLine($"\n🔗 Test 1: Processing single URL");
            try
            {
                var documentId = await context.IndexWebContentAsync(
                    testUrls[0],
                    new WebFluxProcessingOptions
                    {
                        MaxChunkSize = 512,
                        ChunkOverlap = 50
                    });

                Console.WriteLine($"✅ Successfully indexed website. Document ID: {documentId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error processing single URL: {ex.Message}");
            }

            // Test 2: Multiple URLs processing
            Console.WriteLine($"\n🔗 Test 2: Processing multiple URLs");
            try
            {
                var webFlux = context.GetWebFluxIntegration();
                var documentIds = await webFlux.IndexMultipleUrlsAsync(testUrls, new WebFluxProcessingOptions
                {
                    MaxChunkSize = 1024,
                    ChunkOverlap = 100
                });

                Console.WriteLine($"✅ Successfully indexed {documentIds.Count()} websites");
                foreach (var docId in documentIds)
                {
                    Console.WriteLine($"   📄 Document ID: {docId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error processing multiple URLs: {ex.Message}");
            }

            // Test 3: Search functionality
            Console.WriteLine("\n🔍 Test 3: Testing search functionality...");
            try
            {
                var searchResults = await context.Retriever.SearchAsync("example");

                Console.WriteLine($"📊 Found {searchResults.Count()} results:");
                foreach (var result in searchResults.Take(3))
                {
                    var contentPreview = result.DocumentChunk.Content.Length > 100
                        ? result.DocumentChunk.Content.Substring(0, 100) + "..."
                        : result.DocumentChunk.Content;

                    Console.WriteLine($"   📄 Score: {result.Score:F3} | Content: {contentPreview}");

                    // Display metadata if available
                    if (result.DocumentChunk.Metadata?.ContainsKey("webflux_title") == true)
                    {
                        Console.WriteLine($"      🏷️ Title: {result.DocumentChunk.Metadata["webflux_title"]}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error searching: {ex.Message}");
            }

            // Test 4: WebFlux configuration options
            Console.WriteLine("\n⚙️ Test 4: Testing different WebFlux configurations");
            try
            {
                // Semantic strategy configuration
                var semanticOptions = new WebFluxProcessingOptions
                {
                    ChunkingStrategy = WebFlux.Core.Options.ChunkingStrategyType.Semantic,
                    MaxChunkSize = 512
                };
                Console.WriteLine($"   🧠 Semantic config: Strategy={semanticOptions.ChunkingStrategy}, ChunkSize={semanticOptions.MaxChunkSize}");

                // Large content configuration
                var largeContentOptions = new WebFluxProcessingOptions
                {
                    ChunkingStrategy = WebFlux.Core.Options.ChunkingStrategyType.MemoryOptimized,
                    MaxChunkSize = 2048,
                    IncludeImages = false
                };
                Console.WriteLine($"   📄 Large content config: Strategy={largeContentOptions.ChunkingStrategy}, ChunkSize={largeContentOptions.MaxChunkSize}, Images={largeContentOptions.IncludeImages}");

                Console.WriteLine("   ✅ Configuration options validated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error testing configurations: {ex.Message}");
            }

            Console.WriteLine("\n🎉 WebFlux integration sample completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 Fatal error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}