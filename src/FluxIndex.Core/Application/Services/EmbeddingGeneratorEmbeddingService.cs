using FluxIndex.Core.Application.Services.Base;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Settings for <see cref="EmbeddingGeneratorEmbeddingService"/> that a generator's metadata cannot supply.
/// </summary>
public sealed class EmbeddingGeneratorServiceOptions
{
    /// <summary>
    /// The vector size. Required when the generator's <see cref="EmbeddingGeneratorMetadata.DefaultModelDimensions"/>
    /// is null; when both are set they must agree (checked whenever the identity is read).
    /// </summary>
    public int? Dimensions { get; set; }

    /// <summary>
    /// A pipeline revision that changes the vectors for the same model (see
    /// <see cref="Domain.ValueObjects.EmbeddingIdentity.Revision"/>). Part of the index fingerprint.
    /// </summary>
    public string? Revision { get; set; }

    /// <summary>
    /// The query/passage convention revision (see <see cref="Domain.ValueObjects.EmbeddingIdentity.VectorSpaceRevision"/>).
    /// </summary>
    public string? VectorSpaceRevision { get; set; }
}

/// <summary>
/// An <see cref="Interfaces.IEmbeddingService"/> over <c>Microsoft.Extensions.AI</c> embedding generators, so any
/// generator (OpenAI, Azure OpenAI, Ollama, LMSupply's <c>LocalEmbeddingGenerator</c>, IronHive's adapter) plugs into
/// FluxIndex without an adapter class.
/// </summary>
/// <remarks>
/// <para>
/// <c>IEmbeddingGenerator</c> has no query/document distinction, so the role is carried by the instance: pass the
/// generator that embeds stored text as <c>documents</c> and, for an asymmetric model (E5, BGE, Qwen3-Embedding),
/// the one that applies the query convention as <c>queries</c>. Without <c>queries</c>, queries are embedded by
/// <c>documents</c> (a symmetric model).
/// </para>
/// <para>
/// The index identity is the documents generator's provider and model id plus the dimension. It is read from the
/// generator's <see cref="EmbeddingGeneratorMetadata"/> each time it is asked for, not once at construction, so a host
/// whose model is chosen at run time (a settings screen, a fallback chain) can register the service before a model is
/// set. Asking for the identity, or embedding, while the generator reports no model id or no dimension throws: two
/// models would otherwise share an index fingerprint. A store already bound to one identity refuses another
/// (<see cref="Domain.Exceptions.EmbeddingModelMismatchException"/>) - switching the model of an index means
/// re-indexing it, or giving the new model its own collection.
/// The service does not dispose the generators.
/// </para>
/// </remarks>
public sealed class EmbeddingGeneratorEmbeddingService : EmbeddingServiceBase
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _documents;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _queries;
    private readonly int? _dimensions;
    private readonly string? _vectorSpaceRevision;

    /// <summary>Creates the service.</summary>
    /// <param name="documents">Embeds stored text (documents, chunks, summaries).</param>
    /// <param name="queries">Embeds search queries; null uses <paramref name="documents"/>.</param>
    /// <param name="options">Dimension and revisions the metadata does not carry.</param>
    /// <exception cref="ArgumentException"><see cref="EmbeddingGeneratorServiceOptions.Dimensions"/> is not positive.</exception>
    public EmbeddingGeneratorEmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> documents,
        IEmbeddingGenerator<string, Embedding<float>>? queries = null,
        EmbeddingGeneratorServiceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
        _queries = queries ?? documents;
        options ??= new EmbeddingGeneratorServiceOptions();

        if (options.Dimensions is <= 0)
            throw new ArgumentException($"The dimension must be positive (got {options.Dimensions}).", nameof(options));
        _dimensions = options.Dimensions;

        Revision = options.Revision;
        _vectorSpaceRevision = options.VectorSpaceRevision;
    }

    /// <summary>
    /// The provider, model id and dimension the documents generator reports now, with
    /// <see cref="EmbeddingGeneratorServiceOptions.Dimensions"/> in place of a dimension it does not report.
    /// </summary>
    /// <exception cref="InvalidOperationException">The model id or dimension is unknown, or the two dimensions disagree.</exception>
    private (string Provider, string Model, int Dimension) Describe()
    {
        var metadata = _documents.GetService<EmbeddingGeneratorMetadata>();
        if (metadata?.DefaultModelId is not { Length: > 0 } model)
        {
            throw new InvalidOperationException(
                "The embedding generator does not report a model id (EmbeddingGeneratorMetadata.DefaultModelId), " +
                "so the index cannot be fingerprinted. Configure the generator with a default model id before it is used.");
        }

        var reported = metadata.DefaultModelDimensions;
        if (_dimensions is int set && reported is int known && set != known)
        {
            throw new InvalidOperationException(
                $"EmbeddingGeneratorServiceOptions.Dimensions is {set} but the generator reports {known} for model '{model}'.");
        }

        var dimension = _dimensions ?? reported ?? throw new InvalidOperationException(
            $"The embedding generator does not report a dimension for model '{model}'. " +
            "Set EmbeddingGeneratorServiceOptions.Dimensions, or report EmbeddingGeneratorMetadata.DefaultModelDimensions.");
        if (dimension <= 0)
            throw new InvalidOperationException($"The embedding generator reports dimension {dimension} for model '{model}'.");

        return (metadata.ProviderName is { Length: > 0 } provider ? provider : "Microsoft.Extensions.AI", model, dimension);
    }

    /// <inheritdoc />
    protected override Task<float[]> EmbedCoreAsync(string text, CancellationToken cancellationToken) =>
        EmbedOneAsync(_documents, text, cancellationToken);

    /// <inheritdoc />
    protected override Task<float[]> EmbedQueryCoreAsync(string query, CancellationToken cancellationToken) =>
        EmbedOneAsync(_queries, query, cancellationToken);

    /// <summary>Embeds every text in one generator call; empty texts get an empty vector, like the single call.</summary>
    public override async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var all = texts.ToList();
        var results = new float[all.Count][];
        var positions = new List<int>(all.Count);
        var inputs = new List<string>(all.Count);
        for (var i = 0; i < all.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(all[i]))
            {
                results[i] = [];
                continue;
            }
            positions.Add(i);
            inputs.Add(all[i]);
        }

        if (inputs.Count > 0)
        {
            var expected = Describe();
            var generated = await _documents.GenerateAsync(inputs, options: null, cancellationToken).ConfigureAwait(false);
            if (generated.Count != inputs.Count)
            {
                throw new InvalidOperationException(
                    $"The embedding generator returned {generated.Count} embedding(s) for {inputs.Count} input(s). " +
                    "Results are positional, so a partial set cannot be matched to its inputs.");
            }
            for (var k = 0; k < positions.Count; k++)
                results[positions[k]] = Checked(generated[k], expected);
        }

        return results;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The generator reports no dimension and none is configured.</exception>
    public override int GetEmbeddingDimension() => Describe().Dimension;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The generator reports no model id.</exception>
    public override string GetModelName() => Describe().Model;

    /// <inheritdoc />
    protected override string GetProviderName() => Describe().Provider;

    /// <inheritdoc />
    protected override string? GetVectorSpaceRevision() => _vectorSpaceRevision;

    private async Task<float[]> EmbedOneAsync(
        IEmbeddingGenerator<string, Embedding<float>> generator, string text, CancellationToken cancellationToken)
    {
        var expected = Describe();
        var generated = await generator.GenerateAsync([text], options: null, cancellationToken).ConfigureAwait(false);
        if (generated.Count != 1)
        {
            throw new InvalidOperationException(
                $"The embedding generator returned {generated.Count} embedding(s) for one input.");
        }
        return Checked(generated[0], expected);
    }

    private static float[] Checked(Embedding<float> embedding, (string Provider, string Model, int Dimension) expected)
    {
        var vector = embedding.Vector.ToArray();
        if (vector.Length != expected.Dimension)
        {
            throw new InvalidOperationException(
                $"The embedding generator returned a {vector.Length}-dimension vector; the index expects {expected.Dimension} " +
                $"(model '{expected.Model}'). Set EmbeddingGeneratorServiceOptions.Dimensions to the model's size.");
        }
        return vector;
    }
}

/// <summary>DI registration for <see cref="EmbeddingGeneratorEmbeddingService"/>.</summary>
public static class EmbeddingGeneratorServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="Interfaces.IEmbeddingService"/> over the container's
    /// <c>IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="queriesServiceKey">
    /// The key of a keyed generator that embeds queries (an asymmetric model's query side); null embeds queries with
    /// the unkeyed generator.
    /// </param>
    /// <param name="configure">Dimension and revisions the generator's metadata does not carry.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEmbeddingGeneratorEmbedding(
        this IServiceCollection services,
        object? queriesServiceKey = null,
        Action<EmbeddingGeneratorServiceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new EmbeddingGeneratorServiceOptions();
        configure?.Invoke(options);
        services.AddSingleton<Interfaces.IEmbeddingService>(sp => new EmbeddingGeneratorEmbeddingService(
            sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            queriesServiceKey is null
                ? null
                : sp.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(queriesServiceKey),
            options));
        return services;
    }
}
