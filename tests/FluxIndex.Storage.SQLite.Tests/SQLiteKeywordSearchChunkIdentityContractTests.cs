using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// Runs the shared keyword-index chunk-identity contract suite against the SQLite (relational BM25)
/// keyword index.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteKeywordSearchChunkIdentityContractTests : KeywordSearchChunkIdentityContractSuite, IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"fluxindex-kw-contract-{Guid.NewGuid():N}.db");

    protected override Task<IKeywordSearchService> CreateServiceAsync()
        => Task.FromResult<IKeywordSearchService>(
            new SQLiteKeywordSearchService($"Data Source={_path}", NullLogger<SQLiteKeywordSearchService>.Instance));

    public void Dispose()
    {
        try { File.Delete(_path); } catch (IOException) { }
    }
}
