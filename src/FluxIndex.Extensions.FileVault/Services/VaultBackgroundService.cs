using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// Background service for processing vault queue items.
/// </summary>
public sealed class VaultBackgroundService : BackgroundService
{
    private readonly ILogger<VaultBackgroundService> _logger;
    private readonly IVaultQueueService _queueService;
    private readonly IVault _vault;
    private readonly FileVaultOptions _options;
    private readonly SemaphoreSlim _concurrencyLimiter;

    public VaultBackgroundService(
        ILogger<VaultBackgroundService> logger,
        IVaultQueueService queueService,
        IVault vault,
        IOptions<FileVaultOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _options = options?.Value ?? new FileVaultOptions();
        _concurrencyLimiter = new SemaphoreSlim(_options.MaxConcurrentProcessing);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Vault background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var item = await _queueService.DequeueAsync(stoppingToken);

                if (item == null)
                {
                    // No items to process, wait before checking again
                    await Task.Delay(_options.QueuePollingIntervalMs, stoppingToken);
                    continue;
                }

                // Process item with concurrency limit
                await _concurrencyLimiter.WaitAsync(stoppingToken);

                _ = ProcessItemAsync(item, stoppingToken)
                    .ContinueWith(_ => _concurrencyLimiter.Release(), TaskScheduler.Default);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in vault background service loop");
                await Task.Delay(1000, stoppingToken); // Brief pause on error
            }
        }

        _logger.LogInformation("Vault background service stopped");
    }

    private async Task ProcessItemAsync(QueuedItem item, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Processing queue item {ItemId}: {FilePath}", item.Id, item.FilePath);

            // Process based on the stage
            await _vault.ProcessAsync(item.FilePath, null, ct);

            await _queueService.CompleteAsync(item.Id, ct);

            _logger.LogDebug("Completed queue item {ItemId}", item.Id);
        }
        catch (FileNotFoundException)
        {
            await _queueService.FailAsync(item.Id, "File not found", null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process queue item {ItemId}: {FilePath}", item.Id, item.FilePath);

            await _queueService.FailAsync(item.Id, ex.Message, ex, ct);

            // Auto-retry if enabled and under max retries
            if (_options.EnableAutoRetry && item.RetryCount < _options.MaxRetryCount)
            {
                await Task.Delay(_options.RetryDelayMs, ct);
                await _queueService.RetryAsync(item.Id, ct);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Vault background service stopping...");
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _concurrencyLimiter.Dispose();
        base.Dispose();
    }
}
