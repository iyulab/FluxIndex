using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Graph;

/// <summary>
/// <see cref="EntityGraphBuildOptions.ReuseStoredExtractions"/>: a chunk whose entities are already in
/// the graph store (matched by chunk id) is not sent to the extractor again. Building the same chunks
/// twice costs one round of extraction, not two — the difference between re-indexing a corpus and
/// re-paying every LLM call it ever made. The store double below keeps entities in memory and answers
/// the chunk-scoped query the way the real stores do (any entity whose ChunkIds contain one of the ids).
/// </summary>
public class EntityGraphServiceReuseTests
{
    private readonly IAdvancedEntityExtractionService _extractor = Substitute.For<IAdvancedEntityExtractionService>();
    private readonly IGraphStore _store = Substitute.For<IGraphStore>();
    private readonly List<GraphEntity> _storedEntities = [];
    private readonly List<IReadOnlyList<string>> _extractedBatches = [];

    public EntityGraphServiceReuseTests()
    {
        _store.StoreEntitiesBatchAsync(Arg.Any<IEnumerable<GraphEntity>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var batch = ci.Arg<IEnumerable<GraphEntity>>().ToList();
                foreach (var entity in batch)
                {
                    _storedEntities.RemoveAll(e => e.Id == entity.Id);
                    _storedEntities.Add(entity);
                }
                return Task.FromResult<IReadOnlyList<string>>(batch.Select(e => e.Id).ToList());
            });
        _store.StoreRelationshipsBatchAsync(Arg.Any<IEnumerable<GraphRelationship>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IReadOnlyList<string>>(ci.Arg<IEnumerable<GraphRelationship>>().Select(r => r.Id).ToList()));
        _store.GetEntitiesByChunkIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var ids = ci.Arg<IEnumerable<string>>().ToHashSet();
                return Task.FromResult<IReadOnlyList<GraphEntity>>(
                    _storedEntities.Where(e => e.ChunkIds.Any(ids.Contains)).ToList());
            });
        _store.GetRelationshipsAsync(Arg.Any<string>(), Arg.Any<TraversalDirection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<GraphRelationship>>([]));

        _extractor.ExtractBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var contents = ci.Arg<IEnumerable<string>>().ToList();
                _extractedBatches.Add(contents);
                return Task.FromResult<IReadOnlyList<EntityGraph>>(contents.Select(ExtractFrom).ToList());
            });
    }

    /// <summary>A fake extractor: every capitalised word is an organisation.</summary>
    private static EntityGraph ExtractFrom(string content)
    {
        var entities = content
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', ','))
            .Where(w => w.Length > 1 && char.IsUpper(w[0]))
            .Distinct()
            .Select(w => new ExtractedEntity { Id = Guid.NewGuid().ToString(), Text = w, Type = NamedEntityType.Organization, Confidence = 0.9 })
            .ToList();
        return new EntityGraph { SourceId = content, Entities = entities, Relations = [] };
    }

    private static DocumentChunk Chunk(string id, string documentId, string content) => new()
    {
        Id = id,
        DocumentId = documentId,
        Content = content,
        ChunkIndex = 0
    };

    private EntityGraphService CreateService(IGraphStore? store) =>
        new(_extractor, null, store, NullLogger<EntityGraphService>.Instance);

    [Fact]
    public async Task SecondBuildOverTheSameChunks_ExtractsNothing_AndReturnsTheStoredEntities()
    {
        var chunks = new List<DocumentChunk>
        {
            Chunk("c1", "doc-a", "Acme partners with Globex."),
            Chunk("c2", "doc-a", "Globex opened an office.")
        };
        var service = CreateService(_store);

        var first = await service.BuildEntityGraphAsync(chunks, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(_extractedBatches);
        Assert.Equal(2, first.Entities.Count);
        Assert.Equal(2, first.Stats.ChunksExtracted);
        Assert.Equal(0, first.Stats.ChunksReused);

        var second = await service.BuildEntityGraphAsync(chunks, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(_extractedBatches); // no second extraction round
        Assert.Equal(0, second.Stats.ChunksExtracted);
        Assert.Equal(2, second.Stats.ChunksReused);
        Assert.Equal(first.Entities.Select(e => e.Id).Order(), second.Entities.Select(e => e.Id).Order());
        Assert.Equal(["c1", "c2"], second.SourceChunkIds.Order());
        // Provenance is reconstituted from the store, so chunk-scoped lookups on the result still work.
        Assert.Contains(second.ChunkMappings, m => m.ChunkId == "c1" && m.DocumentId == "doc-a");
        Assert.Contains(second.ChunkMappings, m => m.ChunkId == "c2" && m.DocumentId == "doc-a");
        Assert.Equal(2, _storedEntities.Count); // nothing re-persisted, nothing duplicated
    }

    [Fact]
    public async Task BuildOverPartlyNewChunks_ExtractsOnlyTheNewOnes_AndJoinsThemToTheStoredEntity()
    {
        var service = CreateService(_store);
        await service.BuildEntityGraphAsync(
        [
            Chunk("c1", "doc-a", "Acme partners with Globex."),
            Chunk("c2", "doc-a", "Globex opened an office.")
        ], cancellationToken: TestContext.Current.CancellationToken);
        var storedGlobexId = _storedEntities.Single(e => e.Name == "Globex").Id;
        _extractedBatches.Clear();

        // A re-index in which c1 is unchanged, c2 was rewritten (new id c3) and mentions Globex again.
        var result = await service.BuildEntityGraphAsync(
        [
            Chunk("c1", "doc-a", "Acme partners with Globex."),
            Chunk("c3", "doc-a", "Globex hired Initech.")
        ], cancellationToken: TestContext.Current.CancellationToken);

        var batch = Assert.Single(_extractedBatches);
        Assert.Equal(["Globex hired Initech."], batch);
        Assert.Equal(1, result.Stats.ChunksReused);
        Assert.Equal(1, result.Stats.ChunksExtracted);

        // The freshly extracted Globex joined the stored one instead of becoming a second entity.
        var globex = Assert.Single(result.Entities, e => e.Name == "Globex");
        Assert.Equal(storedGlobexId, globex.Id);
        Assert.Contains(result.ChunkMappings, m => m.EntityId == storedGlobexId && m.ChunkId == "c3");
        Assert.Contains(result.Entities, e => e.Name == "Initech");
        Assert.Contains(result.Entities, e => e.Name == "Acme");

        // Persisted provenance grows: the store keeps c1 and c2 and gains c3.
        var persisted = _storedEntities.Single(e => e.Name == "Globex");
        Assert.Equal(new[] { "c1", "c2", "c3" }, persisted.ChunkIds.Order());
        Assert.Equal(3, _storedEntities.Count); // Acme, Globex, Initech — no duplicate Globex
    }

    [Fact]
    public async Task ReuseDisabled_ExtractsEveryChunkAgain()
    {
        var chunks = new List<DocumentChunk> { Chunk("c1", "doc-a", "Acme partners with Globex.") };
        var service = CreateService(_store);
        var options = new EntityGraphBuildOptions { ReuseStoredExtractions = false };

        await service.BuildEntityGraphAsync(chunks, options, TestContext.Current.CancellationToken);
        await service.BuildEntityGraphAsync(chunks, options, TestContext.Current.CancellationToken);

        Assert.Equal(2, _extractedBatches.Count);
    }

    [Fact]
    public async Task WithoutAGraphStore_ExtractsEveryChunkAgain()
    {
        var chunks = new List<DocumentChunk> { Chunk("c1", "doc-a", "Acme partners with Globex.") };
        var service = CreateService(store: null);

        await service.BuildEntityGraphAsync(chunks, cancellationToken: TestContext.Current.CancellationToken);
        await service.BuildEntityGraphAsync(chunks, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, _extractedBatches.Count);
    }
}
