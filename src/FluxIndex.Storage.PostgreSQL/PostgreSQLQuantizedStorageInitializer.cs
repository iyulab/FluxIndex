using System;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.SDK;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// Provisions the PostgreSQL quantized vector store schema (<c>vectors</c> + <c>quantized_vectors</c>).
/// </summary>
/// <remarks>
/// <see cref="ServiceCollectionExtensions.AddPostgreSQLQuantizedVectorStore(IServiceCollection, Action{PostgreSQLQuantizedOptions})"/>
/// registered no provisioning at all — neither an initializer nor a migration — so the store failed on
/// its first write even against an empty database. The store is reachable only by direct registration,
/// which is why no consumer had reported it.
/// </remarks>
internal sealed class PostgreSQLQuantizedStorageInitializer : IStorageInitializer
{
    public void InitializeSync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FluxIndexQuantizedDbContext>();

        RelationalSchemaProvisioner.EnsureDatabase(context);

        // Both tables carry vector-typed columns, so the extension has to exist first.
        context.Database.ExecuteSqlRaw("CREATE EXTENSION IF NOT EXISTS vector");

        RelationalSchemaProvisioner.ProvisionTables(context);
    }
}

/// <summary>
/// Runs <see cref="PostgreSQLQuantizedStorageInitializer"/> at host start, matching how the graph store
/// and semantic cache provision themselves for directly-registered consumers.
/// </summary>
internal sealed class PostgreSQLQuantizedMigrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PostgreSQLQuantizedStorageInitializer _initializer;

    public PostgreSQLQuantizedMigrationService(
        IServiceProvider serviceProvider,
        PostgreSQLQuantizedStorageInitializer initializer)
    {
        _serviceProvider = serviceProvider;
        _initializer = initializer;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _initializer.InitializeSync(_serviceProvider);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
