using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Application.Services.Quantization;
using FluxIndex.Core.Tests.Contract;
using FluxIndex.SDK.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Runs the shared reassignment contract suite against the SDK's InMemoryVectorStore.
/// </summary>
public class InMemoryVectorStoreReassignContractTests : VectorStoreReassignContractSuite
{
    protected override Task<IVectorStore> CreateStoreAsync()
        => Task.FromResult<IVectorStore>(new InMemoryVectorStore());
}

/// <summary>
/// Runs the shared reassignment contract suite through the quantization decorator, which must forward the call and
/// keep its own quantized copies under the new ids.
/// </summary>
public class QuantizedDecoratorReassignContractTests : VectorStoreReassignContractSuite
{
    protected override Task<IVectorStore> CreateStoreAsync()
        => Task.FromResult<IVectorStore>(new QuantizedVectorStoreDecorator(
            new InMemoryVectorStore(),
            new ScalarQuantizer(Options.Create(new QuantizationOptions()), NullLogger<ScalarQuantizer>.Instance),
            NullLogger<QuantizedVectorStoreDecorator>.Instance));
}
