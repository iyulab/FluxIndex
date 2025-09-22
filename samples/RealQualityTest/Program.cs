using FluxIndex.SDK;
using Microsoft.Extensions.Logging;

namespace RealQualityTest;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🧪 FluxIndex Quality Test Sample");
        Console.WriteLine("================================");

        try
        {
            // Create FluxIndex context
            var context = new FluxIndexContextBuilder()
                .UseSQLite("quality_test.db")
                .UseInMemoryEmbedding()
                .WithLogging(builder => builder.AddConsole())
                .Build();

            Console.WriteLine("✅ FluxIndex context initialized");

            // Create sample documents
            var sampleDocuments = new[]
            {
                new FluxIndex.Domain.Entities.Document
                {
                    Id = "doc1",
                    FileName = "sample1.txt",
                    Content = "This is a sample document about artificial intelligence and machine learning technologies.",
                    Metadata = new Dictionary<string, object> { {"category", "technology"} }
                },
                new FluxIndex.Domain.Entities.Document
                {
                    Id = "doc2",
                    FileName = "sample2.txt",
                    Content = "FluxIndex is a powerful RAG infrastructure library for building search applications.",
                    Metadata = new Dictionary<string, object> { {"category", "software"} }
                },
                new FluxIndex.Domain.Entities.Document
                {
                    Id = "doc3",
                    FileName = "sample3.txt",
                    Content = "Vector databases enable semantic search capabilities using embedding vectors.",
                    Metadata = new Dictionary<string, object> { {"category", "database"} }
                }
            };

            // Index documents
            Console.WriteLine("\n📄 Indexing sample documents...");
            foreach (var doc in sampleDocuments)
            {
                await context.Indexer.IndexDocumentAsync(doc);
                Console.WriteLine($"   ✅ Indexed: {doc.FileName}");
            }

            // Test search functionality
            Console.WriteLine("\n🔍 Testing search functionality...");
            var queries = new[] { "artificial intelligence", "vector search", "FluxIndex" };

            foreach (var query in queries)
            {
                Console.WriteLine($"\n   Query: \"{query}\"");
                var results = await context.Retriever.SearchAsync(query);

                Console.WriteLine($"   📊 Found {results.Count()} results:");
                foreach (var result in results.Take(2))
                {
                    Console.WriteLine($"      📄 Score: {result.Score:F3} | Content: {result.DocumentChunk.Content.Substring(0, Math.Min(80, result.DocumentChunk.Content.Length))}...");
                }
            }

            Console.WriteLine("\n🎉 Quality test completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}