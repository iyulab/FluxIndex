using FluentAssertions;
using FluxIndex.Providers.OpenAI.Services;
using Xunit;

namespace FluxIndex.Providers.OpenAI.Tests;

public class WellKnownOpenAIModelsTests
{
    [Theory]
    [InlineData("text-embedding-3-small", 1536)]
    [InlineData("text-embedding-3-large", 3072)]
    [InlineData("text-embedding-ada-002", 1536)]
    [InlineData("qwen3-embedding-0.6b", 1024)]
    [InlineData("bge-small-en-v1.5", 384)]
    [InlineData("bge-base-en-v1.5", 768)]
    [InlineData("bge-large-en-v1.5", 1024)]
    [InlineData("nomic-embed-text", 768)]
    [InlineData("all-minilm-l6-v2", 384)]
    [InlineData("mxbai-embed-large", 1024)]
    public void TryGetEmbeddingDimension_KnownModel_ReturnsCorrectDimension(
        string modelName, int expectedDimension)
    {
        var found = WellKnownOpenAIModels.TryGetEmbeddingDimension(modelName, out int dimension);

        found.Should().BeTrue();
        dimension.Should().Be(expectedDimension);
    }

    [Theory]
    [InlineData("TEXT-EMBEDDING-3-SMALL")]
    [InlineData("Text-Embedding-3-Small")]
    [InlineData("TEXT-EMBEDDING-ADA-002")]
    public void TryGetEmbeddingDimension_CaseInsensitive_ReturnsTrue(string modelName)
    {
        var found = WellKnownOpenAIModels.TryGetEmbeddingDimension(modelName, out _);

        found.Should().BeTrue();
    }

    [Theory]
    [InlineData("unknown-custom-model-v1")]
    [InlineData("qwen3-embedding-9999b")]
    [InlineData("")]
    [InlineData("my-company-embedding-model")]
    public void TryGetEmbeddingDimension_UnknownModel_ReturnsFalse(string modelName)
    {
        var found = WellKnownOpenAIModels.TryGetEmbeddingDimension(modelName, out int dimension);

        found.Should().BeFalse();
        dimension.Should().Be(0);
    }
}
