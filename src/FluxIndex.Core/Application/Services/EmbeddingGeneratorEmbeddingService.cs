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
    /// is null; when both are set they must agree.
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
/// The index identity is the documents generator's provider and model id plus the dimension. Construction fails
/// when the model id or the dimension is unknown: two models would otherwise share an index fingerprint.
/// The service does not dispose the generators.
/// </para>
/// </remarks>
public sealed class EmbeddingGeneratorEmbeddingService : EmbeddingServiceBase
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _documents;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _queries;
    private readonly string _provider;
    private readonly string _model;
    private readonly int _dimension;
    private readonly string? _vectorSpaceRevision;

    /// <summary>Creates the service.</summary>
    /// <param name="documents">Embeds stored text (documents, chunks, summaries).</param>
    /// <param name="queries">Embeds search queries; null uses <paramref name="documents"/>.</param>
    /// <param name="options">Dimension and revisions the metadata does not carry.</param>
    /// <exception cref="ArgumentException">The model id or dimension is unknown, or the two dimensions disagree.</exception>
    public EmbeddingGeneratorEmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> documents,
        IEmbeddingGenerator<string, Embedding<float>>? queries = null,
        EmbeddingGeneratorServiceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
        _queries = queries ?? documents;
        options ??= new EmbeddingGeneratorServiceOptions();

        var metadata = documents.GetService<EmbeddingGeneratorMetadata>();
        if (metadata?.DefaultModelId is not { Length: > 0 } model)
        {
            throw new ArgumentException(
                "The embedding generator does not report a model id (EmbeddingGeneratorMetadata.DefaultModelId), " +
                "so the index cannot be fingerprinted. Configure the generator with a default model id.",
                nameof(documents));
        }
        _model = model;
        _provider = metadata.ProviderName is { Length: > 0 } provider ? provider : "Microsoft.Extensions.AI";

        var reported = metadata.DefaultModelDimensions;
        if (options.Dimensions is int set && reported is int known && set != known)
        {
            throw new ArgumentException(
                $"EmbeddingGeneratorServiceOptions.Dimensions is {set} but the generator reports {known} for model '{_model}'.",
                nameof(options));
        }
        _dimension = options.Dimensions ?? reported ?? throw new ArgumentException(
            $"The embedding generator does not report a dimension for model '{_model}'. " +
            "Set EmbeddingGeneratorServiceOptions.Dimensions.",
            nameof(options));
        if (_dimension <= 0)
            throw new ArgumentException($"The dimension must be positive (got {_dimension}).", nameof(options));

        Revision = options.Revision;
        _vectorSpaceRevision = options.VectorSpaceRevision;
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
            var generated = await _documents.GenerateAsync(inputs, options: null, cancellationToken).ConfigureAwait(false);
            if (generated.Count != inputs.Count)
            {
                throw new InvalidOperationException(
                    $"The embedding generator returned {generated.Count} embedding(s) for {inputs.Count} input(s). " +
                    "Results are positional, so a partial set cannot be matched to its inputs.");
            }
            for (var k = 0; k < positions.Count; k++)
                results[positions[k]] = Checked(generated[k]);
        }

        return results;
    }

    /// <inheritdoc />
    public override int GetEmbeddingDimension() => _dimension;

    /// <inheritdoc />
    public override string GetModelName() => _model;

    /// <inheritdoc />
    protected override string GetProviderName() => _provider;

    /// <inheritdoc />
    protected override string? GetVectorSpaceRevision() => _vectorSpaceRevision;

    private async Task<float[]> EmbedOneAsync(
        IEmbeddingGenerator<string, Embedding<float>> generator, string text, CancellationToken cancellationToken)
    {
        var generated = await generator.GenerateAsync([text], options: null, cancellationToken).ConfigureAwait(false);
        if (generated.Count != 1)
        {
            throw new InvalidOperationException(
                $"The embedding generator returned {generated.Count} embedding(s) for one input.");
        }
        return Checked(generated[0]);
    }

    private float[] Checked(Embedding<float> embedding)
    {
        var vector = embedding.Vector.ToArray();
        if (vector.Length != _dimension)
        {
            throw new InvalidOperationException(
                $"The embedding generator returned a {vector.Length}-dimension vector; the index expects {_dimension} " +
                $"(model '{_model}'). Set EmbeddingGeneratorServiceOptions.Dimensions to the model's size.");
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
