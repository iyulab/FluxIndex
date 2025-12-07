namespace FluxIndex.Stack.Application.Interfaces.Services;

/// <summary>
/// Interface for managing embedding provider cache.
/// Used to invalidate cached providers when AI settings change.
/// </summary>
public interface IEmbeddingProviderCache
{
    /// <summary>
    /// Invalidates the cached embedding provider, forcing reconfiguration on next use.
    /// </summary>
    void InvalidateCache();
}
