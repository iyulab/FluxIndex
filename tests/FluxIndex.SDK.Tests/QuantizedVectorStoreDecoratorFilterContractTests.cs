using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.SDK.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Runs the quantized-path filter contract against <see cref="QuantizedVectorStoreDecorator"/> over the SDK's
/// InMemoryVectorStore. The decorator holds quantized embeddings without metadata, so it checks a filter on the chunks it
/// fetches — these facts hold it to returning the scope's nearest chunks, not the nearest chunks overall.
/// </summary>
public class QuantizedVectorStoreDecoratorFilterContractTests : QuantizedVectorStoreFilterContractSuite
{
    protected override Task<IQuantizedVectorStore> CreateStoreAsync()
        => Task.FromResult<IQuantizedVectorStore>(new QuantizedVectorStoreDecorator(
            new InMemoryVectorStore(), Quantizer, NullLogger<QuantizedVectorStoreDecorator>.Instance));
}
