using System;
using FluxIndex.SDK;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL vector store initializer. Creates the pgvector extension and the vector store schema on
/// Build(), symmetric with the SQLite initializer. Registered by
/// <see cref="FluxIndexContextBuilderExtensions.AddPostgreSQLStorage"/> when
/// <see cref="FluxIndex.SDK.Configuration.VectorStoreOptions.EnableAutoMigration"/> is true (the default).
/// </summary>
/// <remarks>
/// Provisioning goes through <see cref="RelationalSchemaProvisioner"/>, which creates only the
/// relations this store owns — see that type for why <c>EnsureCreated()</c> cannot be used.
/// Reported by All.Manual (2026-07-21).
/// </remarks>
internal sealed class PostgreSQLStorageInitializer : IStorageInitializer
{
    public void InitializeSync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FluxIndexDbContext>();

        // The pgvector extension must exist before the vector-typed column and HNSW index are built,
        // and installing it requires the database to exist first.
        RelationalSchemaProvisioner.EnsureDatabase(context);

        // CREATE EXTENSION IF NOT EXISTS is a privilege-free no-op when the extension is already
        // installed (the managed-PostgreSQL case); it only needs CREATE privilege when the extension
        // is absent — the one scenario where a caller should opt out via EnableAutoMigration.
        context.Database.ExecuteSqlRaw("CREATE EXTENSION IF NOT EXISTS vector");

        RelationalSchemaProvisioner.ProvisionTables(context);
    }
}
