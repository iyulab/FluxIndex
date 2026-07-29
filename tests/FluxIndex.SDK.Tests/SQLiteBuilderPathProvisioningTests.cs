using System.Collections.Generic;
using FluentAssertions;
using FluxIndex.Storage.SQLite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// <c>UseSQLite(path)</c> enables the vector store, the graph store and the semantic cache on one
/// database file. Build() provisions storage by running the registered <see cref="IStorageInitializer"/>
/// instances against its own service provider — it never starts a host, so any component that migrates
/// from an <c>IHostedService</c> would never be provisioned on this path. This pins that every
/// component the builder enables actually gets its schema.
/// </summary>
public class SQLiteBuilderPathProvisioningTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"fluxindex_provisioning_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        // Deliberately not SqliteConnection.ClearAllPools() — that is process-global and would yank
        // pooled connections out from under tests running in parallel. Deleting the temp file is
        // best-effort, as elsewhere in this suite.
        foreach (var path in new[] { _dbPath, EntityGraphPath })
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException) { }
        }

        GC.SuppressFinalize(this);
    }

    private string EntityGraphPath => Path.ChangeExtension(_dbPath, null) + "-entitygraph.db";

    [Fact]
    public void Build_WithSQLiteStack_ProvisionsEveryEnabledComponentSchema()
    {
        FluxIndexContext.CreateBuilder()
            .UseSQLite(_dbPath)
            .UseInMemoryEmbedding()
            .AddSQLiteStorage()
            .Build();

        var tables = TablesIn(_dbPath);

        // Vector store.
        tables.Should().Contain("vectors");

        // Graph store — enabled by UseSQLite on the same file.
        tables.Should().Contain("chunk_hierarchies");
        tables.Should().Contain("chunk_relationships");

        // Semantic cache — likewise enabled by UseSQLite.
        tables.Should().Contain("semantic_cache");
    }

    [Fact]
    public void Build_WithSQLiteStack_ProvisionsEntityGraphDatabase()
    {
        FluxIndexContext.CreateBuilder()
            .UseSQLite(_dbPath)
            .UseInMemoryEmbedding()
            .AddSQLiteStorage()
            .Build();

        // The entity graph lives in its own file derived from the vector database path.
        File.Exists(EntityGraphPath).Should().BeTrue();
        TablesIn(EntityGraphPath).Should().NotBeEmpty();
    }

    [Fact]
    public void Build_Twice_OnSameDatabase_IsIdempotent()
    {
        FluxIndexContext.CreateBuilder()
            .UseSQLite(_dbPath).UseInMemoryEmbedding().AddSQLiteStorage().Build();

        var act = () => FluxIndexContext.CreateBuilder()
            .UseSQLite(_dbPath).UseInMemoryEmbedding().AddSQLiteStorage().Build();

        act.Should().NotThrow();
        TablesIn(_dbPath).Should().Contain("chunk_hierarchies");
    }

    /// <summary>
    /// 0.21.1 이 PostgreSQL 에서 닫은 결함의 SQLite 판. 소비앱이 자기 테이블을 이미 가진 데이터베이스를
    /// 넘기면 EF <c>EnsureCreated()</c> 는 <c>HasTables()</c> 에서 스키마 생성을 <b>전부</b> 건너뛴다 —
    /// Build() 는 성공하고 첫 쓰기가 실패한다. 빈 픽스처로는 절대 드러나지 않는 구간이다.
    /// </summary>
    [Fact]
    public void Build_OnADatabaseThatAlreadyHasConsumerTables_StillProvisionsOurSchema()
    {
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE app_settings (key TEXT PRIMARY KEY, value TEXT)";
            command.ExecuteNonQuery();
        }

        FluxIndexContext.CreateBuilder()
            .UseSQLite(_dbPath)
            .UseInMemoryEmbedding()
            .AddSQLiteStorage()
            .Build();

        var tables = TablesIn(_dbPath);

        tables.Should().Contain("app_settings", "the consumer's own table must be left alone");
        tables.Should().Contain("vectors", "a pre-existing unrelated table must not suppress our schema");
        tables.Should().Contain("chunk_hierarchies");
        tables.Should().Contain("semantic_cache");
    }

    private static List<string> TablesIn(string databasePath)
    {
        var tables = new List<string>();

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
