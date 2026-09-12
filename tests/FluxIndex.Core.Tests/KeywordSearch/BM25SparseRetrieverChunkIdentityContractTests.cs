using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Services;
using FluxIndex.Core.Tests.Contract;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxIndex.Core.Tests.KeywordSearch;

/// <summary>
/// Runs the shared keyword-index chunk-identity contract suite against the in-memory BM25 index.
/// </summary>
public class BM25SparseRetrieverChunkIdentityContractTests : KeywordSearchChunkIdentityContractSuite
{
    protected override Task<IKeywordSearchService> CreateServiceAsync()
        => Task.FromResult<IKeywordSearchService>(new BM25SparseRetriever(NullLogger<BM25SparseRetriever>.Instance));
}
