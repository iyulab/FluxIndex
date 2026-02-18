using FluxIndex.Stack.Application.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Api.BackgroundServices;

/// <summary>
/// Background service that continuously processes pending indexing jobs.
/// </summary>
public partial class IndexingBackgroundService : BackgroundService
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
        LogIndexingBackgroundServiceStarted(_logger);

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
                    LogIndexingServiceNotRegistered(_logger);
                    await Task.Delay(_idleInterval, stoppingToken);
                    continue;
                }

                // Get job summary to check if there's work to do
                var summary = await indexingService.GetStatusSummaryAsync(stoppingToken);

                if (summary.QueuedCount > 0)
                {
                    LogProcessingNextJob(_logger, summary.QueuedCount);
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
                LogIndexingBackgroundServiceError(_logger, ex);
                await Task.Delay(_idleInterval, stoppingToken);
            }
        }

        LogIndexingBackgroundServiceStopped(_logger);
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
                LogIndexingServiceNotAvailableForRecovery(_logger);
                return;
            }

            var recoveredCount = await indexingService.RecoverStuckJobsAsync(stoppingToken);
            if (recoveredCount > 0)
            {
                LogRecoveredStuckJobs(_logger, recoveredCount);
            }
        }
        catch (Exception ex)
        {
            LogRecoverStuckJobsError(_logger, ex);
        }
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexing background service started")]
    private static partial void LogIndexingBackgroundServiceStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "IIndexingService not registered, waiting...")]
    private static partial void LogIndexingServiceNotRegistered(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing next job ({QueuedCount} queued)")]
    private static partial void LogProcessingNextJob(ILogger logger, int queuedCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error in indexing background service")]
    private static partial void LogIndexingBackgroundServiceError(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexing background service stopped")]
    private static partial void LogIndexingBackgroundServiceStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "IIndexingService not available for stuck job recovery")]
    private static partial void LogIndexingServiceNotAvailableForRecovery(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recovered {Count} stuck jobs from Processing state")]
    private static partial void LogRecoveredStuckJobs(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error recovering stuck jobs on startup")]
    private static partial void LogRecoverStuckJobsError(ILogger logger, Exception? exception);

    #endregion
}
