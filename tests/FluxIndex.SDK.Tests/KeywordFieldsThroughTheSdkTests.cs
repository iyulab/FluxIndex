using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// The file-name field reaches a document indexed through the SDK (FluxIndex docket #28, part 1):
/// <see cref="Indexer"/> carries <see cref="Document.FileName"/> into each chunk's <c>file_name</c>
/// metadata, which the relational keyword index scores as a field by default, so a query quoting the
/// file name retrieves the document without the caller tagging every chunk — and a value the caller
/// did set is left alone.
/// </summary>
public class KeywordFieldsThroughTheSdkTests
{
    private static IFluxIndexContext BuildContext()
        => FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .UseInMemoryEmbedding()
            .AddSQLiteStorage()
            .Build();

    private static Document DocumentWithChunks(string id, string fileName, params DocumentChunk[] chunks)
    {
        var document = new Document { Id = id, FileName = fileName, Content = string.Join("\n", chunks.Select(c => c.Content)) };
        foreach (var chunk in chunks)
        {
            chunk.DocumentId = id;
            document.AddChunk(chunk);
        }

        return document;
    }

    [Fact]
    public async Task ADocumentIndexedThroughTheSdk_IsRetrievableByItsFileName()
    {
        var ct = TestContext.Current.CancellationToken;
        var context = BuildContext();
        var keywordSearch = context.ServiceProvider.GetRequiredService<IKeywordSearchService>();

        await context.Indexer.IndexDocumentAsync(
            DocumentWithChunks("doc-1", "ZS11-HA.txt", DocumentChunk.Create("doc-1", "storage nas overview", 0, 1)),
            cancellationToken: ct);
        await context.Indexer.IndexDocumentAsync(
            DocumentWithChunks("doc-2", "other.txt", DocumentChunk.Create("doc-2", "storage san overview", 0, 1)),
            cancellationToken: ct);

        var results = await keywordSearch.SearchAsync("ZS11-HA", cancellationToken: ct);

        results.Should().ContainSingle().Which.Chunk.DocumentId.Should().Be("doc-1",
            "the file name is not in any chunk body, so only the file_name field can have matched");
    }

    [Fact]
    public async Task AFileNameTheCallerAlreadySet_IsNotOverwritten()
    {
        var ct = TestContext.Current.CancellationToken;
        var context = BuildContext();
        var keywordSearch = context.ServiceProvider.GetRequiredService<IKeywordSearchService>();

        var chunk = DocumentChunk.Create("doc-1", "body text", 0, 1);
        chunk.Metadata = new Dictionary<string, object> { ["file_name"] = "caller-choice.md" };
        await context.Indexer.IndexDocumentAsync(DocumentWithChunks("doc-1", "document-property.md", chunk), cancellationToken: ct);

        (await keywordSearch.SearchAsync("caller-choice", cancellationToken: ct)).Should().ContainSingle();
        (await keywordSearch.SearchAsync("document-property", cancellationToken: ct)).Should().BeEmpty();
    }
}
