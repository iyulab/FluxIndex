using FluxIndex.Core.Domain.ValueObjects;
using Qdrant.Client.Grpc;

namespace FluxIndex.Storage.Qdrant;

/// <summary>The embedding identity as a Qdrant collection-metadata value (<see cref="EmbeddingIdentityRecord"/>).</summary>
internal static class RecordedIdentity
{
    internal static Value Format(EmbeddingIdentity identity) => new() { StringValue = EmbeddingIdentityRecord.Format(identity) };

    internal static EmbeddingIdentity? Parse(string? value) => EmbeddingIdentityRecord.Parse(value);
}
