using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Reranking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Reranking;

public class ListwiseRerankerTests
{
    private readonly ITextCompletionService _mockLlmService;
    private readonly IEmbeddingService _mockEmbeddingService;
    private readonly ILogger<ListwiseReranker> _mockLogger;

    public ListwiseRerankerTests()
    {
        _mockLlmService = Substitute.For<ITextCompletionService>();
        _mockEmbeddingService = Substitute.For<IEmbeddingService>();
        _mockLogger = Substitute.For<ILogger<ListwiseReranker>>();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNoParameters_Succeeds()
    {
        var reranker = new ListwiseReranker();
        Assert.NotNull(reranker);
    }

    [Fact]
    public void Constructor_WithAllParameters_Succeeds()
    {
        var reranker = new ListwiseReranker(
            _mockLlmService,
            _mockEmbeddingService,
            _mockLogger);
        Assert.NotNull(reranker);
    }

    [Fact]
    public void Constructor_WithOnlyLlmService_Succeeds()
    {
        var reranker = new ListwiseReranker(llmService: _mockLlmService);
        Assert.NotNull(reranker);
    }

    [Fact]
    public void Constructor_WithOnlyEmbeddingService_Succeeds()
    {
        var reranker = new ListwiseReranker(embeddingService: _mockEmbeddingService);
        Assert.NotNull(reranker);
    }

    #endregion

    #region RerankAsync Basic Tests

    [Fact]
    public async Task RerankAsync_WithEmptyCandidates_ReturnsEmptyList()
    {
        // Arrange
        var reranker = new ListwiseReranker();
        var candidates = Array.Empty<RetrievalCandidate>();

        // Act
        var result = await reranker.RerankAsync("test query", candidates, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task RerankAsync_WithSingleCandidate_ReturnsSingleResult()
    {
        // Arrange
        var reranker = new ListwiseReranker();
        var candidates = new[] { CreateCandidate("1", "Test content", 0.8f) };

        // Act
        var result = await reranker.RerankAsync("test query", candidates, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
        Assert.Equal("1", result[0].Id);
    }

    [Fact]
    public async Task RerankAsync_PreservesDocumentMetadata()
    {
        // Arrange
        var reranker = new ListwiseReranker();
        var metadata = new Dictionary<string, object> { ["key"] = "value" };
        var candidates = new[]
        {
            CreateCandidate("1", "Content", 0.8f, metadata: metadata)
        };

        // Act
        var result = await reranker.RerankAsync("query", candidates, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
        Assert.NotNull(result[0].Metadata);
        Assert.Equal("value", result[0].Metadata!["key"]);
    }

    [Fact]
    public async Task RerankAsync_AssignsNewRanks()
    {
        // Arrange
        var reranker = new ListwiseReranker();
        var candidates = new[]
        {
            CreateCandidate("1", "Content A", 0.8f),
            CreateCandidate("2", "Content B", 0.6f),
            CreateCandidate("3", "Content C", 0.4f)
        };

        // Act
        var result = await reranker.RerankAsync("query", candidates, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].NewRank);
        Assert.Equal(2, result[1].NewRank);
        Assert.Equal(3, result[2].NewRank);
    }

    #endregion

    #region Method-Specific Tests

    [Fact]
    public async Task RerankAsync_AttentionBased_WithoutEmbedding_UsesLexicalFallback()
    {
        // Arrange
        var reranker = new ListwiseReranker(); // No services
        var candidates = new[]
        {
            CreateCandidate("1", "machine learning algorithms", 0.5f),
            CreateCandidate("2", "cooking recipes", 0.5f)
        };
        var options = new ListwiseRerankOptions { Method = ListwiseMethod.AttentionBased };

        // Act
        var result = await reranker.RerankAsync("machine learning", candidates, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);
        // Document about machine learning should rank higher
        var topResult = result.OrderByDescending(r => r.ListwiseScore).First();
        Assert.Equal("1", topResult.Id);
    }

    [Fact]
    public async Task RerankAsync_AttentionBased_WithEmbedding_UsesCosineSimilarity()
    {
        // Arrange
        SetupMockEmbeddingService();
        var reranker = new ListwiseReranker(embeddingService: _mockEmbeddingService);
        var candidates = new[]
        {
            CreateCandidate("1", "Content A", 0.5f),
            CreateCandidate("2", "Content B", 0.5f)
        };
        var options = new ListwiseRerankOptions { Method = ListwiseMethod.AttentionBased };

        // Act
        var result = await reranker.RerankAsync("query", candidates, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.True(r.AttentionScore >= 0 && r.AttentionScore <= 1));
    }

    [Fact]
    public async Task RerankAsync_SlidingWindow_WithoutLlm_FallsBackToAttention()
    {
        // Arrange
        var reranker = new ListwiseReranker(); // No LLM
        var candidates = CreateMultipleCandidates(5);
        var options = new ListwiseRerankOptions { Method = ListwiseMethod.SlidingWindow };

        // Act
        var result = await reranker.RerankAsync("query", candidates, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public async Task RerankAsync_DirectLlm_WithoutLlm_FallsBackToAttention()
    {
        // Arrange
        var reranker = new ListwiseReranker(); // No LLM
        var candidates = CreateMultipleCandidates(3);
        var options = new ListwiseRerankOptions { Method = ListwiseMethod.DirectLlm };

        // Act
        var result = await reranker.RerankAsync("query", candidates, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task RerankAsync_Tournament_ProcessesPairwiseComparisons()
    {
        // Arrange
        SetupMockLlmService();
        var reranker = new ListwiseReranker(llmService: _mockLlmService);
        var candidates = CreateMultipleCandidates(4);
        var options = new ListwiseRerankOptions
        {
            Method = ListwiseMethod.Tournament,
            MaxPairwiseComparisons = 6
        };

        // Act
        var result = await reranker.RerankAsync("query", candidates, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4, result.Count);
        Assert.All(result, r =>
        {
            Assert.True(r.ComparisonCount >= 0);
            Assert.True(r.WinRate >= 0 && r.WinRate <= 1);
        });
    }

    [Fact]
    public async Task RerankAsync_Hybrid_CombinesMultipleMethods()
    {
        // Arrange
        SetupMockEmbeddingService();
        var reranker = new ListwiseReranker(embeddingService: _mockEmbeddingService);
        var candidates = CreateMultipleCandidates(5);
        var options = new ListwiseRerankOptions { Method = ListwiseMethod.Hybrid };

        // Act
        var result = await reranker.RerankAsync("query", candidates, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, result.Count);
        Assert.All(result, r => Assert.NotNull(r.Components));
    }

    #endregion

    #region Options Tests

    [Fact]
    public async Task RerankAsync_RespectsScoreThreshold()
    {
        // Arrange
        var reranker = new ListwiseReranker();
        var candidates = new[]
        {
            CreateCandidate("1", "Very relevant content about the query topic", 0.9f),
            CreateCandidate("2", "xyz", 0.1f)
        };
        var options = new ListwiseRerankOptions
        {
            Method = ListwiseMethod.AttentionBased,
            ScoreThreshold = 0.3f
        };

        // Act
        var result = await reranker.RerankAsync("query topic", candidates, options, TestContext.Current.CancellationToken);

        // Assert
        // Low scoring items may be filtered out
        Assert.True(result.All(r => r.ListwiseScore >= 0.3f));
    }

    [Fact]
    public async Task RerankAsync_RespectsTopN()
    {
        // Arrange
        var reranker = new ListwiseReranker();
        var candidates = CreateMultipleCandidates(10);
        var options = new ListwiseRerankOptions
        {
            TopN = 5,
            ScoreThreshold = 0 // Accept all scores
        };

        // Act
        var result = await reranker.RerankAsync("query", candidates, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Count <= 5);
    }

    [Fact]
    public async Task RerankAsync_InitialScoreWeight_AffectsBlending()
    {
        // Arrange
        var reranker = new ListwiseReranker();
        var candidates = new[]
        {
            CreateCandidate("1", "xyz", 0.9f), // High initial, low relevance
            CreateCandidate("2", "query relevant content", 0.1f) // Low initial, high relevance
        };

        // Test with high initial score weight
        var optionsHighInitial = new ListwiseRerankOptions
        {
            Method = ListwiseMethod.AttentionBased,
            InitialScoreWeight = 0.9f
        };
        var resultHigh = await reranker.RerankAsync("query", candidates, optionsHighInitial, TestContext.Current.CancellationToken);

        // Test with low initial score weight
        var optionsLowInitial = new ListwiseRerankOptions
        {
            Method = ListwiseMethod.AttentionBased,
            InitialScoreWeight = 0.1f
        };
        var resultLow = await reranker.RerankAsync("query", candidates, optionsLowInitial, TestContext.Current.CancellationToken);

        // Assert
        // With high initial weight, candidate 1 (high initial score) should rank higher
        // With low initial weight, candidate 2 (more relevant) should rank higher
        var topWithHighWeight = resultHigh.OrderByDescending(r => r.ListwiseScore).First();
        var topWithLowWeight = resultLow.OrderByDescending(r => r.ListwiseScore).First();
        Assert.Equal("1", topWithHighWeight.Id);
        Assert.Equal("2", topWithLowWeight.Id);
    }

    [Fact]
    public async Task RerankAsync_WindowSize_AffectsSlidingWindowProcessing()
    {
        // Arrange
        SetupMockLlmService();
        var reranker = new ListwiseReranker(llmService: _mockLlmService);
        var candidates = CreateMultipleCandidates(10);
        var options = new ListwiseRerankOptions
        {
            Method = ListwiseMethod.SlidingWindow,
            WindowSize = 3,
            WindowStep = 2
        };

        // Act
        var result = await reranker.RerankAsync("query", candidates, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(10, result.Count);
    }

    #endregion

    #region ComputePairwisePreferenceAsync Tests

    [Fact]
    public async Task ComputePairwisePreferenceAsync_WithLlm_UsesLlmComparison()
    {
        // Arrange
        _mockLlmService.CompleteAsync(
                Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("A");

        var reranker = new ListwiseReranker(llmService: _mockLlmService);

        // Act
        var result = await reranker.ComputePairwisePreferenceAsync("query", "Document A content", "Document B content", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, result.Preference); // A preferred
        Assert.True(result.Confidence > 0);
    }

    [Fact]
    public async Task ComputePairwisePreferenceAsync_WithEmbedding_UsesCosineSimilarity()
    {
        // Arrange
        SetupMockEmbeddingService();
        var reranker = new ListwiseReranker(embeddingService: _mockEmbeddingService);

        // Act
        var result = await reranker.ComputePairwisePreferenceAsync("query", "Doc A", "Doc B", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Preference >= -1 && result.Preference <= 1);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task ComputePairwisePreferenceAsync_WithNoServices_UsesLexicalComparison()
    {
        // Arrange
        var reranker = new ListwiseReranker();

        // Act
        var result = await reranker.ComputePairwisePreferenceAsync("machine learning", "This is about machine learning algorithms", "This is about cooking recipes", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, result.Preference); // First doc should be preferred
        Assert.Contains("Lexical", result.Reason);
    }

    #endregion

    #region GetModelInfo Tests

    [Fact]
    public void GetModelInfo_WithNoServices_ReturnsLimitedCapabilities()
    {
        // Arrange
        var reranker = new ListwiseReranker();

        // Act
        var info = reranker.GetModelInfo();

        // Assert
        Assert.NotNull(info);
        Assert.Equal("FluxIndex Listwise Reranker", info.Name);
        Assert.False(info.LlmAvailable);
        Assert.False(info.EmbeddingsAvailable);
        Assert.Contains(ListwiseMethod.AttentionBased, info.SupportedMethods);
        Assert.DoesNotContain(ListwiseMethod.SlidingWindow, info.SupportedMethods);
    }

    [Fact]
    public void GetModelInfo_WithLlm_IncludesLlmMethods()
    {
        // Arrange
        var reranker = new ListwiseReranker(llmService: _mockLlmService);

        // Act
        var info = reranker.GetModelInfo();

        // Assert
        Assert.True(info.LlmAvailable);
        Assert.Contains(ListwiseMethod.SlidingWindow, info.SupportedMethods);
        Assert.Contains(ListwiseMethod.Tournament, info.SupportedMethods);
        Assert.Contains(ListwiseMethod.DirectLlm, info.SupportedMethods);
    }

    [Fact]
    public void GetModelInfo_WithEmbedding_IncludesHybridMethod()
    {
        // Arrange
        var reranker = new ListwiseReranker(embeddingService: _mockEmbeddingService);

        // Act
        var info = reranker.GetModelInfo();

        // Assert
        Assert.True(info.EmbeddingsAvailable);
        Assert.Contains(ListwiseMethod.Hybrid, info.SupportedMethods);
    }

    [Fact]
    public void GetModelInfo_ReturnsVersionInfo()
    {
        // Arrange
        var reranker = new ListwiseReranker();

        // Act
        var info = reranker.GetModelInfo();

        // Assert
        Assert.NotEmpty(info.Version);
        Assert.True(info.MaxCandidatesPerPass > 0);
        Assert.True(info.EstimatedLatencyPerCandidateMs > 0);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task RerankAsync_RespectsCanellation()
    {
        // Arrange
        var reranker = new ListwiseReranker();
        var candidates = CreateMultipleCandidates(10);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => reranker.RerankAsync("query", candidates, cancellationToken: cts.Token));
    }

    #endregion

    #region Score Components Tests

    [Fact]
    public async Task RerankAsync_SetsScoreComponents()
    {
        // Arrange
        var reranker = new ListwiseReranker();
        var candidates = CreateMultipleCandidates(3);
        var options = new ListwiseRerankOptions { Method = ListwiseMethod.AttentionBased };

        // Act
        var result = await reranker.RerankAsync("query", candidates, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.All(result, r =>
        {
            Assert.NotNull(r.Components);
            Assert.NotNull(r.Components.Weights);
            Assert.True(r.Components.Weights.ContainsKey("attention") ||
                        r.Components.Weights.ContainsKey("initial"));
        });
    }

    [Fact]
    public async Task RerankAsync_PreservesInitialScoreAndRank()
    {
        // Arrange
        var reranker = new ListwiseReranker();
        var candidates = new[]
        {
            CreateCandidate("1", "Content", 0.85f, initialRank: 1),
            CreateCandidate("2", "Content", 0.65f, initialRank: 2)
        };

        // Act
        var result = await reranker.RerankAsync("query", candidates, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var result1 = result.First(r => r.Id == "1");
        var result2 = result.First(r => r.Id == "2");
        Assert.Equal(0.85f, result1.InitialScore);
        Assert.Equal(0.65f, result2.InitialScore);
        Assert.Equal(1, result1.InitialRank);
        Assert.Equal(2, result2.InitialRank);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task RerankAsync_WithDuplicateContent_HandlesGracefully()
    {
        // Arrange
        var reranker = new ListwiseReranker();
        var candidates = new[]
        {
            CreateCandidate("1", "Same content", 0.8f),
            CreateCandidate("2", "Same content", 0.6f)
        };

        // Act
        var result = await reranker.RerankAsync("query", candidates, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.NotEqual(result[0].Id, result[1].Id);
    }

    [Fact]
    public async Task RerankAsync_WithVeryLongContent_TruncatesForProcessing()
    {
        // Arrange
        SetupMockLlmService();
        var reranker = new ListwiseReranker(llmService: _mockLlmService);
        var longContent = new string('x', 10000);
        var candidates = new[] { CreateCandidate("1", longContent, 0.5f) };
        var options = new ListwiseRerankOptions
        {
            Method = ListwiseMethod.DirectLlm,
            MaxContentLength = 100
        };

        // Act
        var result = await reranker.RerankAsync("query", candidates, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task RerankAsync_WithNullMetadata_HandlesGracefully()
    {
        // Arrange
        var reranker = new ListwiseReranker();
        var candidate = new RetrievalCandidate
        {
            Id = "1",
            DocumentId = "doc1",
            Content = "Content",
            InitialScore = 0.5f,
            Metadata = null
        };

        // Act
        var result = await reranker.RerankAsync("query", new[] { candidate }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
    }

    #endregion

    #region Helper Methods

    private void SetupMockLlmService()
    {
        // Return "A" for pairwise comparisons (Tournament mode expects "A", "B", or "TIE")
        // Return "1, 2, 3" for ranking requests (DirectLlm mode expects ranked indices)
        _mockLlmService.CompleteAsync(
                Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns(callInfo => { var prompt = callInfo.ArgAt<string>(0); 
                // Tournament mode asks "Which document is more relevant?"
                if (prompt.Contains("Which document is more relevant"))
                {
                    return "A"; // Always prefer Document A
                }
                // DirectLlm mode asks for ranking
                return "1, 2, 3";
            });
    }

    private void SetupMockEmbeddingService()
    {
        _mockEmbeddingService.GenerateEmbeddingAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()).Returns(callInfo => { var text = callInfo.ArgAt<string>(0); 
                // Generate a simple embedding based on text hash
                var embedding = new float[384];
                var hash = text.GetHashCode();
                for (int i = 0; i < embedding.Length; i++)
                {
                    embedding[i] = (float)Math.Sin(hash + i) * 0.5f + 0.5f;
                }
                return embedding;
            });
    }

    private RetrievalCandidate CreateCandidate(
        string id,
        string content,
        float score,
        int initialRank = 0,
        Dictionary<string, object>? metadata = null)
    {
        return new RetrievalCandidate
        {
            Id = id,
            DocumentId = $"doc-{id}",
            ChunkId = $"chunk-{id}",
            Content = content,
            InitialScore = score,
            InitialRank = initialRank,
            Metadata = metadata
        };
    }

    private List<RetrievalCandidate> CreateMultipleCandidates(int count)
    {
        var candidates = new List<RetrievalCandidate>();
        for (int i = 0; i < count; i++)
        {
            candidates.Add(CreateCandidate(
                $"{i + 1}",
                $"Content for document {i + 1}",
                1.0f - (i * 0.1f),
                i + 1));
        }
        return candidates;
    }

    #endregion
}
