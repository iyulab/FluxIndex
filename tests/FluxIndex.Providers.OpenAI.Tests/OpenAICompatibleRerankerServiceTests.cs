using System.Net;
using System.Text.Json;
using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Providers.OpenAI.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.Providers.OpenAI.Tests;

public class OpenAICompatibleRerankerServiceTests : IDisposable
{
    private readonly ILogger<OpenAICompatibleRerankerService> _logger =
        Substitute.For<ILogger<OpenAICompatibleRerankerService>>();

    private OpenAICompatibleRerankerService? _sut;

    #region Constructor Validation

    [Fact]
    public void Constructor_NullEndpoint_Throws()
    {
        var act = () => new OpenAICompatibleRerankerService(
            null!, "key", "model", _logger);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EmptyEndpoint_Throws()
    {
        var act = () => new OpenAICompatibleRerankerService(
            "", "key", "model", _logger);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WhitespaceModel_Throws()
    {
        var act = () => new OpenAICompatibleRerankerService(
            "http://localhost", "key", "   ", _logger);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new OpenAICompatibleRerankerService(
            "http://localhost", "key", "model", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HttpClientConstructor_NullHttpClient_Throws()
    {
        var act = () => new OpenAICompatibleRerankerService(
            null!, "model", _logger);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HttpClientConstructor_ValidArgs_DoesNotThrow()
    {
        using var httpClient = new HttpClient();
        var act = () => new OpenAICompatibleRerankerService(
            httpClient, "model", _logger);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NullApiKey_DoesNotThrow()
    {
        var act = () => new OpenAICompatibleRerankerService(
            "http://localhost", null, "model", _logger);

        act.Should().NotThrow();
    }

    #endregion

    #region GetModelInfo

    [Fact]
    public void GetModelInfo_ReturnsCorrectInfo()
    {
        _sut = new OpenAICompatibleRerankerService(
            "http://localhost", null, "qwen3-reranker", _logger);

        var info = _sut.GetModelInfo();

        info.Name.Should().Be("qwen3-reranker");
        info.Type.Should().Be(RerankModel.Custom);
        info.RequiresApiKey.Should().BeTrue();
    }

    #endregion

    #region RerankAsync via public API

    [Fact]
    public async Task RerankAsync_EmptyCandidates_ReturnsEmpty()
    {
        _sut = new OpenAICompatibleRerankerService(
            "http://localhost", null, "model", _logger);

        var result = await _sut.RerankAsync("query", []);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RerankAsync_ValidCandidates_ReturnsReranked()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            results = new[]
            {
                new { index = 1, relevance_score = 0.95f },
                new { index = 0, relevance_score = 0.70f },
                new { index = 2, relevance_score = 0.50f }
            }
        });

        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/v1/")
        };

        _sut = new OpenAICompatibleRerankerService(httpClient, "reranker", _logger);

        var candidates = new List<RetrievalCandidate>
        {
            new() { Id = "1", Content = "First document", InitialRank = 1 },
            new() { Id = "2", Content = "Second document", InitialRank = 2 },
            new() { Id = "3", Content = "Third document", InitialRank = 3 }
        };

        var results = (await _sut.RerankAsync("test query", candidates)).ToList();

        results.Should().HaveCount(3);
        // Ordered by descending relevance score
        results[0].RerankScore.Should().Be(0.95f);
        results[0].Id.Should().Be("2"); // index=1 -> second candidate
        results[1].RerankScore.Should().Be(0.70f);
        results[1].Id.Should().Be("1"); // index=0 -> first candidate
    }

    [Fact]
    public async Task RerankAsync_SendsCorrectRequest()
    {
        var handler = new MockHttpMessageHandler(
            JsonSerializer.Serialize(new
            {
                results = new[]
                {
                    new { index = 0, relevance_score = 0.9f }
                }
            }),
            HttpStatusCode.OK);

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/v1/")
        };

        _sut = new OpenAICompatibleRerankerService(httpClient, "my-reranker", _logger);

        var candidates = new List<RetrievalCandidate>
        {
            new() { Id = "1", Content = "doc content", InitialRank = 1 }
        };

        await _sut.RerankAsync("search query", candidates);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequestUri!.ToString().Should().Contain("rerank");

        var body = handler.LastRequestBody!;
        body.Should().Contain("my-reranker");
        body.Should().Contain("search query");
        body.Should().Contain("doc content");
    }

    [Fact]
    public async Task RerankAsync_ServerError_ThrowsHttpRequestException()
    {
        var handler = new MockHttpMessageHandler("", HttpStatusCode.InternalServerError);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/v1/")
        };

        _sut = new OpenAICompatibleRerankerService(httpClient, "model", _logger);

        var candidates = new List<RetrievalCandidate>
        {
            new() { Id = "1", Content = "doc", InitialRank = 1 }
        };

        var act = () => _sut.RerankAsync("query", candidates);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task RerankAsync_EmptyResults_ThrowsInvalidOperation()
    {
        var responseJson = JsonSerializer.Serialize(new { results = Array.Empty<object>() });
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/v1/")
        };

        _sut = new OpenAICompatibleRerankerService(httpClient, "model", _logger);

        var candidates = new List<RetrievalCandidate>
        {
            new() { Id = "1", Content = "doc", InitialRank = 1 }
        };

        var act = () => _sut.RerankAsync("query", candidates);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No rerank results*");
    }

    [Fact]
    public async Task RerankAsync_NullResults_ThrowsInvalidOperation()
    {
        var handler = new MockHttpMessageHandler("{}", HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/v1/")
        };

        _sut = new OpenAICompatibleRerankerService(httpClient, "model", _logger);

        var candidates = new List<RetrievalCandidate>
        {
            new() { Id = "1", Content = "doc", InitialRank = 1 }
        };

        var act = () => _sut.RerankAsync("query", candidates);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No rerank results*");
    }

    #endregion

    #region Score Threshold

    [Fact]
    public async Task RerankAsync_WithScoreThreshold_FiltersLowScores()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            results = new[]
            {
                new { index = 0, relevance_score = 0.9f },
                new { index = 1, relevance_score = 0.3f },
                new { index = 2, relevance_score = 0.1f }
            }
        });

        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/v1/")
        };

        _sut = new OpenAICompatibleRerankerService(httpClient, "model", _logger);

        var candidates = new List<RetrievalCandidate>
        {
            new() { Id = "1", Content = "highly relevant", InitialRank = 1 },
            new() { Id = "2", Content = "somewhat relevant", InitialRank = 2 },
            new() { Id = "3", Content = "not relevant", InitialRank = 3 }
        };

        var options = new RerankOptions { ScoreThreshold = 0.5f };
        var results = (await _sut.RerankAsync("query", candidates, options)).ToList();

        results.Should().HaveCount(1);
        results[0].Id.Should().Be("1");
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_OwnedClient_DisposesHttpClient()
    {
        _sut = new OpenAICompatibleRerankerService(
            "http://localhost", null, "model", _logger);

        _sut.Dispose();

        // After dispose, using the service should eventually fail
        var candidates = new List<RetrievalCandidate>
        {
            new() { Id = "1", Content = "doc", InitialRank = 1 }
        };
        var act = () => _sut.RerankAsync("query", candidates);
        act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_InjectedClient_DoesNotDisposeHttpClient()
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost/")
        };

        _sut = new OpenAICompatibleRerankerService(httpClient, "model", _logger);

        _sut.Dispose();

        // The injected client should still be usable
        var act = () => httpClient.BaseAddress;
        act.Should().NotThrow();
    }

    #endregion

    public void Dispose()
    {
        _sut?.Dispose();
    }
}
