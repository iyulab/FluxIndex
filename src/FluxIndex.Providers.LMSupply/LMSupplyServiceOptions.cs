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
    /// <summary>LMSupply loader options.</summary>
    public RerankerOptions? Reranker { get; set; }
}

/// <summary>Options for <see cref="Extensions.ServiceCollectionExtensions.AddLMSupplyTextCompletion(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{LMSupplyTextCompletionOptions})"/>.</summary>
public sealed class LMSupplyTextCompletionOptions : LMSupplyServiceOptionsBase
{
    /// <summary>LMSupply loader options.</summary>
    public GeneratorOptions? Generator { get; set; }
}
