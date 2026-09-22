using LMSupply;
using LMSupply.Embedder;
using LMSupply.Generator;
using LMSupply.Reranker;

namespace FluxIndex.Providers.LMSupply;

/// <summary>
/// Options shared by the lazily loading LMSupply services. The model is loaded — and downloaded
/// first when it is not cached — on first use, or at host start with <see cref="WarmUpOnStart"/>;
/// building the container never blocks on it.
/// </summary>
public abstract class LMSupplyServiceOptionsBase
{
    /// <summary>LMSupply catalog alias (e.g. "default", "fast") or model id / path.</summary>
    public string ModelId { get; set; } = "default";

    /// <summary>Receives download progress while the model is fetched on first load.</summary>
    public IProgress<DownloadProgress>? Progress { get; set; }

    /// <summary>
    /// Upper bound for one load (download + initialization). <c>null</c> means no timeout. When it
    /// elapses the load fails with a <see cref="TimeoutException"/>; the next use retries.
    /// </summary>
    public TimeSpan? LoadTimeout { get; set; }

    /// <summary>
    /// Load the model when the host starts (an <c>IHostedService</c> is registered) instead of on the
    /// first call, so the first request does not pay the download and a startup failure is visible at
    /// startup. Requires a Generic Host.
    /// </summary>
    public bool WarmUpOnStart { get; set; }
}

/// <summary>Options for <see cref="Extensions.ServiceCollectionExtensions.AddLMSupplyEmbedding(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{LMSupplyEmbeddingOptions})"/>.</summary>
public sealed class LMSupplyEmbeddingOptions : LMSupplyServiceOptionsBase
{
    /// <summary>
    /// Pipeline revision — raise it when an upgrade makes this model's vectors incomparable with what is
    /// already indexed, so the collection separates instead of mixing.
    /// </summary>
    public string? Revision { get; set; }

    /// <summary>
    /// Fold the loaded model's <c>IEmbeddingModel.VectorSpaceRevision</c> (LMSupply 0.71.0+ — derived from what the
    /// loader actually did: tokenizer, pooling, normalization, sequence length, model file) into
    /// <see cref="Revision"/> and so into the <c>EmbeddingIdentity.Fingerprint</c>, when <see cref="Revision"/> is not
    /// set by hand. Default: false — the fingerprint stays what it was, and the value is only reported on
    /// <c>EmbeddingIdentity.VectorSpaceRevision</c> for you to store and compare.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Turning this on moves the collection/table name once</b> (the first fingerprint with the revision folded
    /// in — a re-embed), and again whenever an LMSupply release changes this model's vector space. That is the
    /// point: stale vectors are never mixed with new ones. Plan the re-index before enabling it.
    /// </para>
    /// <para>
    /// The identity needs the value before anything reads it. <c>AddLMSupplyEmbedding</c> reads it from the cached model
    /// files at host start (no load — the model still loads on first use); only when the files cannot answer (not cached,
    /// GGUF, dimension declared nowhere) does the host load the model instead. Reading the identity before either has
    /// happened throws (an identity announced without the revision would name a different collection than the identity
    /// after it). A hand-set <see cref="Revision"/> wins and hides loader changes — that is the consumer's choice.
    /// </para>
    /// </remarks>
    public bool UseVectorSpaceRevision { get; set; }

    /// <summary>LMSupply loader options (execution provider, cache directory, ...).</summary>
    public EmbedderOptions? Embedder { get; set; }

    /// <summary>
    /// The vector dimension to announce before the model is loaded. Catalog models announce it from the
    /// LMSupply registry automatically; a HuggingFace repo id or a local path has no registry entry, so
    /// set this (or warm the model up) before anything asks for the embedding identity. Verified against
    /// the loaded model — a wrong value fails the load rather than silently splitting the index.
    /// </summary>
    public int? Dimensions { get; set; }

    /// <summary>
    /// The model name to announce before the model is loaded, when the automatic derivation from
    /// <see cref="LMSupplyServiceOptionsBase.ModelId"/> is not wanted. Verified against the loaded model.
    /// </summary>
    public string? ModelName { get; set; }
}

/// <summary>Options for <see cref="Extensions.ServiceCollectionExtensions.AddLMSupplyReranker(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{LMSupplyRerankerOptions})"/>.</summary>
public sealed class LMSupplyRerankerOptions : LMSupplyServiceOptionsBase
{
    /// <summary>
    /// Creates reranker options whose <see cref="LMSupplyServiceOptionsBase.ModelId"/> is <c>"auto"</c>: LMSupply picks
    /// the reranker by hardware tier — multilingual on Medium and above. The English-only <c>"default"</c> cross-encoder
    /// ranks an unrelated passage in the query's language above a passage in another language that answers it, which a
    /// corpus that is not English-only hits on the first query.
    /// </summary>
    public LMSupplyRerankerOptions() => ModelId = "auto";

    /// <summary>LMSupply loader options.</summary>
    public RerankerOptions? Reranker { get; set; }
}

/// <summary>Options for <see cref="Extensions.ServiceCollectionExtensions.AddLMSupplyTextCompletion(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{LMSupplyTextCompletionOptions})"/>.</summary>
public sealed class LMSupplyTextCompletionOptions : LMSupplyServiceOptionsBase
{
    /// <summary>LMSupply loader options.</summary>
    public GeneratorOptions? Generator { get; set; }
}
