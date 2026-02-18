using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Stack.Infrastructure.Services;
using LMSupply.Reranker;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.Stack.Api.Tests;

/// <summary>
/// Unit tests for LMSupplyRerankerWrapper.
/// </summary>
public class LMSupplyRerankerWrapperTests
{
    [Fact]
    public async Task RerankAsync_DelegatesToRerankerModel()
    {
        // Arrange
        var model = Substitute.For<IRerankerModel>();
        model.ModelId.Returns("test-model");
        model.RerankAsync(
                Arg.Any<string>(),
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<RankedResult>
            {
                new(OriginalIndex: 1, Score: 0.95f, Document: "doc B"),
                new(OriginalIndex: 0, Score: 0.80f, Document: "doc A"),
            });

        var logger = Substitute.For<ILogger>();
        var wrapper = new LMSupplyRerankerWrapper(model, logger);

        var candidates = new List<RetrievalCandidate>
        {
            new() { Id = "1", Content = "doc A", InitialScore = 0.5f, InitialRank = 1 },
            new() { Id = "2", Content = "doc B", InitialScore = 0.4f, InitialRank = 2 },
        };

        // Act
        var results = (await wrapper.RerankAsync("test query", candidates)).ToList();

        // Assert
        results.Should().HaveCount(2);
        results[0].Id.Should().Be("2"); // doc B ranked first (score 0.95)
        results[0].RerankScore.Should().BeApproximately(0.95f, 0.001f);
        results[0].NewRank.Should().Be(1);
        results[1].Id.Should().Be("1"); // doc A ranked second (score 0.80)
        results[1].RerankScore.Should().BeApproximately(0.80f, 0.001f);
        results[1].NewRank.Should().Be(2);
    }

    [Fact]
    public async Task RerankAsync_WithEmptyCandidates_ReturnsEmpty()
    {
        // Arrange
        var model = Substitute.For<IRerankerModel>();
        var logger = Substitute.For<ILogger>();
        var wrapper = new LMSupplyRerankerWrapper(model, logger);

        // Act
        var results = await wrapper.RerankAsync("query", Enumerable.Empty<RetrievalCandidate>());

        // Assert
        results.Should().BeEmpty();
        await model.DidNotReceive().RerankAsync(
            Arg.Any<string>(),
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RerankAsync_WithScoreThreshold_FiltersLowScores()
    {
        // Arrange
        var model = Substitute.For<IRerankerModel>();
        model.RerankAsync(
                Arg.Any<string>(),
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<RankedResult>
            {
                new(OriginalIndex: 0, Score: 0.9f, Document: "relevant"),
                new(OriginalIndex: 1, Score: 0.1f, Document: "irrelevant"),
            });

        var logger = Substitute.For<ILogger>();
        var wrapper = new LMSupplyRerankerWrapper(model, logger);

        var candidates = new List<RetrievalCandidate>
        {
            new() { Id = "1", Content = "relevant", InitialScore = 0.5f, InitialRank = 1 },
            new() { Id = "2", Content = "irrelevant", InitialScore = 0.4f, InitialRank = 2 },
        };

        var options = new RerankOptions { ScoreThreshold = 0.5f };

        // Act
        var results = (await wrapper.RerankAsync("query", candidates, options)).ToList();

        // Assert
        results.Should().HaveCount(1);
        results[0].Id.Should().Be("1");
    }

    [Fact]
    public void GetModelInfo_ReturnsLocalModelInfo()
    {
        // Arrange
        var model = Substitute.For<IRerankerModel>();
        model.ModelId.Returns("ms-marco-MiniLM-L6-v2");

        var logger = Substitute.For<ILogger>();
        var wrapper = new LMSupplyRerankerWrapper(model, logger);

        // Act
        var info = wrapper.GetModelInfo();

        // Assert
        info.Name.Should().Be("ms-marco-MiniLM-L6-v2");
        info.Type.Should().Be(RerankModel.Local);
        info.RequiresApiKey.Should().BeFalse();
    }

    [Fact]
    public void Constructor_ThrowsOnNullModel()
    {
        // Arrange
        var logger = Substitute.For<ILogger>();

        // Act & Assert
        var act = () => new LMSupplyRerankerWrapper(null!, logger);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task DisposeAsync_DisposesModel()
    {
        // Arrange
        var model = Substitute.For<IRerankerModel>();
        var logger = Substitute.For<ILogger>();
        var wrapper = new LMSupplyRerankerWrapper(model, logger);

        // Act
        await wrapper.DisposeAsync();

        // Assert
        await model.Received(1).DisposeAsync();
    }
}
