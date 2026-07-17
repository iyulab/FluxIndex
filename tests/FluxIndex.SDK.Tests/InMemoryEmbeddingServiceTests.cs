using FluxIndex.SDK.Services;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Regression teeth for the testing embedder's determinism contract.
/// Found by dogfooding 2026-07-17 (ironhive-umbrella cycle-170/171): the seed used
/// string.GetHashCode(), which .NET randomizes per process — vectors persisted by one
/// process (e.g. into a SQLite store) could never match a later process's query vectors,
/// contradicting the class's own "deterministic" documentation.
/// </summary>
public class InMemoryEmbeddingServiceTests
{
    [Fact]
    public async Task SameText_SameVector_AcrossServiceInstances()
    {
        var a = await new InMemoryEmbeddingService().GenerateEmbeddingAsync("FluxIndex");
        var b = await new InMemoryEmbeddingService().GenerateEmbeddingAsync("FluxIndex");

        Assert.Equal(a, b);
    }

    [Fact]
    public async Task DifferentTexts_DifferentVectors()
    {
        var svc = new InMemoryEmbeddingService();
        var a = await svc.GenerateEmbeddingAsync("FluxIndex");
        var b = await svc.GenerateEmbeddingAsync("FileFlux");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task Vectors_Are_L2Normalized_At_Configured_Dimension()
    {
        var svc = new InMemoryEmbeddingService(dimensions: 128);
        var v = await svc.GenerateEmbeddingAsync("normalization check");

        Assert.Equal(128, v.Length);
        var norm = Math.Sqrt(v.Sum(x => (double)x * x));
        Assert.InRange(norm, 0.999, 1.001);
    }
}
