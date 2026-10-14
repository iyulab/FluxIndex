using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// <see cref="HybridSearchOptions.EnableDynamicAlphaTuning"/> runs Dynamic Alpha Tuning in a context the SDK builder
/// built. Before 0.93.0 the builder did not register the tuning service, and the hybrid service's constructors made a
/// container pick the quantizer-only one whenever no tuning service was registered, so the option fell back to the
/// query heuristic without a word — measured on Korean MIRACL, every query reported <c>QueryHeuristic</c>.
/// </summary>
public sealed class DynamicAlphaTuningWiringTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly List<string> _paths = [];

    private static async Task AssertTuningRunsAsync(IFluxIndexContext context)
    {
        await context.Indexer.IndexDocumentAsync("laptop battery drains quickly after the update", "d1", null, Ct);

        using var scope = context.ServiceProvider.CreateScope();
        var hybrid = scope.ServiceProvider.GetRequiredService<IHybridSearchService>();
        var results = await hybrid.SearchAsync(
            "battery update", new HybridSearchOptions { EnableDynamicAlphaTuning = true }, Ct);

        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r => r.Fusion.SelectedBy.Should().Be(FusionSelection.DynamicAlphaTuning));
    }

    [Fact]
    public async Task InMemoryContext_RunsDynamicAlphaTuning()
    {
        await using var context = (FluxIndexContext)FluxIndexContext.CreateBuilder()
            .UseInMemoryEmbedding()
            .SuppressStartupMessages()
            .Build();

        await AssertTuningRunsAsync(context);
    }

    [Fact]
    public async Task SQLiteContext_WithAQuantizerRegistered_RunsDynamicAlphaTuning()
    {
        // SQLite storage registers an IVectorQuantizer: the case where the container used to pick the quantizer-only
        // constructor and drop the tuning service.
        var path = Path.Combine(Path.GetTempPath(), $"fluxindex-dat-{Guid.NewGuid():N}.db");
        _paths.Add(path);
        await using var context = (FluxIndexContext)FluxIndexContext.CreateBuilder()
            .UseSQLite(path)
            .AddSQLiteStorage()
            .UseInMemoryEmbedding()
            .SuppressStartupMessages()
            .Build();

        await AssertTuningRunsAsync(context);
    }

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
