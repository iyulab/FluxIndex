using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxIndex.SDK.Services;

/// <summary>
/// Reports, once per process, which AI services a built context has and how to add the missing ones.
/// </summary>
/// <remarks>
/// The report goes to the context's <see cref="ILogger"/> (category <c>FluxIndex.SDK.Services.StartupMessageService</c>)
/// — never to the console. Before 0.66.0 it was written to standard output, which corrupts the output of a host that
/// uses standard output as its protocol (a sidecar announcing readiness on stdout, an MCP stdio server).
/// Turn it off with <see cref="FluxIndexContextBuilder.SuppressStartupMessages"/> or a log filter on the category.
/// </remarks>
public static partial class StartupMessageService
{
    private static bool _messageDisplayed;

    /// <summary>
    /// Logs the AI service status of the built context and the LMSupply registrations that would add what is missing.
    /// Only the first call in a process logs.
    /// </summary>
    /// <param name="serviceProvider">The built service provider to check registrations</param>
    /// <param name="embeddingProvider">The configured embedding provider name</param>
    /// <param name="vectorStoreProvider">The vector store provider in effect; "InMemory" logs the data-loss warning</param>
    public static void DisplayAIServiceGuidance(IServiceProvider serviceProvider, string? embeddingProvider, string? vectorStoreProvider = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        if (_messageDisplayed) return;
        _messageDisplayed = true;

        var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(StartupMessageService).FullName!)
            ?? NullLogger.Instance;

        if (string.Equals(vectorStoreProvider, "InMemory", StringComparison.OrdinalIgnoreCase))
            LogInMemoryStorage(logger);

        var hasEmbedding = serviceProvider.GetService<IEmbeddingService>() != null;
        var hasTextCompletion = serviceProvider.GetService<ITextCompletionService>() != null;
        var hasReranker = serviceProvider.GetService<IReranker>() != null;
        var hasContextualEnrichment = serviceProvider.GetService<IContextualEnrichmentService>() != null;

        var embedding = hasEmbedding ? GetEmbeddingDescription(embeddingProvider) : "none";
        var textCompletion = Status(hasTextCompletion);
        var reranking = Status(hasReranker);
        var contextualEnrichment = Status(hasContextualEnrichment);
        LogServiceStatus(logger, embedding, textCompletion, reranking, contextualEnrichment);

        var isInMemoryEmbedding = string.Equals(embeddingProvider, "InMemory", StringComparison.OrdinalIgnoreCase);
        if (!isInMemoryEmbedding && (!hasTextCompletion || !hasReranker))
        {
            var registrations = string.Join(
                ", ",
                new[]
                {
                    hasTextCompletion ? null : "services.AddLMSupplyTextCompletion() (HyDE query expansion, metadata enrichment)",
                    hasReranker ? null : "services.AddLMSupplyReranker() (cross-encoder reranking)",
                }.Where(s => s is not null));
            LogLocalServicesHint(logger, registrations);
        }
    }

    /// <summary>
    /// Suppress the startup message (for testing or production environments).
    /// </summary>
    public static void Suppress()
    {
        _messageDisplayed = true;
    }

    /// <summary>
    /// Reset the message display state (for testing).
    /// </summary>
    public static void Reset()
    {
        _messageDisplayed = false;
    }

    private static string Status(bool active) => active ? "active" : "not configured";

    private static string GetEmbeddingDescription(string? provider)
    {
        return provider?.ToLowerInvariant() switch
        {
            "lmsupply" or "localembedder" => "LMSupply (ONNX)",
            "inmemory" => "InMemory (test vectors)",
            "custom" => "custom provider",
            null => "LMSupply (default)",
            _ => provider
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "FluxIndex is using in-memory storage: indexed data is lost on restart. Use UseSQLite(path) or UsePostgreSQL(connectionString) to persist it.")]
    private static partial void LogInMemoryStorage(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "FluxIndex AI services: embedding {Embedding}, text completion {TextCompletion}, reranking {Reranking}, contextual enrichment {ContextualEnrichment}")]
    private static partial void LogServiceStatus(ILogger logger, string embedding, string textCompletion, string reranking, string contextualEnrichment);

    [LoggerMessage(Level = LogLevel.Information, Message = "FluxIndex: these run locally with LMSupply, no API key required - register them through ConfigureServices: {Registrations}")]
    private static partial void LogLocalServicesHint(ILogger logger, string registrations);
}
