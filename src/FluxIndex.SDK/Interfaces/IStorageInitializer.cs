using System;

namespace FluxIndex.SDK;

/// <summary>
/// Interface for storage-specific initialization logic.
/// Storage packages register implementations to perform database initialization
/// (e.g., EnsureCreated, WAL mode, migrations) during Build().
/// </summary>
public interface IStorageInitializer
{
    /// <summary>
    /// Perform synchronous storage initialization.
    /// Called during FluxIndexContextBuilder.Build() after all services are registered.
    /// </summary>
    /// <param name="serviceProvider">The built service provider.</param>
    void InitializeSync(IServiceProvider serviceProvider);
}
