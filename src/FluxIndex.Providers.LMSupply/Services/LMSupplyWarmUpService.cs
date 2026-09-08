using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Providers.LMSupply.Services;

/// <summary>
/// Loads one lazily registered LMSupply service at host start (<c>WarmUpOnStart</c>), so the first
/// request does not pay the download and a load failure surfaces as a startup failure.
/// </summary>
internal sealed partial class LMSupplyWarmUpService : IHostedService
{
    private readonly string _serviceName;
    private readonly Func<ILazilyLoadedModel?> _resolve;
    private readonly ILogger _logger;

    public LMSupplyWarmUpService(string serviceName, Func<ILazilyLoadedModel?> resolve, ILogger logger)
    {
        _serviceName = serviceName;
        _resolve = resolve;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // The registration may have been replaced by a consumer with a non-LMSupply service; then
        // there is nothing to warm up.
        if (_resolve() is not { } lazy)
            return;

        LogWarmUpStarting(_logger, _serviceName);
        await lazy.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        LogWarmUpCompleted(_logger, _serviceName);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Warming up LMSupply {Service} at host start")]
    private static partial void LogWarmUpStarting(ILogger logger, string service);

    [LoggerMessage(Level = LogLevel.Information, Message = "LMSupply {Service} warm-up completed")]
    private static partial void LogWarmUpCompleted(ILogger logger, string service);
}
