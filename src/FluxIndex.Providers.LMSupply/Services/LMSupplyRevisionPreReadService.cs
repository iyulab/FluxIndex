using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Providers.LMSupply.Services;

/// <summary>
/// At host start, reads the embedding model's vector-space revision from the cached files so an identity with
/// <c>UseVectorSpaceRevision</c> can be read before the model loads (LMSupply 0.72.0). Loads the model only when the
/// files cannot answer — the behaviour <c>UseVectorSpaceRevision</c> had before the files-only read existed.
/// </summary>
internal sealed partial class LMSupplyRevisionPreReadService : IHostedService
{
    private readonly Func<LMSupplyEmbeddingService?> _resolve;
    private readonly ILogger _logger;

    public LMSupplyRevisionPreReadService(Func<LMSupplyEmbeddingService?> resolve, ILogger logger)
    {
        _resolve = resolve;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // A consumer may have replaced the registration with a non-LMSupply service; then there is nothing to read.
        if (_resolve() is not { } service)
            return;

        var revision = await service.PreReadVectorSpaceRevisionAsync(cancellationToken).ConfigureAwait(false);
        if (revision is not null)
        {
            LogPreRead(_logger, revision);
            return;
        }

        LogPreReadUnavailable(_logger);
        await service.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "LMSupply embedding vector-space revision {Revision} read from the cached model files; the model loads on first use")]
    private static partial void LogPreRead(ILogger logger, string revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "LMSupply embedding vector-space revision is not readable from the cached files; loading the model at host start")]
    private static partial void LogPreReadUnavailable(ILogger logger);
}
