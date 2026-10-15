using System.Text.Json;

namespace FluxIndex.Core.Domain.ValueObjects;

/// <summary>
/// The form in which a vector store records the <see cref="EmbeddingIdentity"/> of the vectors it holds: the members that
/// decide whether two identities are the same vector space (provider, model, dimension, revision), as JSON. A store that
/// records it can refuse another model after a restart, not only within one process.
/// </summary>
public static class EmbeddingIdentityRecord
{
    private sealed record Record(string Provider, string Model, int Dimension, string? Revision);

    /// <summary>The record of <paramref name="identity"/>.</summary>
    public static string Format(EmbeddingIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return JsonSerializer.Serialize(new Record(identity.Provider, identity.Model, identity.Dimension, identity.Revision));
    }

    /// <summary>The identity a record holds, or null when the text is empty or not a record this library wrote.</summary>
    public static EmbeddingIdentity? Parse(string? record)
    {
        if (string.IsNullOrWhiteSpace(record))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Record>(record) is { Provider: { Length: > 0 } provider, Model: { Length: > 0 } model } r
                ? new EmbeddingIdentity { Provider = provider, Model = model, Dimension = r.Dimension, Revision = r.Revision }
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
