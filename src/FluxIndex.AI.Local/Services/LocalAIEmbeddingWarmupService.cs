using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxIndex.AI.Local.Services;

/// <summary>
/// Background service that warms up the LocalAI embedding model during application startup.
/// Pre-loads the model to avoid cold start latency on first inference.
/// </summary>
public sealed class LocalAIEmbeddingWarmupService : BackgroundService
{
    private readonly LocalAIEmbeddingService _embeddingService;
    private readonly ILogger<LocalAIEmbeddingWarmupService> _logger;

    public LocalAIEmbeddingWarmupService(
        LocalAIEmbeddingService embeddingService,
        ILogger<LocalAIEmbeddingWarmupService> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Starting LocalAI embedding model warmup...");

            await _embeddingService.WarmupAsync(stoppingToken);

            _logger.LogInformation("LocalAI embedding model warmup completed successfully");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("LocalAI embedding warmup was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LocalAI embedding warmup failed: {Message}", ex.Message);
        }
    }
}
