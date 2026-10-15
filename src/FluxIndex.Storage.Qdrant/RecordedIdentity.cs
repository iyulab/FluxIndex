using System.Text.Json;
using FluxIndex.Core.Domain.ValueObjects;
using Qdrant.Client.Grpc;

namespace FluxIndex.Storage.Qdrant;

/// <summary>
/// The embedding identity as it is recorded in a collection's metadata: the members that decide whether two identities
/// are the same vector space (provider, model, dimension, revision), as a JSON string value.
/// </summary>
internal static class RecordedIdentity
{
    private sealed record Record(string Provider, string Model, int Dimension, string? Revision);

    internal static Value Format(EmbeddingIdentity identity)
        => new() { StringValue = JsonSerializer.Serialize(new Record(identity.Provider, identity.Model, identity.Dimension, identity.Revision)) };

    /// <summary>The recorded identity, or null when the value is empty or not one this library wrote.</summary>
    internal static EmbeddingIdentity? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Record>(json) is { Provider: { Length: > 0 } provider, Model: { Length: > 0 } model } record
                ? new EmbeddingIdentity { Provider = provider, Model = model, Dimension = record.Dimension, Revision = record.Revision }
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
