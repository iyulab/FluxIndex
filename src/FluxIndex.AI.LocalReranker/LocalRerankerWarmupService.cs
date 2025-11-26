using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxIndex.AI.LocalReranker;

/// <summary>
/// Background service that warms up LocalReranker during application startup
/// </summary>
internal sealed class LocalRerankerWarmupService : IHostedService
{
    private readonly LocalRerankerAdapter _reranker;
    private readonly ILogger<LocalRerankerWarmupService> _logger;

    public LocalRerankerWarmupService(
        LocalRerankerAdapter reranker,
        ILogger<LocalRerankerWarmupService> logger)
    {
        _reranker = reranker;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting LocalReranker warmup...");

        try
        {
            await _reranker.WarmupAsync(cancellationToken);
            _logger.LogInformation("LocalReranker warmup completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LocalReranker warmup failed");
            // Don't throw - allow application to start even if warmup fails
            // First inference will trigger lazy initialization
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Nothing to do on shutdown - disposal is handled by DI container
        return Task.CompletedTask;
    }
}
