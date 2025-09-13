using FluxIndex;

namespace FileFluxIndexSample;

class ProgramMinimal
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== FluxIndex Minimal Package Test ===");
        
        var client = new FluxIndexClient();
        
        // Test indexing
        Console.WriteLine("\n1. Testing document indexing...");
        var docId = await client.IndexDocumentAsync("This is a test document content", "test-doc-001");
        Console.WriteLine($"   ✓ Indexed document ID: {docId}");
        
        // Test search
        Console.WriteLine("\n2. Testing document search...");
        var results = await client.SearchAsync("test", 5);
        foreach (var result in results)
        {
            Console.WriteLine($"   ✓ Found: {result.DocumentId}");
            Console.WriteLine($"     Content: {result.Content}");
            Console.WriteLine($"     Score: {result.Score:F2}");
        }
        
        Console.WriteLine("\n=== Test completed successfully! ===");
        Console.WriteLine("FluxIndex package is working correctly from local NuGet source.");
    }
}