using FluentAssertions;
using FluxIndex.Storage.SQLite;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Guards against the silent non-persistence reported by All.Manual on 0.18.0: configuring a
/// persistent provider (<c>UsePostgreSQL</c>/<c>UseSQLite</c>) only sets options — registration
/// lives in the storage package's <c>Add*Storage()</c> extension. When that call was missing,
/// <c>Build()</c> fell back to an in-memory store without a word, so the app "worked" and lost its
/// whole index on restart.
/// </summary>
public class StorageRegistrationGuardTests : IDisposable
{
    private readonly string _testDbPath =
        Path.Combine(Path.GetTempPath(), $"fluxindex_guard_test_{Guid.NewGuid()}.db");

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
                File.Delete(_testDbPath);
        }
        catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Build_WithPostgreSQLConfiguredButStorageNotRegistered_ThrowsActionableError()
    {
        var builder = FluxIndexContext.CreateBuilder()
            .UsePostgreSQL("Host=localhost;Database=fluxindex;Username=u;Password=p")
            .UseInMemoryEmbedding();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().ContainAll("PostgreSQL", "AddPostgreSQLStorage", "FluxIndex.Storage.PostgreSQL");
    }

    [Fact]
    public void Build_WithSQLiteConfiguredButStorageNotRegistered_ThrowsActionableError()
    {
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseInMemoryEmbedding();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().ContainAll("SQLite", "AddSQLiteStorage");
    }

    [Fact]
    public void Build_WithRedisCacheConfiguredButStorageNotRegistered_ThrowsActionableError()
    {
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .AddSQLiteStorage()
            .UseRedisCache("localhost:6379")
            .UseInMemoryEmbedding();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().ContainAll("Redis", "AddRedisStorage");
    }

    /// <summary>The guard must not fire on the correct wiring — this is the regression bound.</summary>
    [Fact]
    public void Build_WithSQLiteConfiguredAndRegistered_Succeeds()
    {
        var context = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .AddSQLiteStorage()
            .UseInMemoryEmbedding()
            .Build();

        context.Should().NotBeNull();
    }

    /// <summary>
    /// Each in-memory SQLite context must get its own database. <c>Data Source=:memory:;Cache=Shared</c>
    /// names one process-global database, so co-existing contexts read each other's rows — and racing
    /// schema creation throws 'table "vectors" already exists'.
    /// </summary>
    [Fact]
    public async Task InMemorySQLiteContexts_DoNotSeeEachOthersDocuments()
    {
        var contextA = FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory().AddSQLiteStorage().UseInMemoryEmbedding().Build();
        var contextB = FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory().AddSQLiteStorage().UseInMemoryEmbedding().Build();

        await contextA.Indexer.IndexDocumentAsync("content only A indexed", "doc-a");

        var seenByB = await contextB.Retriever.SearchAsync("content", maxResults: 10, minScore: 0f);
        seenByB.Should().BeEmpty("each in-memory context owns a private database");
    }

    /// <summary>No provider configured at all is the one case the in-memory fallback is for.</summary>
    [Fact]
    public void Build_WithNoStoreProviderConfigured_FallsBackToInMemoryWithoutThrowing()
    {
        var context = FluxIndexContext.CreateBuilder()
            .UseInMemoryEmbedding()
            .Build();

        context.Should().NotBeNull();
    }
}
