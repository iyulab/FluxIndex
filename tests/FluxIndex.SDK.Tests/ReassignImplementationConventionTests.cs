using System.Reflection;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Base;
using FluxIndex.Core.Domain.Models;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Every store and index FluxIndex ships implements document reassignment for real. The interface members carry a
/// throwing default so that implementations outside this repository keep compiling; without this check a new store in
/// this repository could inherit that default and fail only when a consumer first moves a document.
/// </summary>
public class ReassignImplementationConventionTests
{
    private static readonly string[] ShippedAssemblies =
    [
        "FluxIndex.Core",
        "FluxIndex.SDK",
        "FluxIndex.Storage.SQLite",
        "FluxIndex.Storage.PostgreSQL",
        "FluxIndex.Storage.Qdrant",
        "FluxIndex.Storage.Neo4j",
    ];

    private static IEnumerable<Type> ConcreteImplementations(Type contract) =>
        ShippedAssemblies
            .Select(Assembly.Load)
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && contract.IsAssignableFrom(t));

    private static List<string> TypesFallingBackToTheDefault(Type contract, string methodName, params Type[] fallbackOwners) =>
        ConcreteImplementations(contract)
            .Where(type => FallsBackToTheDefault(type, contract, methodName, fallbackOwners))
            .Select(type => type.FullName!)
            .ToList();

    private static bool FallsBackToTheDefault(Type type, Type contract, string methodName, params Type[] fallbackOwners)
    {
        var map = type.GetInterfaceMap(contract);
        var index = Array.IndexOf(map.InterfaceMethods, contract.GetMethod(methodName)!);
        var owner = map.TargetMethods[index].DeclaringType;
        return owner == contract || fallbackOwners.Contains(owner);
    }

    // Positive control: a store that inherits the base-class default is what the check exists to catch.
    [Fact]
    public void TheCheck_FlagsAStoreThatInheritsTheDefault()
    {
        Assert.True(FallsBackToTheDefault(
            typeof(DefaultOnlyStore), typeof(IVectorStore), nameof(IVectorStore.ReassignDocumentAsync), typeof(VectorStoreBase)));
        Assert.False(FallsBackToTheDefault(
            typeof(Services.InMemoryVectorStore), typeof(IVectorStore), nameof(IVectorStore.ReassignDocumentAsync), typeof(VectorStoreBase)));
    }

    private sealed class DefaultOnlyStore : VectorStoreBase
    {
        protected override Task<string> StoreCoreAsync(Core.Domain.Entities.DocumentChunk chunk, CancellationToken cancellationToken) => throw new NotSupportedException();
        protected override Task<Core.Domain.Entities.DocumentChunk?> GetCoreAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        protected override Task<IEnumerable<Core.Application.Utilities.ScoredChunk>> SearchCoreAsync(float[] queryEmbedding, int topK, Dictionary<string, object>? filters, CancellationToken cancellationToken) => throw new NotSupportedException();
        protected override Task<bool> DeleteCoreAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        protected override Task<bool> UpdateCoreAsync(Core.Domain.Entities.DocumentChunk chunk, CancellationToken cancellationToken) => throw new NotSupportedException();
        protected override Task<IEnumerable<Core.Domain.Entities.DocumentChunk>> GetByDocumentIdCoreAsync(string documentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        protected override Task<bool> DeleteByDocumentIdCoreAsync(string documentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        protected override Task<int> CountCoreAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        protected override Task ClearCoreAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public void EveryShippedVectorStore_ImplementsReassignDocument()
    {
        // The scan reaches every storage assembly, not only the one that happens to be loaded first.
        var scanned = ConcreteImplementations(typeof(IVectorStore)).Select(t => t.Name).ToList();
        Assert.Contains("InMemoryVectorStore", scanned);
        Assert.Contains("SQLiteVecVectorStore", scanned);
        Assert.Contains("PostgreSQLQuantizedVectorStore", scanned);
        Assert.Contains("QdrantVectorStore", scanned);
        Assert.Empty(TypesFallingBackToTheDefault(
            typeof(IVectorStore), nameof(IVectorStore.ReassignDocumentAsync), typeof(VectorStoreBase)));
    }

    [Fact]
    public void EveryShippedKeywordIndex_ImplementsReassignDocument()
    {
        Assert.NotEmpty(ConcreteImplementations(typeof(IKeywordSearchService)));
        Assert.Empty(TypesFallingBackToTheDefault(
            typeof(IKeywordSearchService), nameof(IKeywordSearchService.ReassignDocumentAsync)));
    }

    // The defaults read entities and write them back, which replaces over a chunk a concurrent build adds; every shipped
    // graph store must do it as one write against what is stored.
    [Theory]
    [InlineData(nameof(IGraphStore.RemoveEntityChunksAsync))]
    [InlineData(nameof(IGraphStore.RemapEntityChunksAsync))]
    public void EveryShippedGraphStore_OverridesTheReadThenWriteDefault(string method)
    {
        var scanned = ConcreteImplementations(typeof(IGraphStore)).Select(t => t.Name).ToList();
        Assert.Contains("SQLiteEntityGraphStore", scanned);
        Assert.Contains("PostgresEntityGraphStore", scanned);
        Assert.Contains("Neo4jGraphStore", scanned);
        Assert.Empty(TypesFallingBackToTheDefault(typeof(IGraphStore), method));
    }

    [Fact]
    public void EveryShippedGraphRagService_ImplementsReassignChunks()
    {
        Assert.NotEmpty(ConcreteImplementations(typeof(IGraphRAGService)));
        Assert.Empty(TypesFallingBackToTheDefault(
            typeof(IGraphRAGService), nameof(IGraphRAGService.ReassignChunksAsync)));
    }
}
