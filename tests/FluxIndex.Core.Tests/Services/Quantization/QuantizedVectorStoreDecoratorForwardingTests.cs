using System.Reflection;
using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Quantization;

/// <summary>
/// A decorator answers for the store it wraps. <see cref="IVectorStore"/> members with a default body
/// (no-op, constant, NotSupported) used to fall through to that default on the decorator:
/// <c>BindIdentity</c> never reached the inner store, health was always true, the document count 0.
/// </summary>
public class QuantizedVectorStoreDecoratorForwardingTests
{
    private readonly IVectorStore _inner = Substitute.For<IVectorStore>();

    private QuantizedVectorStoreDecorator CreateDecorator() => new(
        _inner, Substitute.For<IVectorQuantizer>(), Substitute.For<ILogger<QuantizedVectorStoreDecorator>>(),
        new QuantizedVectorStoreOptions { AutoQuantizeOnStore = false });

    [Fact]
    public void Declares_every_IVectorStore_member_itself()
    {
        // A member left to the interface default is answered by the default, not by the wrapped store.
        var map = typeof(QuantizedVectorStoreDecorator).GetInterfaceMap(typeof(IVectorStore));
        var servedByDefault = map.InterfaceMethods
            .Where((_, i) => map.TargetMethods[i].DeclaringType == typeof(IVectorStore))
            .Select(m => m.Name)
            .ToList();

        servedByDefault.Should().BeEmpty();
    }

    [Fact]
    public void Identity_members_reach_the_inner_store()
    {
        var identity = new EmbeddingIdentity { Provider = "LMSupply", Model = "multilingual-e5-base", Dimension = 768 };
        _inner.BoundIdentity.Returns(identity);
        _inner.ResolvedStoreName.Returns("chunks_e5");
        _inner.DetectedDimension.Returns(768);
        var decorator = CreateDecorator();

        decorator.BindIdentity(identity);

        _inner.Received(1).BindIdentity(identity);
        decorator.BoundIdentity.Should().Be(identity);
        decorator.ResolvedStoreName.Should().Be("chunks_e5");
        decorator.DetectedDimension.Should().Be(768);
    }

    [Fact]
    public async Task Health_count_and_presence_are_the_inner_store_answers()
    {
        _inner.VerifyHealthAsync(Arg.Any<CancellationToken>()).Returns(false);
        _inner.GetDistinctDocumentCountAsync(Arg.Any<CancellationToken>()).Returns(42);
        _inner.HasVectorsForDocumentAsync("doc", Arg.Any<CancellationToken>()).Returns(true);
        var decorator = CreateDecorator();

        (await decorator.VerifyHealthAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        (await decorator.GetDistinctDocumentCountAsync(TestContext.Current.CancellationToken)).Should().Be(42);
        (await decorator.HasVectorsForDocumentAsync("doc", TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteByFilter_reaches_the_inner_store_and_drops_quantized_copies_of_deleted_chunks()
    {
        var filter = new Dictionary<string, object> { ["tenant"] = "a" };
        _inner.DeleteByFilterAsync(filter, Arg.Any<CancellationToken>()).Returns(1);
        _inner.StoreAsync(Arg.Any<FluxIndex.Core.Domain.Entities.DocumentChunk>(), Arg.Any<CancellationToken>())
            .Returns("gone", "kept");
        _inner.ExistsAsync("gone", Arg.Any<CancellationToken>()).Returns(false);
        _inner.ExistsAsync("kept", Arg.Any<CancellationToken>()).Returns(true);
        var decorator = CreateDecorator();
        var q = new QuantizedVector();
        await decorator.StoreWithQuantizedAsync(new FluxIndex.Core.Domain.Entities.DocumentChunk(), q, TestContext.Current.CancellationToken);
        await decorator.StoreWithQuantizedAsync(new FluxIndex.Core.Domain.Entities.DocumentChunk(), q, TestContext.Current.CancellationToken);

        (await decorator.DeleteByFilterAsync(filter, TestContext.Current.CancellationToken)).Should().Be(1);

        var cache = (System.Collections.IDictionary)typeof(QuantizedVectorStoreDecorator)
            .GetField("_quantizedEmbeddings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(decorator)!;
        cache.Contains("gone").Should().BeFalse();
        cache.Contains("kept").Should().BeTrue();
    }
}
