using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxIndex.SDK.AI.Local.Services;

/// <summary>
/// Background service that warms up the LMSupply reranker model during application startup.
/// Pre-loads the model to avoid cold start latency on first inference.
/// </summary>
public sealed class LMSupplyRerankerWarmupService : BackgroundService
{
    private readonly LMSupplyRerankerAdapter _rerankerAdapter;
    private readonly ILogger<LMSupplyRerankerWarmupService> _logger;

    public LMSupplyRerankerWarmupService(
        LMSupplyRerankerAdapter rerankerAdapter,
        ILogger<LMSupplyRerankerWarmupService> logger)
    {
        _rerankerAdapter = rerankerAdapter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Starting LMSupply reranker model warmup...");

            await _rerankerAdapter.WarmupAsync(stoppingToken);

            _logger.LogInformation("LMSupply reranker model warmup completed successfully");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("LMSupply reranker warmup was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LMSupply reranker warmup failed: {Message}", ex.Message);
        }
    }
}
