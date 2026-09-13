using AwesomeAssertions;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// <c>WithSearchOptions(defaultMaxResults, defaultMinScore)</c> is documented in the guide and wrote into
/// <see cref="RetrieverOptions"/> — which no search method read: every overload carried its own constant
/// default (10 and 0.2, or 0.5 on the <see cref="IFluxIndexContext"/> interface). The defaults now come from
/// the options whenever a caller omits the argument.
/// </summary>
public class RetrieverDefaultsHonouredTests
{
    private static async Task<IFluxIndexContext> ContextWithFiveDocumentsAsync(Action<FluxIndexContextBuilder>? configure = null)
    {
        var builder = FluxIndexContext.CreateBuilder().UseInMemoryEmbedding().SuppressStartupMessages();
        configure?.Invoke(builder);
        var context = builder.Build();
        for (var i = 1; i <= 5; i++)
        {
            await context.Indexer.IndexDocumentAsync($"Document number {i} about retrieval defaults.", $"doc-{i}", cancellationToken: TestContext.Current.CancellationToken);
        }
        return context;
    }

    [Fact]
    public async Task WithSearchOptions_DefaultMaxResults_CapsAnArgumentlessSearch()
    {
        var context = await ContextWithFiveDocumentsAsync(b => b.WithSearchOptions(defaultMaxResults: 2, defaultMinScore: -1f));

        var results = await context.Retriever.SearchAsync("retrieval defaults", cancellationToken: TestContext.Current.CancellationToken);
        var viaContext = await context.SearchAsync("retrieval defaults", cancellationToken: TestContext.Current.CancellationToken);

        results.Should().HaveCount(2, "the builder's default, not the former constant 10, decides when maxResults is omitted");
        viaContext.Should().HaveCount(2, "the context facade must forward the omission rather than substitute its own constant");
    }

    [Fact]
    public async Task WithSearchOptions_DefaultMinScore_FiltersAnArgumentlessSearch_ButAnExplicitArgumentWins()
    {
        var context = await ContextWithFiveDocumentsAsync(b => b.WithSearchOptions(defaultMaxResults: 10, defaultMinScore: 0.999f));

        var byDefault = await context.Retriever.SearchAsync("retrieval defaults", cancellationToken: TestContext.Current.CancellationToken);
        var explicitZero = await context.Retriever.SearchAsync("retrieval defaults", minScore: -1f, cancellationToken: TestContext.Current.CancellationToken);

        byDefault.Should().BeEmpty("a 0.999 default threshold admits nothing from the testing embedder");
        explicitZero.Should().HaveCount(5, "an explicit argument still overrides the builder default");
    }

    [Fact]
    public async Task WithoutWithSearchOptions_TheEffectiveDefaultsAreUnchanged()
    {
        // Callers who never touched WithSearchOptions got 10 / 0.2 before; the option defaults are pinned to
        // those values so wiring them in changes nothing for them (the declared 0.5 was never what ran).
        new RetrieverOptions().DefaultMaxResults.Should().Be(10);
        new RetrieverOptions().DefaultMinScore.Should().Be(0.2f);

        var context = await ContextWithFiveDocumentsAsync();
        var viaInterface = await ((IFluxIndexContext)context).SearchAsync("retrieval defaults", minScore: 0f, cancellationToken: TestContext.Current.CancellationToken);
        var viaClass = await context.SearchAsync("retrieval defaults", minScore: 0f, cancellationToken: TestContext.Current.CancellationToken);
        viaInterface.Should().HaveCount(viaClass.Count(), "interface and class no longer carry different default thresholds");
    }
}
