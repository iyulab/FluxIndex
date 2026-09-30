using FluxGuard.Remote.RAG;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxIndex.Integrations.FluxGuard;

/// <summary>Registers FluxGuard RAG security as FluxIndex's retrieval guard.</summary>
public static class FluxGuardRetrievalGuardServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FluxGuardRetrievalGuard"/> as the <see cref="IRetrievalGuard"/> every FluxIndex search applies,
    /// over the <see cref="IRAGSecurityPipeline"/> registered in the same container (register it with FluxGuard.Remote).
    /// </summary>
    /// <remarks>
    /// The guard is resolved with the lifetime of whatever resolves it, so it follows the pipeline's registration. A
    /// container without an <see cref="IRAGSecurityPipeline"/> fails when the guard is resolved — building a FluxIndex
    /// context — rather than searching unguarded. A guard registered earlier is kept.
    /// </remarks>
    public static IServiceCollection AddFluxGuardRetrievalGuard(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<IRetrievalGuard>(sp => new FluxGuardRetrievalGuard(sp.GetRequiredService<IRAGSecurityPipeline>()));
        return services;
    }
}
