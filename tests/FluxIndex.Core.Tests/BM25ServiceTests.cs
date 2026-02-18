using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Services;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests;

public class BM25ServiceTests
{
    private readonly IDocumentRepository _mockDocumentRepository;
    private readonly IVectorStore _mockVectorStore;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BM25Service> _logger;
    private readonly BM25Service _service;

    public BM25ServiceTests()
    {
        _mockDocumentRepository = Substitute.For<IDocumentRepository>();
        _mockVectorStore = Substitute.For<IVectorStore>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _logger = NullLogger<BM25Service>.Instance;

        _service = new BM25Service(
            _mockDocumentRepository,
            _mockVectorStore,
            _cache,
            _logger);
    }

    [Fact]
    public async Task CalculateScore_ValidQuery_ReturnsPositiveScore()
    {
        // Arrange
        var query = "machine learning algorithm";
        var document = "This document is about machine learning algorithms and their applications";
        var averageDocLength = 10.0f;

        // Setup mock documents for IDF calculation (need at least 3 documents for positive IDF)
        var doc1 = Document.Create("doc1");
        var doc2 = Document.Create("doc2");
        var doc3 = Document.Create("doc3");
        var chunk1 = new DocumentChunk { Id = "chunk1", DocumentId = "doc1", Content = document };
        var chunk2 = new DocumentChunk { Id = "chunk2", DocumentId = "doc2", Content = "Some other content about different topics" };
        var chunk3 = new DocumentChunk { Id = "chunk3", DocumentId = "doc3", Content = "data science and neural networks" };

        _mockDocumentRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { doc1, doc2, doc3 });
        _mockVectorStore.GetByDocumentIdAsync("doc1", Arg.Any<CancellationToken>()).Returns(new[] { chunk1 });
        _mockVectorStore.GetByDocumentIdAsync("doc2", Arg.Any<CancellationToken>()).Returns(new[] { chunk2 });
        _mockVectorStore.GetByDocumentIdAsync("doc3", Arg.Any<CancellationToken>()).Returns(new[] { chunk3 });

        // Initialize IDF cache
        await _service.UpdateIDFCacheAsync();

        // Act
        var score = _service.CalculateScore(query, document, averageDocLength);

        // Debug: Check tokens and IDF values
        var queryTokens = _service.Tokenize(query).ToList();
        var docTokens = _service.Tokenize(document).ToList();

        // Assert
        Assert.True(queryTokens.Any(), "Query should have tokens");
        Assert.True(docTokens.Any(), "Document should have tokens");
        Assert.True(score > 0, $"Score should be > 0, but was {score}. Query tokens: {string.Join(", ", queryTokens)}");
    }

    [Fact]
    public void CalculateScore_NoMatchingTerms_ReturnsZero()
    {
        // Arrange
        var query = "artificial intelligence";
        var document = "This is about database systems";
        var averageDocLength = 10.0f;

        // Act
        var score = _service.CalculateScore(query, document, averageDocLength);

        // Assert
        Assert.Equal(0f, score);
    }

    [Fact]
    public void CalculateScore_EmptyQuery_ReturnsZero()
    {
        // Arrange
        var query = "";
        var document = "Some document content";
        var averageDocLength = 10.0f;

        // Act
        var score = _service.CalculateScore(query, document, averageDocLength);

        // Assert
        Assert.Equal(0f, score);
    }

    [Fact]
    public void CalculateScore_EmptyDocument_ReturnsZero()
    {
        // Arrange
        var query = "test query";
        var document = "";
        var averageDocLength = 10.0f;

        // Act
        var score = _service.CalculateScore(query, document, averageDocLength);

        // Assert
        Assert.Equal(0f, score);
    }

    [Fact]
    public async Task CalculateScore_HigherTermFrequency_ReturnsHigherScore()
    {
        // Arrange - Use 5 documents where only 2 contain the query term for positive IDF
        var query = "algorithms";
        var doc1 = "algorithms are important for algorithms design and algorithms implementation";  // "algorithms" appears 3 times
        var doc2 = "algorithms are useful";  // "algorithms" appears 1 time
        var averageDocLength = 10.0f;

        // Setup 5 documents: "algorithms" in doc1 and doc2, not in doc3-5
        // IDF = log((5-2+0.5)/(2+0.5)) = log(3.5/2.5) = log(1.4) = 0.34 (positive!)
        var document1 = Document.Create("doc1");
        var document2 = Document.Create("doc2");
        var document3 = Document.Create("doc3");
        var document4 = Document.Create("doc4");
        var document5 = Document.Create("doc5");
        var chunk1 = new DocumentChunk { Id = "chunk1", DocumentId = "doc1", Content = doc1 };
        var chunk2 = new DocumentChunk { Id = "chunk2", DocumentId = "doc2", Content = doc2 };
        var chunk3 = new DocumentChunk { Id = "chunk3", DocumentId = "doc3", Content = "data science and analytics" };
        var chunk4 = new DocumentChunk { Id = "chunk4", DocumentId = "doc4", Content = "machine learning models" };
        var chunk5 = new DocumentChunk { Id = "chunk5", DocumentId = "doc5", Content = "neural network architecture" };

        _mockDocumentRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { document1, document2, document3, document4, document5 });
        _mockVectorStore.GetByDocumentIdAsync("doc1", Arg.Any<CancellationToken>()).Returns(new[] { chunk1 });
        _mockVectorStore.GetByDocumentIdAsync("doc2", Arg.Any<CancellationToken>()).Returns(new[] { chunk2 });
        _mockVectorStore.GetByDocumentIdAsync("doc3", Arg.Any<CancellationToken>()).Returns(new[] { chunk3 });
        _mockVectorStore.GetByDocumentIdAsync("doc4", Arg.Any<CancellationToken>()).Returns(new[] { chunk4 });
        _mockVectorStore.GetByDocumentIdAsync("doc5", Arg.Any<CancellationToken>()).Returns(new[] { chunk5 });

        // Initialize IDF cache
        await _service.UpdateIDFCacheAsync();

        // Act
        var score1 = _service.CalculateScore(query, doc1, averageDocLength);
        var score2 = _service.CalculateScore(query, doc2, averageDocLength);

        // Assert - doc1 has higher term frequency (3 occurrences vs 1), should have higher score
        Assert.True(score1 > score2, $"doc1 score ({score1}) should be > doc2 score ({score2})");
    }

    [Theory]
    [InlineData("hello world", 2)]
    [InlineData("the quick brown fox", 4)]
    [InlineData("AI ML DL", 3)]
    [InlineData("", 0)]
    public void Tokenize_VariousInputs_ReturnsCorrectTokenCount(string text, int expectedCount)
    {
        // Act
        var tokens = _service.Tokenize(text).ToList();

        // Assert
        Assert.Equal(expectedCount, tokens.Count);
    }

    [Fact]
    public void Tokenize_KoreanText_TokenizesCorrectly()
    {
        // Arrange
        var text = "머신러닝과 딥러닝은 인공지능의 핵심 기술입니다";

        // Act
        var tokens = _service.Tokenize(text).ToList();

        // Assert
        Assert.True(tokens.Count > 0);
        Assert.Contains(tokens, t => t.Contains("머신러닝") || t.Contains("딥러닝"));
    }

    [Fact]
    public void Tokenize_MixedLanguage_TokenizesBoth()
    {
        // Arrange
        var text = "Machine Learning 머신러닝 is important";

        // Act
        var tokens = _service.Tokenize(text).ToList();

        // Assert
        Assert.True(tokens.Count >= 3);
    }

    [Fact]
    public void Tokenize_CaseInsensitive_ReturnsLowercaseTokens()
    {
        // Arrange
        var text = "Machine LEARNING Algorithm";

        // Act
        var tokens = _service.Tokenize(text).ToList();

        // Assert
        Assert.All(tokens, token => Assert.Equal(token, token.ToLowerInvariant()));
    }

    [Fact]
    public async Task GetIDF_UnknownTerm_ReturnsDefaultIDF()
    {
        // Arrange
        var unknownTerm = "supercalifragilisticexpialidocious";

        // Setup mock documents for IDF calculation (need at least 2 documents for meaningful IDF)
        var doc1 = Document.Create("doc1");
        var doc2 = Document.Create("doc2");
        var chunk1 = new DocumentChunk { Id = "chunk1", DocumentId = "doc1", Content = "test content" };
        var chunk2 = new DocumentChunk { Id = "chunk2", DocumentId = "doc2", Content = "other document" };

        _mockDocumentRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { doc1, doc2 });
        _mockVectorStore.GetByDocumentIdAsync("doc1", Arg.Any<CancellationToken>()).Returns(new[] { chunk1 });
        _mockVectorStore.GetByDocumentIdAsync("doc2", Arg.Any<CancellationToken>()).Returns(new[] { chunk2 });

        // Initialize IDF cache
        await _service.UpdateIDFCacheAsync();

        // Act
        var idf = _service.GetIDF(unknownTerm);

        // Assert
        Assert.True(idf > 0); // Should return a positive default IDF
    }

    [Fact]
    public async Task SearchAsync_NoDocuments_ReturnsEmpty()
    {
        // Arrange
        var query = "test query";
        _mockDocumentRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<Document>());

        // Act
        var results = await _service.SearchAsync(query);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ValidDocuments_ReturnsRankedResults()
    {
        // Arrange
        var query = "machine learning";

        var doc1 = Document.Create("doc1");
        var doc2 = Document.Create("doc2");
        var doc3 = Document.Create("doc3");

        var chunk1 = new DocumentChunk
        {
            Id = "chunk1",
            DocumentId = "doc1",
            Content = "This is about machine learning and deep learning"
        };

        var chunk2 = new DocumentChunk
        {
            Id = "chunk2",
            DocumentId = "doc2",
            Content = "This is about database systems"
        };

        var chunk3 = new DocumentChunk
        {
            Id = "chunk3",
            DocumentId = "doc3",
            Content = "This is about data science and analytics"
        };

        _mockDocumentRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { doc1, doc2, doc3 });

        _mockVectorStore.GetByDocumentIdAsync("doc1", Arg.Any<CancellationToken>()).Returns(new[] { chunk1 });

        _mockVectorStore.GetByDocumentIdAsync("doc2", Arg.Any<CancellationToken>()).Returns(new[] { chunk2 });

        _mockVectorStore.GetByDocumentIdAsync("doc3", Arg.Any<CancellationToken>()).Returns(new[] { chunk3 });

        // Act
        var results = await _service.SearchAsync(query, topK: 10);

        // Assert
        Assert.NotEmpty(results);
        var resultList = results.ToList();

        // doc1 should rank higher as it contains "machine learning"
        Assert.Equal("chunk1", resultList[0].ChunkId);
        Assert.True(resultList[0].Score > 0);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        // Arrange
        var query = "";

        // Act
        var results = await _service.SearchAsync(query);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_TopKLimit_ReturnsCorrectCount()
    {
        // Arrange
        var query = "machine";
        var topK = 2;

        var documents = Enumerable.Range(1, 5)
            .Select(i => Document.Create($"doc{i}"))
            .ToList();

        // Create varied content so not all terms appear in all documents
        var contents = new[]
        {
            "machine learning algorithms",
            "machine intelligence systems",
            "data science analytics",
            "neural network architecture",
            "computer vision processing"
        };

        _mockDocumentRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(documents);

        for (int i = 0; i < documents.Count; i++)
        {
            var doc = documents[i];
            var chunk = new DocumentChunk
            {
                Id = $"chunk-{doc.Id}",
                DocumentId = doc.Id,
                Content = contents[i]
            };

            _mockVectorStore.GetByDocumentIdAsync(doc.Id, Arg.Any<CancellationToken>()).Returns(new[] { chunk });
        }

        // Act
        var results = await _service.SearchAsync(query, topK);

        // Assert
        Assert.Equal(topK, results.Count());
    }

    [Fact]
    public async Task UpdateIDFCacheAsync_ValidCorpus_CalculatesIDF()
    {
        // Arrange - Need at least 3 documents for positive IDF values
        var doc1 = Document.Create("doc1");
        var doc2 = Document.Create("doc2");
        var doc3 = Document.Create("doc3");

        var chunk1 = new DocumentChunk
        {
            Id = "chunk1",
            DocumentId = "doc1",
            Content = "machine learning algorithms"
        };

        var chunk2 = new DocumentChunk
        {
            Id = "chunk2",
            DocumentId = "doc2",
            Content = "deep neural networks"  // Removed "learning" to make it rarer
        };

        var chunk3 = new DocumentChunk
        {
            Id = "chunk3",
            DocumentId = "doc3",
            Content = "data science and analytics"
        };

        _mockDocumentRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { doc1, doc2, doc3 });

        _mockVectorStore.GetByDocumentIdAsync("doc1", Arg.Any<CancellationToken>()).Returns(new[] { chunk1 });

        _mockVectorStore.GetByDocumentIdAsync("doc2", Arg.Any<CancellationToken>()).Returns(new[] { chunk2 });

        _mockVectorStore.GetByDocumentIdAsync("doc3", Arg.Any<CancellationToken>()).Returns(new[] { chunk3 });

        // Act
        await _service.UpdateIDFCacheAsync();

        // Assert - Terms should have IDF values
        // "machine" appears in 1/3 documents, should have positive IDF
        // "learning" appears in 1/3 documents, should have positive IDF
        // "algorithms" appears in 1/3 documents
        // "data" appears in 1/3 documents
        var machineIdf = _service.GetIDF("machine");
        var learningIdf = _service.GetIDF("learning");
        var algorithmsIdf = _service.GetIDF("algorithms");

        Assert.True(machineIdf > 0, $"machine IDF should be > 0, but was {machineIdf}");
        Assert.True(learningIdf > 0, $"learning IDF should be > 0, but was {learningIdf}");
        Assert.True(algorithmsIdf > 0, $"algorithms IDF should be > 0, but was {algorithmsIdf}");
    }

    [Fact]
    public async Task UpdateIDFCacheAsync_NoDocuments_HandlesGracefully()
    {
        // Arrange
        _mockDocumentRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<Document>());

        // Act & Assert - Should not throw
        await _service.UpdateIDFCacheAsync();

        var idf = _service.GetIDF("test");
        // With 0 documents, default IDF is log(0+1) = log(1) = 0
        Assert.True(idf >= 0, "IDF should be non-negative even with no documents");
    }

    [Theory]
    [InlineData(1.2f, 0.75f)]  // Default parameters
    [InlineData(1.5f, 0.5f)]   // Custom parameters
    [InlineData(1.0f, 1.0f)]   // Edge case parameters
    public void Constructor_CustomParameters_AcceptsParameters(float k1, float b)
    {
        // Act
        var customService = new BM25Service(
            _mockDocumentRepository,
            _mockVectorStore,
            _cache,
            _logger,
            k1,
            b);

        // Assert - Should create without throwing
        Assert.NotNull(customService);
    }
}
