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
public partial class VaultBackgroundService : BackgroundService
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
        LogVaultBackgroundServiceStarted(_logger);

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
                        LogStartedWatchingFolder(_logger, folder.Path);
                    }
                    catch (Exception ex)
                    {
                        LogStartWatcherFailed(_logger, ex, folder.Path);
                    }
                }
            }

            var watcherCount = folders.Count;
            LogInitializedFileWatchers(_logger, watcherCount);

            // Keep the service running until stopped
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            LogVaultBackgroundServiceError(_logger, ex);
        }
        finally
        {
            // Cleanup on shutdown
            await _fileWatcherService.StopAllAsync(CancellationToken.None);
            LogVaultBackgroundServiceStopped(_logger);
        }
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Vault background service started (using Extensions.FileVault)")]
    private static partial void LogVaultBackgroundServiceStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started watching folder: {Path}")]
    private static partial void LogStartedWatchingFolder(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to start watcher for folder: {Path}")]
    private static partial void LogStartWatcherFailed(ILogger logger, Exception? exception, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Initialized {Count} file watchers")]
    private static partial void LogInitializedFileWatchers(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error in vault background service")]
    private static partial void LogVaultBackgroundServiceError(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Vault background service stopped")]
    private static partial void LogVaultBackgroundServiceStopped(ILogger logger);

    #endregion
}
