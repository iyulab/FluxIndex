using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// <see cref="IVectorStore.ResolvedStoreName"/> and <see cref="IVectorStore.DetectedDimension"/> are what
/// retriever statistics report to a consumer. The vec0 store resolves a fingerprinted table name and a
/// bound dimension, so it must report them instead of the interface default (null = "not resolved").
/// </summary>
[Collection("SQLite Tests")]
public class SQLiteVecResolvedStoreStatsTests : IDisposable
{
    private readonly string _dbPath = $"stats_{Guid.NewGuid():N}.db";
    private ServiceProvider? _sp;

    [Fact]
    public async Task AfterBindAndWrite_ReportsFingerprintTableAndBoundDimension()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSQLiteVecVectorStore(o =>
        {
            o.UseInMemory = true;
            o.UseSQLiteVec = true;
            o.VectorDimension = 4;
            o.DatabasePath = _dbPath;
            o.FallbackToInMemoryOnError = false;
        });
        _sp = services.BuildServiceProvider();

        using var scope = _sp.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<SQLiteVecVectorStore>();
        IVectorStore contract = store;

        contract.ResolvedStoreName.Should().BeNull("nothing is resolved before initialization");

        var identity = new EmbeddingIdentity { Provider = "test", Model = "stats-model", Dimension = 4 };
        store.BindIdentity(identity);
        contract.DetectedDimension.Should().Be(4, "the bound identity fixes the dimension");

        var ctx = scope.ServiceProvider.GetRequiredService<SQLiteVecDbContext>();
        await ctx.InitializeAsync(TestContext.Current.CancellationToken);
        await store.StoreAsync(new DocumentChunk
        {
            DocumentId = "doc",
            ChunkIndex = 0,
            Content = "stats probe",
            Embedding = [0.1f, 0.2f, 0.3f, 0.4f],
            TokenCount = 2,
        }, TestContext.Current.CancellationToken);

        var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SQLiteVecOptions>>().Value;
        contract.ResolvedStoreName.Should().Be(options.GetVecTableName(), "the vec0 table the writes landed in");
        contract.DetectedDimension.Should().Be(4);
    }

    public void Dispose()
    {
        _sp?.Dispose();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch
        {
            /* best effort test cleanup */
        }
        GC.SuppressFinalize(this);
    }
}
