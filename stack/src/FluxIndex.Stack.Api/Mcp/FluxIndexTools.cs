using FluxIndex.SDK;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Shared.DTOs.Documents;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace FluxIndex.Stack.Api.Mcp;

/// <summary>
/// MCP Server Tools for FluxIndex RAG operations.
/// Provides LLM-invocable tools for memorizing, searching, and managing knowledge base.
/// </summary>
[McpServerToolType]
public class FluxIndexTools
{
    private readonly IFluxIndexContext? _fluxIndex;
    private readonly IDocumentService _documentService;
    private readonly ISearchService _searchService;
    private readonly ILogger<FluxIndexTools> _logger;

    public FluxIndexTools(
        IDocumentService documentService,
        ISearchService searchService,
        ILogger<FluxIndexTools> logger,
        IFluxIndexContext? fluxIndex = null)
    {
        _fluxIndex = fluxIndex;
        _documentService = documentService;
        _searchService = searchService;
        _logger = logger;
    }

    /// <summary>
    /// Index content into the knowledge base for later retrieval.
    /// </summary>
    /// <param name="content">The text content to memorize</param>
    /// <param name="title">A descriptive title for the content</param>
    /// <param name="metadata">Optional JSON metadata object</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result with document ID and resource URI</returns>
    [McpServerTool]
    [Description("Memorize content into the RAG knowledge base for semantic search. Returns the document ID and resource URI.")]
    public async Task<MemorizeToolResult> Memorize(
        [Description("The text content to memorize and index")] string content,
        [Description("A descriptive title for the content")] string title,
        [Description("Optional JSON metadata string (e.g., '{\"author\": \"John\", \"tags\": [\"docs\"]}')")] string? metadata = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP Tool Memorize: {Title}", title);

        try
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return new MemorizeToolResult
                {
                    Success = false,
                    Error = "Content is required"
                };
            }

            Dictionary<string, object>? metadataDict = null;
            if (!string.IsNullOrWhiteSpace(metadata))
            {
                try
                {
                    metadataDict = JsonSerializer.Deserialize<Dictionary<string, object>>(metadata);
                }
                catch (JsonException)
                {
                    return new MemorizeToolResult
                    {
                        Success = false,
                        Error = "Invalid metadata JSON format"
                    };
                }
            }

            if (_fluxIndex != null)
            {
                var documentId = await _fluxIndex.Indexer.IndexDocumentAsync(
                    content,
                    title,
                    metadataDict,
                    cancellationToken);

                _logger.LogInformation("MCP Memorize completed: {DocumentId}", documentId);

                return new MemorizeToolResult
                {
                    Success = true,
                    DocumentId = documentId,
                    Title = title,
                    ResourceUri = $"flux://docs/{documentId}",
                    Message = $"Successfully memorized '{title}'"
                };
            }
            else
            {
                var response = await _documentService.UploadContentAsync(
                    new UploadDocumentContentRequest
                    {
                        Title = title,
                        Content = content,
                        SourceType = "mcp",
                        Metadata = metadataDict
                    },
                    cancellationToken);

                return new MemorizeToolResult
                {
                    Success = true,
                    DocumentId = response.DocumentId.ToString(),
                    Title = title,
                    ResourceUri = $"flux://docs/{response.DocumentId}",
                    Message = response.Message
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP Memorize failed");
            return new MemorizeToolResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Search the knowledge base using semantic similarity.
    /// </summary>
    /// <param name="query">The search query text</param>
    /// <param name="maxResults">Maximum number of results to return (default: 10)</param>
    /// <param name="minScore">Minimum similarity score threshold 0-1 (default: 0.2)</param>
    /// <param name="searchType">Search type: 'vector', 'hybrid', 'quantized' (default: vector)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Search results with relevance scores</returns>
    [McpServerTool]
    [Description("Search the RAG knowledge base using semantic similarity. Returns ranked results with content excerpts and relevance scores.")]
    public async Task<SearchToolResult> Search(
        [Description("The natural language search query")] string query,
        [Description("Maximum results to return (1-100, default: 10)")] int maxResults = 10,
        [Description("Minimum similarity score 0-1 (default: 0.2)")] float minScore = 0.2f,
        [Description("Search type: 'vector', 'hybrid', or 'quantized'")] string searchType = "vector",
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP Tool Search: {Query}", query);

        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new SearchToolResult
                {
                    Success = false,
                    Error = "Query is required"
                };
            }

            maxResults = Math.Clamp(maxResults, 1, 100);
            minScore = Math.Clamp(minScore, 0f, 1f);

            if (_fluxIndex != null)
            {
                IEnumerable<FluxIndex.SDK.SearchResult> results;

                if (searchType.Equals("quantized", StringComparison.OrdinalIgnoreCase) && _fluxIndex.SupportsQuantization)
                {
                    results = await _fluxIndex.SearchQuantizedAsync(query, maxResults, minScore, cancellationToken);
                }
                else
                {
                    results = await _fluxIndex.SearchAsync(query, maxResults, minScore, null, cancellationToken);
                }

                var searchResults = results.Select(r => new SearchResultItem
                {
                    DocumentId = r.DocumentId,
                    ChunkId = r.Id,
                    Content = r.Content,
                    Score = r.Score,
                    ChunkIndex = r.ChunkIndex,
                    ResourceUri = $"flux://docs/{r.DocumentId}/chunks/{r.Id}"
                }).ToList();

                return new SearchToolResult
                {
                    Success = true,
                    Query = query,
                    ResultCount = searchResults.Count,
                    Results = searchResults
                };
            }
            else
            {
                var searchResult = await _searchService.SearchAsync(
                    new Shared.DTOs.Search.SearchRequest { Query = query, TopK = maxResults },
                    null, cancellationToken);

                var searchResults = searchResult.Results.Select(r => new SearchResultItem
                {
                    DocumentId = r.DocumentId.ToString(),
                    ChunkId = r.ChunkId.ToString(),
                    Content = r.Content,
                    Score = (float)r.Score
                }).ToList();

                return new SearchToolResult
                {
                    Success = true,
                    Query = query,
                    ResultCount = searchResults.Count,
                    Results = searchResults
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP Search failed");
            return new SearchToolResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Remove a document from the knowledge base.
    /// </summary>
    /// <param name="documentId">The document ID to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure</returns>
    [McpServerTool]
    [Description("Remove a document from the knowledge base by its ID. This operation is irreversible.")]
    public async Task<UnmemorizeToolResult> Unmemorize(
        [Description("The document ID to remove from knowledge base")] string documentId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP Tool Unmemorize: {DocumentId}", documentId);

        try
        {
            if (string.IsNullOrWhiteSpace(documentId))
            {
                return new UnmemorizeToolResult
                {
                    Success = false,
                    Error = "Document ID is required"
                };
            }

            bool deleted = false;

            if (_fluxIndex != null)
            {
                deleted = await _fluxIndex.DeleteDocumentAsync(documentId, cancellationToken);
            }
            else if (Guid.TryParse(documentId, out var guidId))
            {
                await _documentService.DeleteAsync(guidId, cancellationToken);
                deleted = true;
            }

            if (deleted)
            {
                return new UnmemorizeToolResult
                {
                    Success = true,
                    DocumentId = documentId,
                    Message = $"Successfully removed document '{documentId}' from knowledge base"
                };
            }
            else
            {
                return new UnmemorizeToolResult
                {
                    Success = false,
                    Error = $"Document '{documentId}' was not found in the knowledge base"
                };
            }
        }
        catch (KeyNotFoundException)
        {
            return new UnmemorizeToolResult
            {
                Success = false,
                Error = $"Document '{documentId}' was not found"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP Unmemorize failed");
            return new UnmemorizeToolResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Get system status and statistics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>System status and statistics</returns>
    [McpServerTool]
    [Description("Get the current status and statistics of the RAG knowledge base, including document counts and capabilities.")]
    public async Task<StatusToolResult> Status(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP Tool Status");

        try
        {
            if (_fluxIndex != null)
            {
                var stats = await _fluxIndex.GetStatisticsAsync(cancellationToken);

                return new StatusToolResult
                {
                    Success = true,
                    Status = "healthy",
                    TotalDocuments = stats.TotalDocuments,
                    TotalChunks = stats.TotalChunks,
                    AverageChunksPerDocument = stats.AverageChunksPerDocument,
                    VectorStoreProvider = stats.VectorStoreProvider,
                    EmbeddingModel = stats.EmbeddingModel,
                    SupportsQuantization = _fluxIndex.SupportsQuantization,
                    SupportsHybridSearch = true,
                    CacheEnabled = stats.CacheEnabled
                };
            }
            else
            {
                return new StatusToolResult
                {
                    Success = true,
                    Status = "healthy",
                    VectorStoreProvider = "PostgreSQL",
                    CacheEnabled = true,
                    Message = "FluxIndex SDK not configured. Using basic document service."
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP Status check failed");
            return new StatusToolResult
            {
                Success = false,
                Status = "error",
                Message = ex.Message
            };
        }
    }

    /// <summary>
    /// Get chunks for a specific document.
    /// </summary>
    /// <param name="documentId">The document ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Document chunks with content and metadata</returns>
    [McpServerTool]
    [Description("Retrieve all text chunks for a specific document. Useful for understanding how content was segmented.")]
    public async Task<GetChunksToolResult> GetChunks(
        [Description("The document ID to get chunks for")] string documentId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP Tool GetChunks: {DocumentId}", documentId);

        try
        {
            if (string.IsNullOrWhiteSpace(documentId))
            {
                return new GetChunksToolResult
                {
                    Success = false,
                    Error = "Document ID is required"
                };
            }

            if (_fluxIndex != null)
            {
                var document = await _fluxIndex.GetDocumentAsync(documentId, cancellationToken);

                if (document == null)
                {
                    return new GetChunksToolResult
                    {
                        Success = false,
                        Error = $"Document '{documentId}' was not found"
                    };
                }

                var chunks = document.Chunks.Select(c => new ChunkInfo
                {
                    ChunkId = c.Id,
                    Content = c.Content,
                    ChunkIndex = c.ChunkIndex,
                    TokenCount = c.TokenCount
                }).ToList();

                return new GetChunksToolResult
                {
                    Success = true,
                    DocumentId = documentId,
                    Title = document.FileName,
                    ChunkCount = chunks.Count,
                    Chunks = chunks
                };
            }
            else if (Guid.TryParse(documentId, out var guidId))
            {
                var detail = await _documentService.GetDetailByIdAsync(guidId, cancellationToken);

                if (detail == null)
                {
                    return new GetChunksToolResult
                    {
                        Success = false,
                        Error = $"Document '{documentId}' was not found"
                    };
                }

                return new GetChunksToolResult
                {
                    Success = true,
                    DocumentId = documentId,
                    Title = detail.Title,
                    ChunkCount = detail.ChunkCount
                };
            }
            else
            {
                return new GetChunksToolResult
                {
                    Success = false,
                    Error = "Document ID must be a valid GUID"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP GetChunks failed");
            return new GetChunksToolResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Re-index a document with updated content processing.
    /// </summary>
    /// <param name="documentId">The document ID to re-index</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating the reindex was queued</returns>
    [McpServerTool]
    [Description("Queue a document for re-indexing with updated chunking and embeddings. Use after content updates.")]
    public async Task<ReindexToolResult> Reindex(
        [Description("The document ID to re-index")] string documentId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP Tool Reindex: {DocumentId}", documentId);

        try
        {
            if (string.IsNullOrWhiteSpace(documentId))
            {
                return new ReindexToolResult
                {
                    Success = false,
                    Error = "Document ID is required"
                };
            }

            if (Guid.TryParse(documentId, out var guidId))
            {
                await _documentService.ReindexAsync(guidId, cancellationToken);

                return new ReindexToolResult
                {
                    Success = true,
                    DocumentId = documentId,
                    Message = "Document queued for re-indexing"
                };
            }
            else
            {
                return new ReindexToolResult
                {
                    Success = false,
                    Error = "Document ID must be a valid GUID"
                };
            }
        }
        catch (KeyNotFoundException)
        {
            return new ReindexToolResult
            {
                Success = false,
                Error = $"Document '{documentId}' was not found"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP Reindex failed");
            return new ReindexToolResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }
}

#region Tool Result DTOs

public class MemorizeToolResult
{
    public bool Success { get; set; }
    public string? DocumentId { get; set; }
    public string? Title { get; set; }
    public string? ResourceUri { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}

public class SearchToolResult
{
    public bool Success { get; set; }
    public string? Query { get; set; }
    public int ResultCount { get; set; }
    public List<SearchResultItem>? Results { get; set; }
    public string? Error { get; set; }
}

public class SearchResultItem
{
    public string DocumentId { get; set; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public float Score { get; set; }
    public int ChunkIndex { get; set; }
    public string? ResourceUri { get; set; }
}

public class UnmemorizeToolResult
{
    public bool Success { get; set; }
    public string? DocumentId { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}

public class StatusToolResult
{
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalDocuments { get; set; }
    public int TotalChunks { get; set; }
    public double AverageChunksPerDocument { get; set; }
    public string? VectorStoreProvider { get; set; }
    public string? EmbeddingModel { get; set; }
    public bool SupportsQuantization { get; set; }
    public bool SupportsHybridSearch { get; set; }
    public bool CacheEnabled { get; set; }
    public string? Message { get; set; }
}

public class GetChunksToolResult
{
    public bool Success { get; set; }
    public string? DocumentId { get; set; }
    public string? Title { get; set; }
    public int ChunkCount { get; set; }
    public List<ChunkInfo>? Chunks { get; set; }
    public string? Error { get; set; }
}

public class ChunkInfo
{
    public string ChunkId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public int TokenCount { get; set; }
}

public class ReindexToolResult
{
    public bool Success { get; set; }
    public string? DocumentId { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}

#endregion
