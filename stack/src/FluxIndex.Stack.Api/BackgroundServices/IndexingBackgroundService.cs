using FluxIndex.Stack.Application.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Api.BackgroundServices;

/// <summary>
/// Background service that continuously processes pending indexing jobs.
/// </summary>
public class IndexingBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IndexingBackgroundService> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _idleInterval = TimeSpan.FromSeconds(10);

    public IndexingBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<IndexingBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Indexing background service started");

        // Recover any jobs stuck in Processing state (from previous server shutdown)
        await RecoverStuckJobsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var indexingService = scope.ServiceProvider.GetService<IIndexingService>();

                if (indexingService == null)
                {
                    _logger.LogWarning("IIndexingService not registered, waiting...");
                    await Task.Delay(_idleInterval, stoppingToken);
                    continue;
                }

                // Get job summary to check if there's work to do
                var summary = await indexingService.GetStatusSummaryAsync(stoppingToken);

                if (summary.QueuedCount > 0)
                {
                    _logger.LogDebug("Processing next job ({QueuedCount} queued)", summary.QueuedCount);
                    await indexingService.ProcessNextJobAsync(stoppingToken);
                    await Task.Delay(_pollingInterval, stoppingToken);
                }
                else
                {
                    // No jobs queued, wait longer before checking again
                    await Task.Delay(_idleInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected on shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in indexing background service");
                await Task.Delay(_idleInterval, stoppingToken);
            }
        }

        _logger.LogInformation("Indexing background service stopped");
    }

    /// <summary>
    /// Recovers jobs that were left in Processing state from a previous server shutdown.
    /// These jobs are reset to Queued so they can be reprocessed.
    /// </summary>
    private async Task RecoverStuckJobsAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var indexingService = scope.ServiceProvider.GetService<IIndexingService>();

            if (indexingService == null)
            {
                _logger.LogWarning("IIndexingService not available for stuck job recovery");
                return;
            }

            var recoveredCount = await indexingService.RecoverStuckJobsAsync(stoppingToken);
            if (recoveredCount > 0)
            {
                _logger.LogWarning("Recovered {Count} stuck jobs from Processing state", recoveredCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recovering stuck jobs on startup");
        }
    }
}
