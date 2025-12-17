using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxIndex.SDK.AI.Local.Services;

/// <summary>
/// Background service that warms up the LMSupply embedding model during application startup.
/// Pre-loads the model to avoid cold start latency on first inference.
/// </summary>
public sealed class LMSupplyEmbeddingWarmupService : BackgroundService
{
    private readonly LMSupplyEmbeddingService _embeddingService;
    private readonly ILogger<LMSupplyEmbeddingWarmupService> _logger;

    public LMSupplyEmbeddingWarmupService(
        LMSupplyEmbeddingService embeddingService,
        ILogger<LMSupplyEmbeddingWarmupService> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Starting LMSupply embedding model warmup...");

            await _embeddingService.WarmupAsync(stoppingToken);

            _logger.LogInformation("LMSupply embedding model warmup completed successfully");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("LMSupply embedding warmup was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LMSupply embedding warmup failed: {Message}", ex.Message);
        }
    }
}
