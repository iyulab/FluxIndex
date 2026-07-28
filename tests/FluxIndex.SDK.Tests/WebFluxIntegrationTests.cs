using FluxIndex.SDK;
using FluxIndex.Integrations.WebFlux;
using FluxIndex.Storage.SQLite;
using WebFlux.Core.Models.Events;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Tests for the WebFlux integration wrapper surface (event exposure).
/// </summary>
public class WebFluxIntegrationTests : IDisposable
{
    private readonly IFluxIndexContext _context;

    public WebFluxIntegrationTests()
    {
        _context = FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .AddSQLiteStorage()
            .UseInMemoryEmbedding()
            .UseWebFlux()
            .Build();
    }

    public void Dispose()
    {
        if (_context is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    [Fact]
    public void GetWebFluxIntegration_ExposesEventPublisher()
    {
        // Regression guard: the wrapper used to swallow WebFlux's IEventPublisher entirely,
        // making crawl-progress subscription impossible through the SDK path.
        var integration = _context.GetWebFluxIntegration();

        Assert.NotNull(integration.Events);
    }

    [Fact]
    public async Task Events_SubscriptionReceivesPublishedEvents()
    {
        var integration = _context.GetWebFluxIntegration();
        Assert.NotNull(integration.Events);

        PageCrawledEvent? received = null;
        using var subscription = integration.Events!.Subscribe<PageCrawledEvent>(e =>
        {
            received = e;
            return Task.CompletedTask;
        });

        await integration.Events!.PublishAsync(new PageCrawledEvent
        {
            Url = "https://example.test/page",
            StatusCode = 200
        });

        Assert.NotNull(received);
        Assert.Equal("https://example.test/page", received!.Url);
    }
}
