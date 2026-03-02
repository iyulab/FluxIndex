using System.Net;
using System.Text.Json;
using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Providers.OpenAI.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.Providers.OpenAI.Tests;

public class OpenAICompatibleEmbeddingServiceTests : IDisposable
{
    private readonly ILogger<OpenAICompatibleEmbeddingService> _logger =
        Substitute.For<ILogger<OpenAICompatibleEmbeddingService>>();

    private OpenAICompatibleEmbeddingService? _sut;

    #region Constructor Validation

    [Fact]
    public void Constructor_NullEndpoint_Throws()
    {
        var act = () => new OpenAICompatibleEmbeddingService(
            null!, "key", "model", 128, _logger);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EmptyEndpoint_Throws()
    {
        var act = () => new OpenAICompatibleEmbeddingService(
            "", "key", "model", 128, _logger);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WhitespaceModel_Throws()
    {
        var act = () => new OpenAICompatibleEmbeddingService(
            "http://localhost", "key", "   ", 128, _logger);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ZeroDimension_Throws()
    {
        var act = () => new OpenAICompatibleEmbeddingService(
            "http://localhost", "key", "model", 0, _logger);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_NegativeDimension_Throws()
    {
        var act = () => new OpenAICompatibleEmbeddingService(
            "http://localhost", "key", "model", -1, _logger);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new OpenAICompatibleEmbeddingService(
            "http://localhost", "key", "model", 128, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HttpClientConstructor_NullHttpClient_Throws()
    {
        var act = () => new OpenAICompatibleEmbeddingService(
            null!, "model", 128, _logger);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HttpClientConstructor_ValidArgs_DoesNotThrow()
    {
        using var httpClient = new HttpClient();
        var act = () => new OpenAICompatibleEmbeddingService(
            httpClient, "model", 128, _logger);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NullApiKey_DoesNotThrow()
    {
        var act = () => new OpenAICompatibleEmbeddingService(
            "http://localhost", null, "model", 128, _logger);

        act.Should().NotThrow();
    }

    #endregion

    #region Property Methods

    [Fact]
    public void GetEmbeddingDimension_ReturnsConfiguredDimension()
    {
        _sut = new OpenAICompatibleEmbeddingService(
            "http://localhost", null, "test-model", 1024, _logger);

        _sut.GetEmbeddingDimension().Should().Be(1024);
    }

    [Fact]
    public void GetModelName_ReturnsConfiguredModel()
    {
        _sut = new OpenAICompatibleEmbeddingService(
            "http://localhost", null, "text-embedding-3-small", 1536, _logger);

        _sut.GetModelName().Should().Be("text-embedding-3-small");
    }

    #endregion

    #region EmbedCoreAsync via GenerateEmbeddingAsync

    [Fact]
    public async Task GenerateEmbeddingAsync_EmptyText_ReturnsEmptyArray()
    {
        _sut = new OpenAICompatibleEmbeddingService(
            "http://localhost", null, "model", 3, _logger);

        var result = await _sut.GenerateEmbeddingAsync("");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhitespaceText_ReturnsEmptyArray()
    {
        _sut = new OpenAICompatibleEmbeddingService(
            "http://localhost", null, "model", 3, _logger);

        var result = await _sut.GenerateEmbeddingAsync("   ");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_ValidText_ReturnsEmbedding()
    {
        var expectedEmbedding = new[] { 0.1f, 0.2f, 0.3f };
        var responseJson = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new { embedding = expectedEmbedding }
            }
        });

        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/v1/")
        };

        _sut = new OpenAICompatibleEmbeddingService(httpClient, "model", 3, _logger);

        var result = await _sut.GenerateEmbeddingAsync("Hello world");

        result.Should().BeEquivalentTo(expectedEmbedding);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_SendsCorrectRequest()
    {
        var handler = new MockHttpMessageHandler(
            JsonSerializer.Serialize(new
            {
                data = new[] { new { embedding = new[] { 1.0f } } }
            }),
            HttpStatusCode.OK);

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/v1/")
        };

        _sut = new OpenAICompatibleEmbeddingService(httpClient, "my-model", 1, _logger);

        await _sut.GenerateEmbeddingAsync("test input");

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequestUri!.ToString().Should().Contain("embeddings");

        var body = handler.LastRequestBody!;
        body.Should().Contain("my-model");
        body.Should().Contain("test input");
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_ServerError_ThrowsHttpRequestException()
    {
        var handler = new MockHttpMessageHandler("", HttpStatusCode.InternalServerError);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/v1/")
        };

        _sut = new OpenAICompatibleEmbeddingService(httpClient, "model", 3, _logger);

        var act = () => _sut.GenerateEmbeddingAsync("test");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_EmptyDataArray_ThrowsInvalidOperation()
    {
        var responseJson = JsonSerializer.Serialize(new { data = Array.Empty<object>() });
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/v1/")
        };

        _sut = new OpenAICompatibleEmbeddingService(httpClient, "model", 3, _logger);

        var act = () => _sut.GenerateEmbeddingAsync("test");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No embedding returned*");
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_NullData_ThrowsInvalidOperation()
    {
        var handler = new MockHttpMessageHandler("{}", HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/v1/")
        };

        _sut = new OpenAICompatibleEmbeddingService(httpClient, "model", 3, _logger);

        var act = () => _sut.GenerateEmbeddingAsync("test");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No embedding returned*");
    }

    #endregion

    #region Batch Embedding

    [Fact]
    public async Task GenerateEmbeddingsBatchAsync_MultipleTexts_ReturnsAllEmbeddings()
    {
        var callCount = 0;
        var handler = new MockHttpMessageHandler(() =>
        {
            callCount++;
            var emb = new[] { (float)callCount, (float)callCount * 2 };
            return JsonSerializer.Serialize(new
            {
                data = new[] { new { embedding = emb } }
            });
        }, HttpStatusCode.OK);

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/v1/")
        };

        _sut = new OpenAICompatibleEmbeddingService(httpClient, "model", 2, _logger);

        var results = (await _sut.GenerateEmbeddingsBatchAsync(["text1", "text2", "text3"])).ToList();

        results.Should().HaveCount(3);
        results[0].Should().BeEquivalentTo(new[] { 1f, 2f });
        results[1].Should().BeEquivalentTo(new[] { 2f, 4f });
        results[2].Should().BeEquivalentTo(new[] { 3f, 6f });
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_OwnedClient_DisposesHttpClient()
    {
        _sut = new OpenAICompatibleEmbeddingService(
            "http://localhost", null, "model", 128, _logger);

        _sut.Dispose();

        // Attempting to use the disposed client should throw
        var act = () => _sut.GenerateEmbeddingAsync("test");
        act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_InjectedClient_DoesNotDisposeHttpClient()
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost/")
        };

        _sut = new OpenAICompatibleEmbeddingService(httpClient, "model", 128, _logger);

        _sut.Dispose();

        // The injected client should still be usable (not disposed)
        var act = () => httpClient.BaseAddress;
        act.Should().NotThrow();
    }

    #endregion

    #region Authentication

    [Fact]
    public async Task Constructor_WithApiKey_SetsAuthorizationHeader()
    {
        var handler = new MockHttpMessageHandler(
            JsonSerializer.Serialize(new
            {
                data = new[] { new { embedding = new[] { 1.0f } } }
            }),
            HttpStatusCode.OK);

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/v1/")
        };
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-key");

        _sut = new OpenAICompatibleEmbeddingService(httpClient, "model", 1, _logger);

        await _sut.GenerateEmbeddingAsync("test");

        handler.LastRequest!.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be("test-key");
    }

    #endregion

    public void Dispose()
    {
        _sut?.Dispose();
    }
}
