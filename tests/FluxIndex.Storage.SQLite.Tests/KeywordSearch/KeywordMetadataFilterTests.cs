using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests.KeywordSearch;

/// <summary>
/// Pins the keyword index's metadata filter dimension.
///
/// <para>
/// The load-bearing claim is that the filter is <b>pushed into the query</b> rather than applied to
/// the results. A post-filter looks correct on a small corpus and fails exactly where it matters: on
/// a shared index, a scope whose documents lose the global ranking race gets zero results for a query
/// its documents match perfectly. <see cref="Search_AppliesFilterBeforeTruncation_NotAfter"/> is the
/// test that separates the two implementations — every other test here passes under both.
/// </para>
/// </summary>
public class KeywordMetadataFilterTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"fluxindex-metafilter-{Guid.NewGuid():N}.db");

    private SQLiteKeywordSearchService CreateService() =>
        new($"Data Source={_dbPath}", NullLogger<SQLiteKeywordSearchService>.Instance);

    private static DocumentChunk Chunk(
        string documentId,
        string content,
        Dictionary<string, object>? metadata = null,
        int index = 0)
    {
        var chunk = DocumentChunk.Create(documentId, content, index, 1);
        chunk.Metadata = metadata;
        return chunk;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(
            new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"));
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    // === The claim the feature exists for ===

    /// <summary>
    /// A tenant's documents are deliberately made the <em>worst</em> matches in the corpus, and
    /// MaxResults is set so the global top N contains none of them. A push-down returns the tenant's
    /// matches; a post-filter returns nothing.
    /// </summary>
    [Fact]
    public async Task Search_AppliesFilterBeforeTruncation_NotAfter()
    {
        var service = CreateService();

        var chunks = new List<DocumentChunk>();
        // Ten strong matches for another tenant - "provisioning" many times over.
        for (var i = 0; i < 10; i++)
        {
            chunks.Add(Chunk(
                $"other-{i}",
                "provisioning provisioning provisioning provisioning schema",
                new Dictionary<string, object> { ["tenant"] = "other" }));
        }

        // One weak match for ours - the term appears once, buried in unrelated words.
        chunks.Add(Chunk(
            "ours-1",
            "provisioning is mentioned once here among many other unrelated trailing words",
            new Dictionary<string, object> { ["tenant"] = "ours" }));

        await service.IndexChunksAsync(chunks, TestContext.Current.CancellationToken);

        var results = await service.SearchAsync("provisioning", new KeywordSearchOptions
        {
            MaxResults = 3,
            MetadataFilter = new Dictionary<string, object> { ["tenant"] = "ours" }
        }, TestContext.Current.CancellationToken);

        // Guard against a vacuous pass: the setup must really put our document outside the global top 3.
        var unfiltered = await service.SearchAsync("provisioning", new KeywordSearchOptions { MaxResults = 3 }, TestContext.Current.CancellationToken);
        unfiltered.Should().NotContain(r => r.Chunk.DocumentId == "ours-1",
            "otherwise this test would pass under a post-filter too and prove nothing");

        results.Should().ContainSingle().Which.Chunk.DocumentId.Should().Be("ours-1");
    }

    [Fact]
    public async Task Search_WithoutFilter_IsUnchanged()
    {
        var service = CreateService();
        await service.IndexChunksAsync([
            Chunk("doc-a", "provisioning schema", new Dictionary<string, object> { ["tenant"] = "a" }),
            Chunk("doc-b", "provisioning schema", new Dictionary<string, object> { ["tenant"] = "b" })
        ], TestContext.Current.CancellationToken);

        var results = await service.SearchAsync("provisioning", cancellationToken: TestContext.Current.CancellationToken);

        results.Select(r => r.Chunk.DocumentId).Should().BeEquivalentTo(["doc-a", "doc-b"]);
    }

    // === Match-any and value shapes ===

    [Fact]
    public async Task Search_CollectionFilterValue_MatchesAnyElement()
    {
        var service = CreateService();
        await service.IndexChunksAsync([
            Chunk("doc-a", "provisioning", new Dictionary<string, object> { ["tenant"] = "a" }),
            Chunk("doc-b", "provisioning", new Dictionary<string, object> { ["tenant"] = "b" }),
            Chunk("doc-c", "provisioning", new Dictionary<string, object> { ["tenant"] = "c" })
        ], TestContext.Current.CancellationToken);

        var results = await service.SearchAsync("provisioning", new KeywordSearchOptions
        {
            MetadataFilter = new Dictionary<string, object> { ["tenant"] = new[] { "a", "c" } }
        }, TestContext.Current.CancellationToken);

        results.Select(r => r.Chunk.DocumentId).Should().BeEquivalentTo(["doc-a", "doc-c"]);
    }

    [Fact]
    public async Task Search_ChunkWithSeveralValuesForOneKey_MatchesAnyOfThem()
    {
        var service = CreateService();
        await service.IndexChunksAsync([
            Chunk("doc-multi", "provisioning",
                new Dictionary<string, object> { ["label"] = new[] { "red", "blue" } })
        ], TestContext.Current.CancellationToken);

        var byRed = await service.SearchAsync("provisioning", new KeywordSearchOptions
        {
            MetadataFilter = new Dictionary<string, object> { ["label"] = "red" }
        }, TestContext.Current.CancellationToken);
        var byBlue = await service.SearchAsync("provisioning", new KeywordSearchOptions
        {
            MetadataFilter = new Dictionary<string, object> { ["label"] = "blue" }
        }, TestContext.Current.CancellationToken);

        byRed.Should().ContainSingle();
        byBlue.Should().ContainSingle();
    }

    /// <summary>
    /// A chunk carrying several values for one key must still be scored once. Joining the metadata
    /// rows instead of testing existence would return the chunk once per matching value and inflate
    /// its BM25 score — the ranking would depend on how many tags a document happens to have.
    /// </summary>
    [Fact]
    public async Task Search_MultiValuedMetadata_DoesNotDuplicateOrInflateScore()
    {
        var service = CreateService();
        await service.IndexChunksAsync([
            Chunk("doc-multi", "provisioning schema",
                new Dictionary<string, object> { ["label"] = new[] { "red", "blue", "green" } })
        ], TestContext.Current.CancellationToken);

        var filtered = await service.SearchAsync("provisioning schema", new KeywordSearchOptions
        {
            MetadataFilter = new Dictionary<string, object> { ["label"] = "red" }
        }, TestContext.Current.CancellationToken);
        var unfiltered = await service.SearchAsync("provisioning schema", cancellationToken: TestContext.Current.CancellationToken);

        filtered.Should().ContainSingle();
        filtered[0].Score.Should().BeApproximately(unfiltered[0].Score, 1e-9,
            "a filter selects rows; it must not change how they score");
    }

    [Fact]
    public async Task Search_MultipleFilterEntries_AreAnded()
    {
        var service = CreateService();
        await service.IndexChunksAsync([
            Chunk("doc-hit", "provisioning",
                new Dictionary<string, object> { ["tenant"] = "a", ["stage"] = "final" }),
            Chunk("doc-miss", "provisioning",
                new Dictionary<string, object> { ["tenant"] = "a", ["stage"] = "draft" })
        ], TestContext.Current.CancellationToken);

        var results = await service.SearchAsync("provisioning", new KeywordSearchOptions
        {
            MetadataFilter = new Dictionary<string, object> { ["tenant"] = "a", ["stage"] = "final" }
        }, TestContext.Current.CancellationToken);

        results.Should().ContainSingle().Which.Chunk.DocumentId.Should().Be("doc-hit");
    }

    /// <summary>
    /// Numbers and booleans are written by the indexer as typed values and supplied by callers the
    /// same way. Both sides go through one formatter, so <c>7</c> indexed matches <c>7</c> queried
    /// without the caller having to know the index stores text.
    /// </summary>
    [Theory]
    [InlineData(7, 7)]
    [InlineData(true, true)]
    [InlineData("plain", "plain")]
    public async Task Search_NonStringScalarValues_RoundTrip(object indexed, object queried)
    {
        var service = CreateService();
        await service.IndexChunksAsync([
            Chunk("doc-typed", "provisioning", new Dictionary<string, object> { ["v"] = indexed })
        ], TestContext.Current.CancellationToken);

        var results = await service.SearchAsync("provisioning", new KeywordSearchOptions
        {
            MetadataFilter = new Dictionary<string, object> { ["v"] = queried }
        }, TestContext.Current.CancellationToken);

        results.Should().ContainSingle();
    }

    [Fact]
    public async Task Search_ChunkWithoutMetadata_IsExcludedByAnyFilter()
    {
        var service = CreateService();
        await service.IndexChunksAsync([Chunk("doc-bare", "provisioning")], TestContext.Current.CancellationToken);

        var results = await service.SearchAsync("provisioning", new KeywordSearchOptions
        {
            MetadataFilter = new Dictionary<string, object> { ["tenant"] = "a" }
        }, TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }

    // === Re-indexing keeps the filter dimension truthful ===

    /// <summary>
    /// Re-indexing a chunk whose metadata lost a key must stop it matching that key. An upsert keyed
    /// on the rows now present would leave the stale row behind, and the chunk would keep answering a
    /// filter it no longer satisfies — a leak across whatever boundary the key represents.
    /// </summary>
    [Fact]
    public async Task Reindex_WithChangedMetadata_DropsTheStaleValue()
    {
        var service = CreateService();

        // The same chunk instance, so the second pass really replaces the first. Building a second
        // chunk with DocumentChunk.Create would mint a new id and index a sibling instead - which is
        // a different (known) behaviour and would not exercise replacement at all.
        var chunk = Chunk("doc-move", "provisioning", new Dictionary<string, object> { ["tenant"] = "old" });
        await service.IndexChunksAsync([chunk], TestContext.Current.CancellationToken);

        chunk.Metadata = new Dictionary<string, object> { ["tenant"] = "new" };
        await service.IndexChunksAsync([chunk], TestContext.Current.CancellationToken);

        var byOld = await service.SearchAsync("provisioning", new KeywordSearchOptions
        {
            MetadataFilter = new Dictionary<string, object> { ["tenant"] = "old" }
        }, TestContext.Current.CancellationToken);
        var byNew = await service.SearchAsync("provisioning", new KeywordSearchOptions
        {
            MetadataFilter = new Dictionary<string, object> { ["tenant"] = "new" }
        }, TestContext.Current.CancellationToken);

        byOld.Should().BeEmpty("the chunk no longer carries the old value");
        byNew.Should().ContainSingle();
    }

    // === Bulk delete ===

    [Fact]
    public async Task DeleteByFilter_RemovesOnlyMatchingChunks_AndReportsTheCount()
    {
        var service = CreateService();
        await service.IndexChunksAsync([
            Chunk("doc-a1", "provisioning", new Dictionary<string, object> { ["tenant"] = "a" }),
            Chunk("doc-a2", "provisioning", new Dictionary<string, object> { ["tenant"] = "a" }),
            Chunk("doc-b1", "provisioning", new Dictionary<string, object> { ["tenant"] = "b" })
        ], TestContext.Current.CancellationToken);

        var removed = await service.DeleteByFilterAsync(new Dictionary<string, object> { ["tenant"] = "a" }, TestContext.Current.CancellationToken);

        removed.Should().Be(2);

        var survivors = await service.SearchAsync("provisioning", cancellationToken: TestContext.Current.CancellationToken);
        survivors.Select(r => r.Chunk.DocumentId).Should().BeEquivalentTo(["doc-b1"]);
    }

    /// <summary>
    /// Deleting must clear the metadata rows too. A leftover row would let a re-used chunk id inherit
    /// the deleted chunk's scope, which is the same cross-boundary leak as the stale-value case.
    /// </summary>
    [Fact]
    public async Task DeleteByFilter_AlsoClearsTheFilterDimension()
    {
        var service = CreateService();
        var chunk = Chunk("doc-a1", "provisioning", new Dictionary<string, object> { ["tenant"] = "a" });
        await service.IndexChunksAsync([chunk], TestContext.Current.CancellationToken);

        await service.DeleteByFilterAsync(new Dictionary<string, object> { ["tenant"] = "a" }, TestContext.Current.CancellationToken);

        // Re-index under the same chunk id with no metadata. A leftover metadata row would let it
        // inherit the deleted chunk's scope; a new id would sidestep the question entirely.
        chunk.Metadata = null;
        await service.IndexChunksAsync([chunk], TestContext.Current.CancellationToken);

        var byOldTenant = await service.SearchAsync("provisioning", new KeywordSearchOptions
        {
            MetadataFilter = new Dictionary<string, object> { ["tenant"] = "a" }
        }, TestContext.Current.CancellationToken);

        byOldTenant.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteByFilter_MatchingNothing_ReturnsZeroAndKeepsTheIndex()
    {
        var service = CreateService();
        await service.IndexChunksAsync([
            Chunk("doc-b1", "provisioning", new Dictionary<string, object> { ["tenant"] = "b" })
        ], TestContext.Current.CancellationToken);

        var removed = await service.DeleteByFilterAsync(new Dictionary<string, object> { ["tenant"] = "absent" }, TestContext.Current.CancellationToken);

        removed.Should().Be(0);
        (await service.SearchAsync("provisioning", cancellationToken: TestContext.Current.CancellationToken)).Should().ContainSingle();
    }

    // === Fail loud rather than widen ===

    /// <summary>
    /// An empty filter would mean "match everything", which for a delete is "drop the index". That is
    /// <c>ClearIndexAsync</c>'s job and must not be reachable by handing in an empty dictionary.
    /// </summary>
    [Fact]
    public async Task DeleteByFilter_EmptyFilter_Throws()
    {
        var service = CreateService();

        var act = () => service.DeleteByFilterAsync(new Dictionary<string, object>());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Silently dropping an unfilterable condition would widen the filter: the caller asked for A AND
    /// B and would get rows they meant to exclude. On a delete that destroys another scope's rows.
    /// </summary>
    [Fact]
    public async Task Filter_WithUnfilterableValue_ThrowsRatherThanWidening()
    {
        var service = CreateService();
        await service.IndexChunksAsync([Chunk("doc-a", "provisioning")], TestContext.Current.CancellationToken);

        var search = () => service.SearchAsync("provisioning", new KeywordSearchOptions
        {
            MetadataFilter = new Dictionary<string, object>
            {
                ["tenant"] = "a",
                ["nested"] = new Dictionary<string, object> { ["x"] = 1 }
            }
        });
        var delete = () => service.DeleteByFilterAsync(new Dictionary<string, object>
        {
            ["nested"] = new Dictionary<string, object> { ["x"] = 1 }
        });

        await search.Should().ThrowAsync<ArgumentException>();
        await delete.Should().ThrowAsync<ArgumentException>();
    }
}
