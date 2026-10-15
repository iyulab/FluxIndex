using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests.KeywordSearch;

/// <summary>
/// Runs the shared index-definition contract suite against the SQLite keyword index, plus the constructor check
/// that an analyzer must name itself.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteKeywordSearchDefinitionChangeTests : KeywordSearchDefinitionChangeContractSuite, IDisposable
{
    private readonly List<string> _paths = [];
    private readonly List<SQLiteKeywordSearchService> _services = [];

    private string NewPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluxindex-kw-definition-{Guid.NewGuid():N}.db");
        _paths.Add(path);
        return path;
    }

    private SQLiteKeywordSearchService Create(string path, ITextAnalyzer? analyzer, KeywordFieldOptions? fields, ILogger? logger = null)
    {
        var service = new SQLiteKeywordSearchService($"Data Source={path}", logger is null ? NullLogger<SQLiteKeywordSearchService>.Instance : new TypedLogger<SQLiteKeywordSearchService>(logger), analyzer, fields);
        _services.Add(service);
        return service;
    }

    protected override Task<IKeywordSearchService> CreateServiceAsync(ITextAnalyzer? analyzer, KeywordFieldOptions? fields = null)
        => Task.FromResult<IKeywordSearchService>(Create(NewPath(), analyzer, fields));

    protected override Task<IKeywordSearchService> ReopenAsync(ITextAnalyzer analyzer, KeywordFieldOptions? fields = null, ILogger? logger = null)
        => Task.FromResult<IKeywordSearchService>(Create(_paths[^1], analyzer, fields, logger));

    protected override async Task ForgetRecordedAnalyzerAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_paths[^1]}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM bm25_statistics WHERE key LIKE 'analyzer:%'";
        await command.ExecuteNonQueryAsync();
    }

    protected override async Task WriteStatisticAsync(string key)
    {
        await using var connection = new SqliteConnection($"Data Source={_paths[^1]}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO bm25_statistics (key, value) VALUES (@key, 1)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@key";
        parameter.Value = key;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public void AnAnalyzerWithoutIdentity_IsRefused()
    {
        var analyzer = Substitute.For<ITextAnalyzer>();
        analyzer.Identity.Returns(" ");

        var create = () => Create(NewPath(), analyzer, fields: null);

        create.Should().Throw<ArgumentException>().WithMessage("*empty Identity*");
    }

    public void Dispose()
    {
        foreach (var service in _services)
            service.Dispose();
        foreach (var path in _paths)
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
