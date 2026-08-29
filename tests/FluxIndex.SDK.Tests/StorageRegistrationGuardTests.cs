using AwesomeAssertions;
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

        await contextA.Indexer.IndexDocumentAsync("content only A indexed", "doc-a", cancellationToken: TestContext.Current.CancellationToken);

        var seenByB = await contextB.Retriever.SearchAsync("content", maxResults: 10, minScore: 0f, cancellationToken: TestContext.Current.CancellationToken);
        seenByB.Should().BeEmpty("each in-memory context owns a private database");
    }

    /// <summary>
    /// A keyword provider named on its own — the split deployment the leg's own options exist for —
    /// must be registered too. Otherwise Build() falls through to the in-memory BM25 fallback and
    /// hybrid search keeps returning results from the vector leg, so the loss never surfaces as a
    /// failure: the sparse half is just empty after every restart.
    /// </summary>
    [Fact]
    public void Build_WithAKeywordProviderConfiguredButStorageNotRegistered_ThrowsActionableError()
    {
        var builder = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .AddSQLiteStorage()
            .UseInMemoryEmbedding();
        builder.Options.KeywordSearch.Provider = "PostgreSQL";
        builder.Options.KeywordSearch.UseVectorStoreConnection = false;
        builder.Options.KeywordSearch.ConnectionString = "Host=localhost;Database=meta;Username=u;Password=p";

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().ContainAll("PostgreSQL", "AddPostgreSQLKeywordSearch");
    }

    /// <summary>The guard must stay silent when the leg follows the vector store — the default.</summary>
    [Fact]
    public void Build_WithAnUnsetKeywordProvider_DoesNotFire()
    {
        var context = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .AddSQLiteStorage()
            .UseInMemoryEmbedding()
            .Build();

        context.Should().NotBeNull();
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

    /// <summary>
    /// 0.22.0 계약: 키워드 레그는 벡터 스토어와 함께 재시작을 살아남는다.
    ///
    /// 0.21.5 까지 이 테스트는 <b>깨진 계약을 고정한 characterization</b> 이었다 — 레그가 프로세스-로컬
    /// <c>InMemoryDocumentRepository</c> 를 타서 재시작 후 빈 결과를 냈고(hybrid 는 조용히 vector-only),
    /// 그 상태를 "정상"으로 단정하고 있었다. 인덱싱 경로가 sparse 인덱스를 채우고
    /// <c>UseSQLite</c> 가 영속 백엔드를 등록하게 되면서 <b>뒤집혔다</b>. 뒤집히는 것이 설계된 동작이다.
    /// </summary>
    [Fact]
    public async Task KeywordLeg_SurvivesARestart_AlongsideTheVectorStore()
    {
        const string keyword = "zylophonequux"; // distinctive token, won't collide with other content
        const string docId = "doc-restart";

        // Process A: index into the persistent SQLite store; the keyword leg sees it in-process.
        var contextA = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath).AddSQLiteStorage().UseInMemoryEmbedding().Build();
        try
        {
            await contextA.Indexer.IndexDocumentAsync($"the {keyword} is a rare instrument", docId, cancellationToken: TestContext.Current.CancellationToken);

            var inProcess = await contextA.Retriever.KeywordSearchAsync(keyword, maxResults: 10, cancellationToken: TestContext.Current.CancellationToken);
            inProcess.Should().NotBeEmpty("the keyword leg sees documents indexed by its own process");
        }
        finally
        {
            (contextA as IDisposable)?.Dispose();
        }

        // Process B: a fresh Build over the SAME db file == restart / new process context.
        var contextB = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath).AddSQLiteStorage().UseInMemoryEmbedding().Build();
        try
        {
            // The SQLite-backed vector store survives the restart...
            var stats = await contextB.Retriever.GetStatisticsAsync(TestContext.Current.CancellationToken);
            stats.TotalChunks.Should().BeGreaterThan(0, "the persistent vector store survives a restart");

            // ...and so does the keyword leg, which is the whole point of 0.22.0.
            var afterRestart = await contextB.Retriever.KeywordSearchAsync(keyword, maxResults: 10, cancellationToken: TestContext.Current.CancellationToken);
            afterRestart.Should().NotBeEmpty(
                "the keyword index is persisted alongside the vectors, so a restarted process still " +
                "finds documents by keyword instead of degrading to vector-only");
        }
        finally
        {
            (contextB as IDisposable)?.Dispose();
        }
    }
}
