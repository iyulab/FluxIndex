using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluxIndex.Stack.Api.Tests;

/// <summary>
/// Custom WebApplicationFactory for integration testing.
/// Configures in-memory database and test services.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registrations
            var descriptorsToRemove = services
                .Where(d => d.ServiceType.Name.Contains("DbContext") ||
                           d.ServiceType.Name.Contains("IDbContextFactory"))
                .ToList();

            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            // Add in-memory database for testing
            services.AddDbContext<TestDbContext>(options =>
            {
                options.UseInMemoryDatabase($"FluxIndexTestDb_{Guid.NewGuid()}");
            });

            // Configure test API key validation
            services.AddSingleton<ITestApiKeyValidator, TestApiKeyValidator>();

            // Configure test embedding provider (mock)
            ConfigureTestEmbeddingProvider(services);

            // Build service provider to ensure configuration is valid
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetService<TestDbContext>();
            db?.Database.EnsureCreated();
        });
    }

    private static void ConfigureTestEmbeddingProvider(IServiceCollection services)
    {
        // Remove real embedding provider
        var embeddingDescriptor = services
            .FirstOrDefault(d => d.ServiceType.Name.Contains("IEmbeddingProvider"));

        if (embeddingDescriptor != null)
        {
            services.Remove(embeddingDescriptor);
        }

        // Add mock embedding provider for testing
        services.AddSingleton<FluxIndex.Stack.Application.Interfaces.Services.IEmbeddingProvider, MockEmbeddingProvider>();
    }
}

/// <summary>
/// Mock embedding provider for testing.
/// Returns consistent embeddings without requiring actual ML models.
/// </summary>
public class MockEmbeddingProvider : FluxIndex.Stack.Application.Interfaces.Services.IEmbeddingProvider
{
    private const int Dimension = 384;

    public int EmbeddingDimension => Dimension;

    public string ModelName => "mock-embedding-model";

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        // Generate deterministic embedding based on text hash
        var embedding = new float[Dimension];
        var hash = text.GetHashCode();
        var random = new Random(hash);

        for (int i = 0; i < Dimension; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        }

        // Normalize
        var magnitude = MathF.Sqrt(embedding.Sum(x => x * x));
        for (int i = 0; i < Dimension; i++)
        {
            embedding[i] /= magnitude;
        }

        return Task.FromResult(embedding);
    }

    public async Task<float[][]> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var results = new List<float[]>();
        foreach (var text in texts)
        {
            results.Add(await GetEmbeddingAsync(text, cancellationToken));
        }
        return results.ToArray();
    }
}

/// <summary>
/// Test API key validator interface.
/// </summary>
public interface ITestApiKeyValidator
{
    bool ValidateApiKey(string apiKey);
    string GetRole(string apiKey);
}

/// <summary>
/// Test API key validator implementation.
/// </summary>
public class TestApiKeyValidator : ITestApiKeyValidator
{
    private readonly Dictionary<string, string> _testKeys = new()
    {
        { "test-api-key-admin", "Admin" },
        { "test-api-key-writer", "Writer" },
        { "test-api-key-reader", "Reader" }
    };

    public bool ValidateApiKey(string apiKey)
    {
        return _testKeys.ContainsKey(apiKey);
    }

    public string GetRole(string apiKey)
    {
        return _testKeys.TryGetValue(apiKey, out var role) ? role : "Reader";
    }
}

/// <summary>
/// Test DbContext placeholder.
/// In actual implementation, this would be the real DbContext with in-memory provider.
/// </summary>
public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    // Add DbSets as needed for testing
}
