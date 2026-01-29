using FluxIndex.Extensions.FileVault.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Api.BackgroundServices;

/// <summary>
/// Background service that initializes FileVault watchers on startup.
/// The actual processing is handled by FluxIndex.Extensions.FileVault's VaultBackgroundService.
/// This service only handles Stack-specific initialization.
/// </summary>
public class VaultBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileWatcherService _fileWatcherService;
    private readonly ILogger<VaultBackgroundService> _logger;

    public VaultBackgroundService(
        IServiceScopeFactory scopeFactory,
        IFileWatcherService fileWatcherService,
        ILogger<VaultBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _fileWatcherService = fileWatcherService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Vault background service started (using Extensions.FileVault)");

        try
        {
            // Use a scope to access scoped services like IVault
            using var scope = _scopeFactory.CreateScope();
            var vault = scope.ServiceProvider.GetRequiredService<IVault>();

            // Initialize watchers for all watched folders
            var folders = await vault.GetAllWatchedFoldersAsync(stoppingToken);

            foreach (var folder in folders)
            {
                if (folder.Status == FluxIndex.Extensions.FileVault.Domain.Enums.WatcherStatus.Active)
                {
                    try
                    {
                        await _fileWatcherService.StartWatchingAsync(folder, stoppingToken);
                        _logger.LogInformation("Started watching folder: {Path}", folder.Path);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to start watcher for folder: {Path}", folder.Path);
                    }
                }
            }

            _logger.LogInformation("Initialized {Count} file watchers", folders.Count);

            // Keep the service running until stopped
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in vault background service");
        }
        finally
        {
            // Cleanup on shutdown
            await _fileWatcherService.StopAllAsync();
            _logger.LogInformation("Vault background service stopped");
        }
    }
}
