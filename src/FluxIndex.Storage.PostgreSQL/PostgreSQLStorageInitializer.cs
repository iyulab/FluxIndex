using FluxIndex.SDK;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL storage initializer. Creates the pgvector extension and the vector store schema on
/// Build(), symmetric with the SQLite initializer. Registered by
/// <see cref="FluxIndexContextBuilderExtensions.AddPostgreSQLStorage"/> when
/// <see cref="FluxIndex.SDK.Configuration.VectorStoreOptions.EnableAutoMigration"/> is true (the default).
/// </summary>
internal sealed class PostgreSQLStorageInitializer : IStorageInitializer
{
    public void InitializeSync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FluxIndexDbContext>();

        // The pgvector extension must exist before EnsureCreated builds the vector-typed column and
        // HNSW index. CREATE EXTENSION IF NOT EXISTS is a privilege-free no-op when the extension is
        // already installed (the managed-PostgreSQL case); it only needs CREATE privilege when the
        // extension is absent — the one scenario where a caller should opt out via EnableAutoMigration.
        context.Database.ExecuteSqlRaw("CREATE EXTENSION IF NOT EXISTS vector");
        context.Database.EnsureCreated();
    }
}
