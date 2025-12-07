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
}
