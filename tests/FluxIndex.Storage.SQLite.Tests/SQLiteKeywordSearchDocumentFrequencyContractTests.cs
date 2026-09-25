using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>Runs the shared document-frequency contract suite against the SQLite keyword index.</summary>
[Collection("SQLite Tests")]
public sealed class SQLiteKeywordSearchDocumentFrequencyContractTests : KeywordSearchDocumentFrequencyContractSuite, IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"fluxindex-kw-df-{Guid.NewGuid():N}.db");

    protected override Task<IKeywordSearchService> CreateServiceAsync()
        => Task.FromResult<IKeywordSearchService>(
            new SQLiteKeywordSearchService($"Data Source={_path}", NullLogger<SQLiteKeywordSearchService>.Instance));

    public void Dispose()
    {
        try { File.Delete(_path); } catch (IOException) { }
    }
}
