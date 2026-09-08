using AwesomeAssertions;
using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Providers.LMSupply.Extensions;
using FluxIndex.Providers.LMSupply.Services;
using LMSupply.Embedder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FluxIndex.Providers.LMSupply.Tests;

public sealed class ServiceCollectionExtensionsTests : IDisposable
{
    private readonly string _emptyCache = Path.Combine(Path.GetTempPath(), "fluxindex-lmsupply-nocache-" + Guid.NewGuid().ToString("N"));

    public ServiceCollectionExtensionsTests() => Directory.CreateDirectory(_emptyCache);

    public void Dispose()
    {
        try { Directory.Delete(_emptyCache, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void AddLMSupplyEmbedding_RegistersIEmbeddingService()
    {
        var services = new ServiceCollection();

        services.AddLMSupplyEmbedding("test-model");

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IEmbeddingService) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public async Task AddLMSupplyEmbedding_ResolvingAndReadingIdentity_LoadsNothing()
    {
        // The whole point of lazy registration: a container can be built, the service resolved and the
        // embedding identity (needed by BindIdentity) read without touching the network or the disk.
        var services = new ServiceCollection();
        services.AddLMSupplyEmbedding(o =>
        {
            o.ModelId = "fast";
            o.Embedder = new EmbedderOptions { CacheDirectory = _emptyCache };
        });

        await using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IEmbeddingService>();
        var identity = service.GetIdentity();

        identity.Provider.Should().Be("LMSupply");
        identity.Model.Should().Be("fast");
        identity.Dimension.Should().Be(384, because: "the catalog announces multilingual-e5-small's dimension before any load");
        ((ILazilyLoadedModel)service).IsLoaded.Should().BeFalse();
        Directory.EnumerateFileSystemEntries(_emptyCache).Should().BeEmpty(because: "nothing may be downloaded at resolution time");
    }

    [Fact]
    public async Task AddLMSupplyEmbedding_NonCatalogModel_DimensionIsUnknownUntilLoaded_AndSaysHow()
    {
        var services = new ServiceCollection();
        services.AddLMSupplyEmbedding(o => o.ModelId = "acme/not-in-the-catalog");

        await using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IEmbeddingService>();

        service.GetModelName().Should().Be("acme/not-in-the-catalog", because: "LMSupply uses the id as given for a HuggingFace repo");
        var act = () => service.GetEmbeddingDimension();
        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("EnsureLoadedAsync")
            .And.Contain("WarmUpOnStart")
            .And.Contain("Dimensions");
    }

    [Fact]
    public async Task AddLMSupplyEmbedding_NonCatalogModel_DimensionsOverride_AnnouncesIdentity()
    {
        var services = new ServiceCollection();
        services.AddLMSupplyEmbedding(o =>
        {
            o.ModelId = "acme/not-in-the-catalog";
            o.Dimensions = 768;
            o.Revision = "r2";
        });

        await using var provider = services.BuildServiceProvider();
        var identity = provider.GetRequiredService<IEmbeddingService>().GetIdentity();

        identity.Model.Should().Be("acme/not-in-the-catalog");
        identity.Dimension.Should().Be(768, because: "the registry's placeholder 384 must not leak; the override is announced instead");
        identity.Revision.Should().Be("r2");
    }

    [Fact]
    public void AddLMSupplyEmbedding_WarmUpOnStart_RegistersOneHostedService()
    {
        var services = new ServiceCollection();
        services.AddLMSupplyEmbedding(o => o.WarmUpOnStart = true);

        services.Count(sd => sd.ServiceType == typeof(IHostedService)).Should().Be(1);
    }

    [Fact]
    public void AddLMSupplyEmbedding_Default_RegistersNoHostedService()
    {
        var services = new ServiceCollection();
        services.AddLMSupplyEmbedding();

        services.Should().NotContain(sd => sd.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public async Task AddLMSupplyReranker_RegistersIReranker_AndResolvesWithoutLoading()
    {
        var services = new ServiceCollection();

        services.AddLMSupplyReranker("test-model");

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IReranker) &&
            sd.Lifetime == ServiceLifetime.Singleton);

        await using var provider = services.BuildServiceProvider();
        var reranker = provider.GetRequiredService<IReranker>();
        ((ILazilyLoadedModel)reranker).IsLoaded.Should().BeFalse();
        reranker.GetModelInfo().Name.Should().Be("test-model");
    }

    [Fact]
    public async Task AddLMSupplyTextCompletion_RegistersITextCompletionService_AndResolvesWithoutLoading()
    {
        var services = new ServiceCollection();

        services.AddLMSupplyTextCompletion("test-model");

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ITextCompletionService) &&
            sd.Lifetime == ServiceLifetime.Singleton);

        await using var provider = services.BuildServiceProvider();
        var completion = provider.GetRequiredService<ITextCompletionService>();
        ((ILazilyLoadedModel)completion).IsLoaded.Should().BeFalse();
    }

    [Fact]
    public void AddLMSupplyEmbedding_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddLMSupplyEmbedding();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddLMSupplyReranker_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddLMSupplyReranker();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddLMSupplyTextCompletion_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddLMSupplyTextCompletion();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void ProviderSources_ContainNoSyncOverAsync()
    {
        // Teeth for the defect class this package had: a DI factory blocking on CreateAsync(...)
        // .GetAwaiter().GetResult(). The lazy handle removes the need structurally; keep it removed.
        var root = FindRepositoryRoot();
        var sources = Directory.EnumerateFiles(Path.Combine(root, "src", "FluxIndex.Providers.LMSupply"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

        sources.Should().NotBeEmpty();
        var offenders = sources
            .Where(f => File.ReadAllText(f).Contains(".GetAwaiter().GetResult()", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        offenders.Should().BeEmpty(because: "model loads must stay asynchronous (LazyModelHandle), never block a DI factory");
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FluxIndex.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("FluxIndex.slnx not found above the test directory");
    }
}
