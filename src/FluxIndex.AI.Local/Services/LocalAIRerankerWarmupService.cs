using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxIndex.AI.Local.Services;

/// <summary>
/// Background service that warms up the LocalAI reranker model during application startup.
/// Pre-loads the model to avoid cold start latency on first inference.
/// </summary>
public sealed class LocalAIRerankerWarmupService : BackgroundService
{
    private readonly LocalAIRerankerAdapter _rerankerAdapter;
    private readonly ILogger<LocalAIRerankerWarmupService> _logger;

    public LocalAIRerankerWarmupService(
        LocalAIRerankerAdapter rerankerAdapter,
        ILogger<LocalAIRerankerWarmupService> logger)
    {
        _rerankerAdapter = rerankerAdapter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Starting LocalAI reranker model warmup...");

            await _rerankerAdapter.WarmupAsync(stoppingToken);

            _logger.LogInformation("LocalAI reranker model warmup completed successfully");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("LocalAI reranker warmup was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LocalAI reranker warmup failed: {Message}", ex.Message);
        }
    }
}
