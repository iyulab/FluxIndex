using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.SDK.Services;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Runs the shared IVectorStore filter-contract suite against the SDK's InMemoryVectorStore.
/// </summary>
public class InMemoryVectorStoreFilterContractTests : VectorStoreFilterContractSuite
{
    protected override Task<IVectorStore> CreateStoreAsync()
        => Task.FromResult<IVectorStore>(new InMemoryVectorStore());
}
