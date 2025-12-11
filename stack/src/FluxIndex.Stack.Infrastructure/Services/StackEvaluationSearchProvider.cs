using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Shared.DTOs.Search;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Stack implementation of IEvaluationSearchProvider.
/// Bridges Stack's search infrastructure with Core's RAG evaluation framework.
/// </summary>
public class StackEvaluationSearchProvider : IEvaluationSearchProvider
{
    private readonly ISearchService _searchService;
    private readonly ITextCompletionService? _textCompletionService;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly ILogger<StackEvaluationSearchProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the StackEvaluationSearchProvider.
    /// </summary>
    public StackEvaluationSearchProvider(
        ISearchService searchService,
        IDocumentChunkRepository chunkRepository,
        ILogger<StackEvaluationSearchProvider> logger,
        ITextCompletionService? textCompletionService = null)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _chunkRepository = chunkRepository ?? throw new ArgumentNullException(nameof(chunkRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _textCompletionService = textCompletionService;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<DocumentChunk>> RetrieveChunksAsync(
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving chunks for evaluation. Query: {Query}, TopK: {TopK}", query, topK);

        var searchRequest = new SearchRequest
        {
            Query = query,
            TopK = topK,
            Mode = SearchMode.Auto,
            QualityPreference = QualityPreference.Quality,
            IncludeContent = true,
            IncludeMetadata = true
        };

        try
        {
            var response = await _searchService.SearchAsync(searchRequest, cancellationToken: cancellationToken);

            // Convert Stack search results to Core DocumentChunk entities
            var chunks = new List<DocumentChunk>();

            foreach (var result in response.Results)
            {
                var chunk = new DocumentChunk
                {
                    Id = result.ChunkId.ToString(),
                    DocumentId = result.DocumentId.ToString(),
                    Content = result.Content ?? string.Empty,
                    ChunkIndex = result.ChunkIndex,
                    Metadata = result.Metadata ?? new Dictionary<string, object>()
                };

                // Add search score to metadata for evaluation
                chunk.Metadata["search_score"] = result.Score;
                chunk.Metadata["document_title"] = result.DocumentTitle ?? string.Empty;

                chunks.Add(chunk);
            }

            _logger.LogDebug("Retrieved {Count} chunks for evaluation query", chunks.Count);
            return chunks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve chunks for evaluation. Query: {Query}", query);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> GenerateAnswerAsync(
        string query,
        IEnumerable<DocumentChunk> retrievedChunks,
        CancellationToken cancellationToken = default)
    {
        if (_textCompletionService == null)
        {
            _logger.LogWarning("Text completion service not available. Returning concatenated context as answer.");
            return ConcatenateChunkContents(retrievedChunks);
        }

        _logger.LogDebug("Generating answer for evaluation. Query: {Query}", query);

        try
        {
            // Build context from retrieved chunks
            var context = BuildContextFromChunks(retrievedChunks);

            // Generate answer using RAG prompt
            var prompt = BuildRAGPrompt(query, context);
            var answer = await _textCompletionService.GenerateCompletionAsync(
                prompt,
                maxTokens: 1000,
                temperature: 0.3f,
                cancellationToken);

            _logger.LogDebug("Generated answer of length {Length} for evaluation query", answer?.Length ?? 0);
            return answer ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate answer for evaluation. Query: {Query}", query);
            throw;
        }
    }

    /// <summary>
    /// Builds context string from retrieved chunks.
    /// </summary>
    private static string BuildContextFromChunks(IEnumerable<DocumentChunk> chunks)
    {
        var contextParts = new List<string>();
        var index = 1;

        foreach (var chunk in chunks)
        {
            var title = chunk.Metadata.TryGetValue("document_title", out var t) ? t?.ToString() : "Unknown";
            contextParts.Add($"[Document {index}: {title}]\n{chunk.Content}");
            index++;
        }

        return string.Join("\n\n", contextParts);
    }

    /// <summary>
    /// Builds a RAG prompt for answer generation.
    /// </summary>
    private static string BuildRAGPrompt(string query, string context)
    {
        return $"""
            You are a helpful assistant that answers questions based on the provided context.
            Answer the question accurately and concisely using only the information from the context.
            If the context doesn't contain enough information to answer the question, say so.

            Context:
            {context}

            Question: {query}

            Answer:
            """;
    }

    /// <summary>
    /// Concatenates chunk contents as fallback when LLM is not available.
    /// </summary>
    private static string ConcatenateChunkContents(IEnumerable<DocumentChunk> chunks)
    {
        return string.Join("\n\n---\n\n", chunks.Select(c => c.Content));
    }
}
