using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Providers.LMSupply.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxIndex.Providers.LMSupply.Extensions;

/// <summary>
/// Extension methods for registering LMSupply-based AI services with dependency injection.
/// All services use local ONNX inference — no API key required.
/// </summary>
/// <remarks>
/// Every registration is lazy: building the container and resolving the service never loads (or
/// downloads) a model. The load happens on first use — with the caller's cancellation token, and the
/// configured progress reporting and timeout — or at host start when <c>WarmUpOnStart</c> is set.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IEmbeddingService"/> backed by a local LMSupply ONNX embedding model,
    /// loaded on first use.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="modelId">LMSupply catalog alias (e.g., "default", "fast", "large") or model ID.</param>
    /// <param name="revision">
    /// Optional pipeline revision — raise it when an upgrade makes this model's vectors
    /// incomparable with what is already indexed, so the collection separates instead of mixing.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLMSupplyEmbedding(
        this IServiceCollection services,
        string modelId = "default",
        string? revision = null)
        => services.AddLMSupplyEmbedding(o =>
        {
            o.ModelId = modelId;
            o.Revision = revision;
        });

    /// <summary>
    /// Registers an <see cref="IEmbeddingService"/> backed by a local LMSupply ONNX embedding model,
    /// loaded on first use, with full control over progress, timeout, warm-up and the pre-load identity.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the <see cref="LMSupplyEmbeddingOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLMSupplyEmbedding(
        this IServiceCollection services,
        Action<LMSupplyEmbeddingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new LMSupplyEmbeddingOptions();
        configure(options);

        services.AddSingleton<IEmbeddingService>(_ => new LMSupplyEmbeddingService(options));
        AddWarmUp<IEmbeddingService>(services, options);
        return services;
    }

    /// <summary>
    /// Registers an <see cref="IReranker"/> backed by a local LMSupply ONNX cross-encoder model,
    /// loaded on first use.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="modelId">Reranker model ID (e.g., "ms-marco-MiniLM-L6-v2") or "default".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLMSupplyReranker(
        this IServiceCollection services,
        string modelId = "default")
        => services.AddLMSupplyReranker(o => o.ModelId = modelId);

    /// <summary>
    /// Registers an <see cref="IReranker"/> backed by a local LMSupply ONNX cross-encoder model,
    /// loaded on first use, with control over progress, timeout and warm-up.
    /// </summary>
    public static IServiceCollection AddLMSupplyReranker(
        this IServiceCollection services,
        Action<LMSupplyRerankerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new LMSupplyRerankerOptions();
        configure(options);

        services.AddSingleton<IReranker>(sp =>
            new LMSupplyRerankerService(options, CreateLogger<LMSupplyRerankerService>(sp)));
        AddWarmUp<IReranker>(services, options);
        return services;
    }

    /// <summary>
    /// Registers an <see cref="ITextCompletionService"/> backed by a local LMSupply generator model,
    /// loaded on first use.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="modelId">Generator model ID or "default".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLMSupplyTextCompletion(
        this IServiceCollection services,
        string modelId = "default")
        => services.AddLMSupplyTextCompletion(o => o.ModelId = modelId);

    /// <summary>
    /// Registers an <see cref="ITextCompletionService"/> backed by a local LMSupply generator model,
    /// loaded on first use, with control over progress, timeout and warm-up.
    /// </summary>
    public static IServiceCollection AddLMSupplyTextCompletion(
        this IServiceCollection services,
        Action<LMSupplyTextCompletionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new LMSupplyTextCompletionOptions();
        configure(options);

        services.AddSingleton<ITextCompletionService>(sp =>
            new LMSupplyTextCompletionService(options, CreateLogger<LMSupplyTextCompletionService>(sp)));
        AddWarmUp<ITextCompletionService>(services, options);
        return services;
    }

    private static ILogger CreateLogger<T>(IServiceProvider sp) =>
        sp.GetService<ILoggerFactory>()?.CreateLogger<T>() ?? NullLogger<T>.Instance;

    private static void AddWarmUp<TService>(IServiceCollection services, LMSupplyServiceOptionsBase options)
        where TService : class
    {
        if (!options.WarmUpOnStart)
            return;

        services.AddSingleton<IHostedService>(sp => new LMSupplyWarmUpService(
            typeof(TService).Name,
            () => sp.GetRequiredService<TService>() as ILazilyLoadedModel,
            CreateLogger<LMSupplyWarmUpService>(sp)));
    }
}
