using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// The <see cref="IEmbeddingService"/> in place when no embedding service is configured: FluxIndex then indexes and
/// searches keyword-only. Chunks are stored without vectors, the keyword index is the search index, hybrid search runs
/// its keyword leg alone, and a vector search fails with a message naming how to add an embedder. Every embedding call
/// throws <see cref="InvalidOperationException"/>; nothing ever writes a placeholder vector.
/// </summary>
/// <remarks>
/// Code that can run without vectors checks <see cref="IsKeywordOnly"/> rather than catching the exception. Adding an
/// embedding service later and calling the indexer's embedding backfill fills the vectors of chunks indexed this way.
/// </remarks>
public sealed class NoEmbeddingService : IEmbeddingService
{
    /// <summary>The shared instance.</summary>
    public static NoEmbeddingService Instance { get; } = new();

    private NoEmbeddingService()
    {
    }

    /// <summary>Whether <paramref name="embeddingService"/> is the keyword-only placeholder (or absent).</summary>
    public static bool IsKeywordOnly(IEmbeddingService? embeddingService) => embeddingService is null or NoEmbeddingService;

    /// <summary>The message every refused embedding call carries.</summary>
    public const string NotConfiguredMessage =
        "No embedding service is configured, so this FluxIndex context is keyword-only: vector search needs an " +
        "embedder. Register one with UseEmbeddingService(...) or an embedding provider package " +
        "(UseInMemoryEmbedding() gives deterministic test vectors), or use keyword search.";

    /// <inheritdoc />
    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(NotConfiguredMessage);

    /// <inheritdoc />
    public Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(NotConfiguredMessage);

    /// <summary>0: there is no vector space.</summary>
    public int GetEmbeddingDimension() => 0;

    /// <summary><c>"none"</c>.</summary>
    public string GetModelName() => "none";

    /// <summary>No embedder limits the input.</summary>
    public int GetMaxTokens() => int.MaxValue;

    /// <summary>An estimate from the word count (about 0.75 words per token), as there is no tokenizer.</summary>
    public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default)
    {
        var words = string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return Task.FromResult((int)Math.Ceiling(words / 0.75));
    }

    /// <summary>There is no vector space, so there is no identity: throws, like the embedding calls.</summary>
    public EmbeddingIdentity GetIdentity() => throw new InvalidOperationException(NotConfiguredMessage);
}
