using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.SDK.Services;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Runs the shared IVectorStore chunk-identity contract suite against the SDK's InMemoryVectorStore.
/// </summary>
public class InMemoryVectorStoreChunkIdentityContractTests : VectorStoreChunkIdentityContractSuite
{
    protected override Task<IVectorStore> CreateStoreAsync()
        => Task.FromResult<IVectorStore>(new InMemoryVectorStore());
}
