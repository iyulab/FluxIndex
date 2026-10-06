using FluxIndex.SDK.Configuration;
using FluxIndex.Storage.Qdrant;
using Xunit;

namespace FluxIndex.Storage.Qdrant.Tests;

/// <summary>
/// The builder's Qdrant settings reach the store options. A self-hosted Qdrant behind TLS used to get plaintext gRPC:
/// <see cref="VectorStoreOptions.QdrantUseHttps"/> was honoured only on the Qdrant Cloud path (which an API key selected).
/// </summary>
public class BuilderOptionsMappingTests
{
    [Fact]
    public void SelfHosted_https_and_api_key_reach_the_store()
    {
        var store = new VectorStoreOptions { QdrantHost = "qdrant.internal", QdrantGrpcPort = 7334, QdrantUseHttps = true, QdrantApiKey = "k" };
        var qdrant = new QdrantOptions();

        FluxIndexContextBuilderExtensions.ApplyBuilderOptions(qdrant, store);

        Assert.True(qdrant.UseHttps);
        Assert.Equal("k", qdrant.ApiKey);
        Assert.Equal("qdrant.internal", qdrant.Host);
        Assert.Equal(7334, qdrant.GrpcPort);
        Assert.Equal(CollectionNamingStrategy.ModelFingerprint, qdrant.NamingStrategy);
    }

    [Fact]
    public void Fixed_naming_carries_the_vector_size_and_an_empty_key_stays_unset()
    {
        var store = new VectorStoreOptions { QdrantNamingStrategy = "Fixed", QdrantVectorSize = 1024, QdrantApiKey = "" };
        var qdrant = new QdrantOptions();

        FluxIndexContextBuilderExtensions.ApplyBuilderOptions(qdrant, store);

        Assert.Equal(CollectionNamingStrategy.Fixed, qdrant.NamingStrategy);
        Assert.Equal(1024, qdrant.VectorSize);
        Assert.Null(qdrant.ApiKey);
        Assert.False(qdrant.UseHttps);
    }

    // A key selected the Cloud registration (https forced) before 0.79.0; it must not start going out in plaintext.
    [Fact]
    public void An_api_key_keeps_https_even_when_QdrantUseHttps_is_left_false()
    {
        var store = new VectorStoreOptions { QdrantApiKey = "k", QdrantUseHttps = false };
        var qdrant = new QdrantOptions();

        FluxIndexContextBuilderExtensions.ApplyBuilderOptions(qdrant, store);

        Assert.True(qdrant.UseHttps);
    }
}
