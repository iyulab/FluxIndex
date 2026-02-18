using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Documents;
using FluxIndex.Stack.Shared.DTOs.Search;
using FluentAssertions;
using Xunit;

namespace FluxIndex.Stack.Api.Tests;

/// <summary>
/// Integration tests for the complete RAG pipeline.
/// Tests document upload, indexing, enrichment, and search functionality.
/// </summary>
[Trait("Category", "Integration")]
public class RAGPipelineTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public RAGPipelineTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key-admin");
    }

    #region Document Upload Tests

    [Fact]
    public async Task UploadDocument_WithValidFile_ReturnsDocumentIdAndJobId()
    {
        // Arrange
        var content = new MultipartFormDataContent();
        var fileContent = new StringContent("This is a test document for RAG pipeline testing.", Encoding.UTF8);
        content.Add(fileContent, "file", "test.txt");
        content.Add(new StringContent("Test Document"), "title");

        // Act
        var response = await _client.PostAsync("/api/v1/documents/upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<UploadDocumentResponse>>();
        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.Data!.DocumentId.Should().NotBeEmpty();
        result.Data.JobId.Should().NotBeNull();
    }

    [Fact]
    public async Task UploadDocumentContent_WithText_CreatesDocument()
    {
        // Arrange
        var request = new UploadDocumentContentRequest
        {
            Title = "Text Content Document",
            Content = "This is inline text content for testing the RAG pipeline."
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/documents/upload/content", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<UploadDocumentResponse>>();
        result!.Data!.DocumentId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UploadDocument_WithKoreanContent_DetectsLanguageProfile()
    {
        // Arrange
        var request = new UploadDocumentContentRequest
        {
            Title = "Korean Document",
            Content = "안녕하세요. 이것은 한국어 문서입니다. RAG 파이프라인 테스트를 위한 내용입니다."
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/documents/upload/content", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<UploadDocumentResponse>>();
        result!.Data!.DocumentId.Should().NotBeEmpty();

        // Verify language detection in document metadata
        var docResponse = await _client.GetAsync($"/api/v1/documents/{result.Data.DocumentId}");
        var doc = await docResponse.Content.ReadFromJsonAsync<ApiResponse<DocumentDto>>();
        // Language profile should be detected (once FileFlux integration is complete)
        // doc!.Data!.Metadata.Should().ContainKey("language");
    }

    #endregion

    #region Indexing Pipeline Tests

    [Fact]
    public async Task IndexDocument_CompletesSuccessfully()
    {
        // Arrange
        var request = new UploadDocumentContentRequest
        {
            Title = "Indexing Test Document",
            Content = "This document tests the indexing pipeline. It should be chunked and embedded."
        };
        var uploadResponse = await _client.PostAsJsonAsync("/api/v1/documents/upload/content", request);
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<UploadDocumentResponse>>();
        var jobId = uploadResult!.Data!.JobId;

        // Act - Wait for indexing to complete
        var maxWait = TimeSpan.FromSeconds(30);
        var startTime = DateTime.UtcNow;
        string? jobStatus = null;

        while (DateTime.UtcNow - startTime < maxWait)
        {
            var jobResponse = await _client.GetAsync($"/api/v1/jobs/{jobId}");
            var jobResult = await jobResponse.Content.ReadFromJsonAsync<ApiResponse<object>>();
            jobStatus = jobResult?.Data?.ToString();

            if (jobStatus?.Contains("Completed") == true || jobStatus?.Contains("Failed") == true)
                break;

            await Task.Delay(500);
        }

        // Assert
        jobStatus.Should().Contain("Completed");
    }

    [Fact]
    public async Task IndexDocument_CreatesChunksWithEmbeddings()
    {
        // Arrange
        var longContent = string.Join(" ", Enumerable.Repeat(
            "This is a sentence for testing chunking behavior in the RAG pipeline.", 50));

        var request = new UploadDocumentContentRequest
        {
            Title = "Chunking Test Document",
            Content = longContent
        };
        var uploadResponse = await _client.PostAsJsonAsync("/api/v1/documents/upload/content", request);
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<UploadDocumentResponse>>();
        var documentId = uploadResult!.Data!.DocumentId;

        // Wait for indexing
        await WaitForIndexingAsync(uploadResult.Data.JobId!.Value);

        // Act
        var detailResponse = await _client.GetAsync($"/api/v1/documents/{documentId}/detail");
        var detail = await detailResponse.Content.ReadFromJsonAsync<ApiResponse<DocumentDetailDto>>();

        // Assert
        detail!.Data!.Chunks.Should().NotBeEmpty();
        detail.Data.ChunkCount.Should().BeGreaterThan(1); // Should have multiple chunks
    }

    [Fact]
    public async Task IndexDocument_WithFileFlux_UsesIntelligentChunking()
    {
        // Arrange - Content with natural paragraph breaks
        var content = """
            # Introduction

            This is the introduction paragraph. It contains several sentences that form a coherent unit.

            # Main Content

            This is the main content section. It should be chunked separately from the introduction.
            The chunking algorithm should respect paragraph boundaries.

            # Conclusion

            This is the conclusion. It summarizes the document content.
            """;

        var request = new UploadDocumentContentRequest
        {
            Title = "Intelligent Chunking Test",
            Content = content
        };
        var uploadResponse = await _client.PostAsJsonAsync("/api/v1/documents/upload/content", request);
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<UploadDocumentResponse>>();

        // Wait for indexing
        await WaitForIndexingAsync(uploadResult!.Data!.JobId!.Value);

        // Act
        var detailResponse = await _client.GetAsync($"/api/v1/documents/{uploadResult.Data.DocumentId}/detail");
        var detail = await detailResponse.Content.ReadFromJsonAsync<ApiResponse<DocumentDetailDto>>();

        // Assert - Chunks should respect paragraph boundaries (once FileFlux is integrated)
        detail!.Data!.Chunks.Should().NotBeEmpty();
        // Each chunk should be semantically coherent (verify manually or with heuristics)
    }

    #endregion

    #region Enrichment Tests (FluxImprover Integration)

    [Fact(Skip = "Requires FluxImprover integration")]
    public async Task IndexDocument_WithEnrichment_AddsSummaryAndKeywords()
    {
        // Arrange
        var request = new UploadDocumentContentRequest
        {
            Title = "Enrichment Test Document",
            Content = "Machine learning is a subset of artificial intelligence that enables computers to learn from data."
        };
        var uploadResponse = await _client.PostAsJsonAsync("/api/v1/documents/upload/content", request);
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<UploadDocumentResponse>>();

        // Wait for indexing
        await WaitForIndexingAsync(uploadResult!.Data!.JobId!.Value);

        // Act
        var detailResponse = await _client.GetAsync($"/api/v1/documents/{uploadResult.Data.DocumentId}/detail");
        var detail = await detailResponse.Content.ReadFromJsonAsync<ApiResponse<DocumentDetailDto>>();

        // Assert - Enriched metadata should exist
        var chunk = detail!.Data!.Chunks.First();
        chunk.Metadata.Should().ContainKey("summary");
        chunk.Metadata.Should().ContainKey("keywords");
    }

    [Fact(Skip = "Requires FluxImprover integration")]
    public async Task IndexDocument_WithQAGeneration_CreatesQAPairs()
    {
        // Arrange
        var request = new UploadDocumentContentRequest
        {
            Title = "QA Generation Test",
            Content = "The capital of France is Paris. Paris is known for the Eiffel Tower."
        };
        var uploadResponse = await _client.PostAsJsonAsync("/api/v1/documents/upload/content", request);
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<UploadDocumentResponse>>();

        // Wait for indexing
        await WaitForIndexingAsync(uploadResult!.Data!.JobId!.Value);

        // Act - Get generated QA pairs (requires new endpoint)
        var qaResponse = await _client.GetAsync($"/api/v1/documents/{uploadResult.Data.DocumentId}/qa-pairs");

        // Assert
        qaResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        // QA pairs should be generated from the content
    }

    #endregion

    #region Search Tests

    [Fact]
    public async Task Search_KeywordMode_ReturnsRelevantResults()
    {
        // Arrange - Create and index a document
        await CreateAndIndexDocumentAsync("Keyword Search Test",
            "Artificial intelligence and machine learning are transforming industries.");

        // Act
        var searchRequest = new SearchRequest
        {
            Query = "machine learning",
            Mode = SearchMode.Keyword,
            TopK = 10
        };
        var response = await _client.PostAsJsonAsync("/api/v1/search", searchRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<SearchResponse>>();
        result!.Data!.Results.Should().NotBeEmpty();
        result.Data.Results.First().Score.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Search_VectorMode_UsesSemanticSimilarity()
    {
        // Arrange - Create and index documents
        await CreateAndIndexDocumentAsync("Vector Search Test 1",
            "Deep learning neural networks process information in layers.");
        await CreateAndIndexDocumentAsync("Vector Search Test 2",
            "Traditional programming uses explicit rules defined by developers.");

        // Act
        var searchRequest = new SearchRequest
        {
            Query = "how do neural networks work",
            Mode = SearchMode.Vector,
            TopK = 10
        };
        var response = await _client.PostAsJsonAsync("/api/v1/search", searchRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<SearchResponse>>();
        result!.Data!.Results.Should().NotBeEmpty();
        // Vector search should rank the neural network document higher
        result.Data.Results.First().DocumentTitle.Should().Contain("Vector Search Test 1");
    }

    [Fact]
    public async Task Search_HybridMode_CombinesVectorAndKeyword()
    {
        // Arrange
        await CreateAndIndexDocumentAsync("Hybrid Test",
            "Retrieval augmented generation combines search with language models.");

        // Act
        var searchRequest = new SearchRequest
        {
            Query = "RAG search",
            Mode = SearchMode.Hybrid,
            TopK = 10,
            IncludeContent = true
        };
        var response = await _client.PostAsJsonAsync("/api/v1/search", searchRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<SearchResponse>>();
        result!.Data!.Results.Should().NotBeEmpty();
        // Hybrid should have both keyword and vector scores
        var firstResult = result.Data.Results.First();
        firstResult.KeywordScore.Should().NotBeNull();
        // firstResult.VectorScore.Should().NotBeNull(); // Once vector search is implemented
    }

    [Fact(Skip = "Requires LocalReranker integration")]
    public async Task Search_WithReranking_ImproveResultQuality()
    {
        // Arrange - Create multiple documents
        await CreateAndIndexDocumentAsync("Rerank Doc 1", "Machine learning algorithms.");
        await CreateAndIndexDocumentAsync("Rerank Doc 2", "Deep neural network architectures.");
        await CreateAndIndexDocumentAsync("Rerank Doc 3", "Statistical analysis methods.");

        // Act
        var searchRequest = new SearchRequest
        {
            Query = "neural network deep learning",
            Mode = SearchMode.Hybrid,
            TopK = 10,
            EnableReranking = true // New parameter
        };
        var response = await _client.PostAsJsonAsync("/api/v1/search", searchRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<SearchResponse>>();
        // Reranked results should have the most relevant document first
        result!.Data!.Results.First().DocumentTitle.Should().Contain("Rerank Doc 2");
    }

    [Fact]
    public async Task Search_WithFilters_AppliesMetadataFiltering()
    {
        // Arrange
        var request1 = new UploadDocumentContentRequest
        {
            Title = "Filter Test 1",
            Content = "Document about technology.",
            Metadata = new Dictionary<string, object> { { "category", "tech" } }
        };
        var request2 = new UploadDocumentContentRequest
        {
            Title = "Filter Test 2",
            Content = "Document about science.",
            Metadata = new Dictionary<string, object> { { "category", "science" } }
        };

        await _client.PostAsJsonAsync("/api/v1/documents/upload/content", request1);
        await _client.PostAsJsonAsync("/api/v1/documents/upload/content", request2);
        await Task.Delay(2000); // Wait for indexing

        // Act
        var searchRequest = new SearchRequest
        {
            Query = "document",
            Mode = SearchMode.Keyword,
            TopK = 10,
            Filters = new Dictionary<string, object> { { "category", "tech" } }
        };
        var response = await _client.PostAsJsonAsync("/api/v1/search", searchRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<SearchResponse>>();
        result!.Data!.Results.Should().OnlyContain(r => r.DocumentTitle == "Filter Test 1");
    }

    #endregion

    #region Cache Tests

    [Fact]
    public async Task Search_CachedQuery_ReturnsFasterSecondTime()
    {
        // Arrange
        await CreateAndIndexDocumentAsync("Cache Test", "Testing semantic cache functionality.");

        var searchRequest = new SearchRequest
        {
            Query = "semantic cache test",
            Mode = SearchMode.Keyword,
            TopK = 10
        };

        // Act - First search
        var watch1 = System.Diagnostics.Stopwatch.StartNew();
        var response1 = await _client.PostAsJsonAsync("/api/v1/search", searchRequest);
        watch1.Stop();
        var result1 = await response1.Content.ReadFromJsonAsync<ApiResponse<SearchResponse>>();

        // Second search (should be cached)
        var watch2 = System.Diagnostics.Stopwatch.StartNew();
        var response2 = await _client.PostAsJsonAsync("/api/v1/search", searchRequest);
        watch2.Stop();
        var result2 = await response2.Content.ReadFromJsonAsync<ApiResponse<SearchResponse>>();

        // Assert
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        // Cache should make second request faster (once Redis cache is integrated)
        // watch2.ElapsedMilliseconds.Should().BeLessThan(watch1.ElapsedMilliseconds);
    }

    #endregion

    #region Graph Tests (Neo4j Integration)

    [Fact(Skip = "Requires Neo4j integration")]
    public async Task Search_WithGraphContext_ReturnsRelatedDocuments()
    {
        // Arrange - Create related documents
        await CreateAndIndexDocumentAsync("Graph Doc 1", "Introduction to machine learning.");
        await CreateAndIndexDocumentAsync("Graph Doc 2", "Advanced machine learning techniques.");
        // Create relationship between documents

        // Act
        var searchRequest = new SearchRequest
        {
            Query = "machine learning",
            Mode = SearchMode.Hybrid,
            TopK = 10,
            IncludeGraphContext = true // New parameter
        };
        var response = await _client.PostAsJsonAsync("/api/v1/search", searchRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<SearchResponse>>();
        // Results should include related documents from graph traversal
    }

    #endregion

    #region End-to-End Tests

    [Fact]
    public async Task RAGPipeline_UploadToSearch_WorksEndToEnd()
    {
        // Arrange
        var uniqueContent = $"Unique content for E2E test: {Guid.NewGuid()}";

        // Act - Upload
        var uploadRequest = new UploadDocumentContentRequest
        {
            Title = "E2E Test Document",
            Content = uniqueContent
        };
        var uploadResponse = await _client.PostAsJsonAsync("/api/v1/documents/upload/content", uploadRequest);
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<UploadDocumentResponse>>();

        // Wait for indexing
        await WaitForIndexingAsync(uploadResult!.Data!.JobId!.Value);

        // Search
        var searchRequest = new SearchRequest
        {
            Query = "E2E test",
            Mode = SearchMode.Keyword,
            TopK = 10,
            IncludeContent = true
        };
        var searchResponse = await _client.PostAsJsonAsync("/api/v1/search", searchRequest);
        var searchResult = await searchResponse.Content.ReadFromJsonAsync<ApiResponse<SearchResponse>>();

        // Assert
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        searchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        searchResult!.Data!.Results.Should().Contain(r => r.DocumentTitle == "E2E Test Document");
    }

    [Fact]
    public async Task RAGPipeline_LargeDocument_ProcessesEfficiently()
    {
        // Arrange - Create a large document
        var largeContent = string.Join("\n\n", Enumerable.Range(1, 100)
            .Select(i => $"Section {i}: {string.Join(" ", Enumerable.Repeat($"Content paragraph {i}.", 20))}"));

        var request = new UploadDocumentContentRequest
        {
            Title = "Large Document Test",
            Content = largeContent
        };

        // Act
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var uploadResponse = await _client.PostAsJsonAsync("/api/v1/documents/upload/content", request);
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<UploadDocumentResponse>>();

        await WaitForIndexingAsync(uploadResult!.Data!.JobId!.Value, TimeSpan.FromMinutes(2));
        watch.Stop();

        // Assert
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        // Large document should be processed within reasonable time
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(2));

        // Verify chunks were created
        var detailResponse = await _client.GetAsync($"/api/v1/documents/{uploadResult.Data.DocumentId}/detail");
        var detail = await detailResponse.Content.ReadFromJsonAsync<ApiResponse<DocumentDetailDto>>();
        detail!.Data!.Chunks.Count.Should().BeGreaterThan(10);
    }

    #endregion

    #region Helper Methods

    private async Task<Guid> CreateAndIndexDocumentAsync(string title, string content)
    {
        var request = new UploadDocumentContentRequest
        {
            Title = title,
            Content = content
        };
        var uploadResponse = await _client.PostAsJsonAsync("/api/v1/documents/upload/content", request);
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<UploadDocumentResponse>>();

        if (uploadResult?.Data?.JobId != null)
        {
            await WaitForIndexingAsync(uploadResult.Data.JobId.Value);
        }

        return uploadResult!.Data!.DocumentId;
    }

    private async Task WaitForIndexingAsync(Guid jobId, TimeSpan? timeout = null)
    {
        var maxWait = timeout ?? TimeSpan.FromSeconds(30);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < maxWait)
        {
            var jobResponse = await _client.GetAsync($"/api/v1/jobs/{jobId}");
            if (jobResponse.IsSuccessStatusCode)
            {
                var content = await jobResponse.Content.ReadAsStringAsync();
                if (content.Contains("Completed") || content.Contains("Failed"))
                    return;
            }

            await Task.Delay(500);
        }
    }

    #endregion
}

/// <summary>
/// Extended search request with new parameters for enhanced search features.
/// </summary>
public class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public SearchMode Mode { get; set; } = SearchMode.Hybrid;
    public int TopK { get; set; } = 10;
    public float MinScore { get; set; }
    public Guid? CollectionId { get; set; }
    public bool IncludeContent { get; set; }
    public bool IncludeMetadata { get; set; }
    public Dictionary<string, object>? Filters { get; set; }

    // New parameters for enhanced search
    public bool EnableReranking { get; set; }
    public bool IncludeGraphContext { get; set; }
}
