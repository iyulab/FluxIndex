namespace FluxIndex.Providers.OpenAI;

/// <summary>
/// The scale an OpenAI-compatible <c>/v1/rerank</c> endpoint answers <c>relevance_score</c> on.
/// The wire format does not say: hosted rerank APIs answer with a probability in (0, 1), while a
/// llama.cpp server started with <c>--rerank</c> answers with the cross-encoder's raw logit.
/// </summary>
public enum ScoreScale
{
    /// <summary>
    /// Decide per response: if any score lies outside [0, 1] the whole response is read as logits
    /// and mapped through a sigmoid; otherwise it is left as it is. A logit response whose values
    /// all happen to fall inside [0, 1] cannot be told apart — say <see cref="Logit"/> for such an endpoint.
    /// </summary>
    Auto = 0,

    /// <summary>The endpoint answers in (0, 1). Scores are never mapped.</summary>
    Probability = 1,

    /// <summary>The endpoint answers with raw logits. Every response is mapped through a sigmoid.</summary>
    Logit = 2
}
