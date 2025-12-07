using FluxIndex.SDK;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Shared.DTOs.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace FluxIndex.Stack.Api.Controllers;

/// <summary>
/// MCP (Model Context Protocol) endpoints for AI assistant integration.
/// Provides tools for memorizing, searching, and managing knowledge base content.
/// </summary>
[ApiController]
[Route("mcp")]
[AllowAnonymous] // MCP endpoints use their own authentication
public class McpController : ControllerBase
{
    private readonly IFluxIndexContext? _fluxIndex;
    private readonly IDocumentService _documentService;
    private readonly ISearchService _searchService;
    private readonly ILogger<McpController> _logger;

    public McpController(
        IDocumentService documentService,
        ISearchService searchService,
        ILogger<McpController> logger,
        IFluxIndexContext? fluxIndex = null)
    {
        _fluxIndex = fluxIndex;
        _documentService = documentService;
        _searchService = searchService;
        _logger = logger;
    }

    /// <summary>
    /// Get MCP server information and available tools.
    /// </summary>
    [HttpGet("")]
    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        return Ok(new
        {
            name = "FluxIndex Stack MCP Server",
            version = "1.0.0",
            description = "RAG knowledge base for AI assistants",
            capabilities = new
            {
                memorize = true,
                search = true,
                unmemorize = true,
                status = true,
                chunks = true,
                reindex = true
            },
            tools = new[]
            {
                new { name = "memorize", description = "Index content into the knowledge base" },
                new { name = "search", description = "Search the knowledge base" },
                new { name = "unmemorize", description = "Remove content from the knowledge base" },
                new { name = "status", description = "Get system status and statistics" },
                new { name = "get_chunks", description = "Get chunks for a document" },
                new { name = "reindex", description = "Re-index a document" }
            }
        });
    }

    /// <summary>
    /// Index content into the knowledge base.
    /// </summary>
    [HttpPost("tools/memorize")]
    public async Task<IActionResult> Memorize(
        [FromBody] MemorizeRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("MCP Memorize: {Title}", request.Title ?? request.SourcePath);

        try
        {
            if (string.IsNullOrWhiteSpace(request.Content) && string.IsNullOrWhiteSpace(request.SourcePath))
            {
                return BadRequest(new McpErrorResponse
                {
                    Error = "Invalid request",
                    Message = "Either 'content' or 'sourcePath' must be provided"
                });
            }

            // If content is provided directly, use it
            string content = request.Content ?? string.Empty;
            string title = request.Title ?? Path.GetFileName(request.SourcePath) ?? "Untitled";

            // If source path is provided and content is empty, try to read the file
            if (string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(request.SourcePath))
            {
                if (request.SourcePath.StartsWith("http://") || request.SourcePath.StartsWith("https://"))
                {
                    // URL - would need WebFlux integration
                    return BadRequest(new McpErrorResponse
                    {
                        Error = "URL not supported",
                        Message = "URL memorization is not yet implemented. Please provide content directly."
                    });
                }

                if (!System.IO.File.Exists(request.SourcePath))
                {
                    return NotFound(new McpErrorResponse
                    {
                        Error = "File not found",
                        Message = $"File '{request.SourcePath}' does not exist"
                    });
                }

                content = await System.IO.File.ReadAllTextAsync(request.SourcePath, cancellationToken);
            }

            // Use FluxIndex SDK if available
            if (_fluxIndex != null)
            {
                var metadataDict = request.Metadata?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (object)kvp.Value);

                var documentId = await _fluxIndex.Indexer.IndexDocumentAsync(
                    content,
                    title,
                    metadataDict,
                    cancellationToken);

                _logger.LogInformation("MCP Memorize completed: {DocumentId}", documentId);

                return Ok(new MemorizeResponse
                {
                    Success = true,
                    DocumentId = documentId,
                    Title = title,
                    ResourceUri = $"flux://docs/{documentId}",
                    Message = $"Successfully memorized '{title}'"
                });
            }
            else
            {
                // Fallback to document service
                var response = await _documentService.UploadContentAsync(
                    new UploadDocumentContentRequest
                    {
                        Title = title,
                        Content = content,
                        SourceType = "mcp",
                        Metadata = request.Metadata?.ToDictionary(
                            kvp => kvp.Key,
                            kvp => (object)kvp.Value)
                    },
                    cancellationToken);

                return Ok(new MemorizeResponse
                {
                    Success = true,
                    DocumentId = response.DocumentId.ToString(),
                    Title = title,
                    ResourceUri = $"flux://docs/{response.DocumentId}",
                    Message = response.Message
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP Memorize failed");
            return StatusCode(500, new McpErrorResponse
            {
                Error = "Memorize failed",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Search the knowledge base.
    /// </summary>
    [HttpPost("tools/search")]
    public async Task<IActionResult> Search(
        [FromBody] McpSearchRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("MCP Search: {Query}", request.Query);

        try
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return BadRequest(new McpErrorResponse
                {
                    Error = "Invalid request",
                    Message = "Query is required"
                });
            }

            var maxResults = request.MaxResults > 0 ? request.MaxResults : 10;
            var minScore = request.MinScore > 0 ? request.MinScore : 0.2f;

            // Use FluxIndex SDK if available for better search
            if (_fluxIndex != null)
            {
                IEnumerable<FluxIndex.SDK.SearchResult> results;

                if (request.SearchType?.ToLowerInvariant() == "hybrid" && !string.IsNullOrEmpty(request.Keyword))
                {
                    results = await _fluxIndex.HybridSearchAsync(
                        request.Keyword,
                        request.Query,
                        maxResults,
                        request.VectorWeight > 0 ? request.VectorWeight : 0.7f,
                        request.Filter,
                        cancellationToken);
                }
                else if (request.SearchType?.ToLowerInvariant() == "quantized" && _fluxIndex.SupportsQuantization)
                {
                    results = await _fluxIndex.SearchQuantizedAsync(
                        request.Query,
                        maxResults,
                        minScore,
                        cancellationToken);
                }
                else
                {
                    results = await _fluxIndex.SearchAsync(
                        request.Query,
                        maxResults,
                        minScore,
                        request.Filter,
                        cancellationToken);
                }

                var searchResults = results.Select(r => new McpSearchResultItem
                {
                    DocumentId = r.DocumentId,
                    ChunkId = r.Id,
                    Content = r.Content,
                    Score = r.Score,
                    ChunkIndex = r.ChunkIndex,
                    Metadata = r.Metadata,
                    ResourceUri = $"flux://docs/{r.DocumentId}/chunks/{r.Id}"
                }).ToList();

                return Ok(new McpSearchResponse
                {
                    Success = true,
                    Query = request.Query,
                    ResultCount = searchResults.Count,
                    Results = searchResults
                });
            }
            else
            {
                // Fallback to search service
                var searchResult = await _searchService.SearchAsync(
                    new Shared.DTOs.Search.SearchRequest
                    {
                        Query = request.Query,
                        TopK = maxResults
                    },
                    null,
                    cancellationToken);

                var searchResults = searchResult.Results.Select(r => new McpSearchResultItem
                {
                    DocumentId = r.DocumentId.ToString(),
                    ChunkId = r.ChunkId.ToString(),
                    Content = r.Content,
                    Score = (float)r.Score,
                    Metadata = r.Metadata
                }).ToList();

                return Ok(new McpSearchResponse
                {
                    Success = true,
                    Query = request.Query,
                    ResultCount = searchResults.Count,
                    Results = searchResults
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP Search failed");
            return StatusCode(500, new McpErrorResponse
            {
                Error = "Search failed",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Remove content from the knowledge base.
    /// </summary>
    [HttpPost("tools/unmemorize")]
    [HttpDelete("tools/unmemorize/{documentId}")]
    public async Task<IActionResult> Unmemorize(
        [FromRoute] string? documentId = null,
        [FromBody] UnmemorizeRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var docId = documentId ?? request?.DocumentId;

        if (string.IsNullOrWhiteSpace(docId))
        {
            return BadRequest(new McpErrorResponse
            {
                Error = "Invalid request",
                Message = "Document ID is required"
            });
        }

        _logger.LogInformation("MCP Unmemorize: {DocumentId}", docId);

        try
        {
            bool deleted = false;

            // Use FluxIndex SDK if available
            if (_fluxIndex != null)
            {
                deleted = await _fluxIndex.DeleteDocumentAsync(docId, cancellationToken);
            }
            else if (Guid.TryParse(docId, out var guidId))
            {
                await _documentService.DeleteAsync(guidId, cancellationToken);
                deleted = true;
            }

            if (deleted)
            {
                return Ok(new
                {
                    success = true,
                    documentId = docId,
                    message = $"Successfully removed document '{docId}' from knowledge base"
                });
            }
            else
            {
                return NotFound(new McpErrorResponse
                {
                    Error = "Document not found",
                    Message = $"Document '{docId}' was not found in the knowledge base"
                });
            }
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new McpErrorResponse
            {
                Error = "Document not found",
                Message = $"Document '{docId}' was not found in the knowledge base"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP Unmemorize failed");
            return StatusCode(500, new McpErrorResponse
            {
                Error = "Unmemorize failed",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Get system status and statistics.
    /// </summary>
    [HttpGet("tools/status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        try
        {
            if (_fluxIndex != null)
            {
                var stats = await _fluxIndex.GetStatisticsAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    status = "healthy",
                    statistics = new
                    {
                        totalDocuments = stats.TotalDocuments,
                        totalChunks = stats.TotalChunks,
                        averageChunksPerDocument = stats.AverageChunksPerDocument,
                        vectorStoreProvider = stats.VectorStoreProvider,
                        embeddingModel = stats.EmbeddingModel,
                        cacheEnabled = stats.CacheEnabled,
                        semanticCacheEnabled = stats.SemanticCacheEnabled,
                        defaultChunkSize = stats.DefaultChunkSize,
                        defaultChunkOverlap = stats.DefaultChunkOverlap
                    },
                    capabilities = new
                    {
                        supportsQuantization = _fluxIndex.SupportsQuantization,
                        supportsHybridSearch = true,
                        supportsSemanticCache = stats.SemanticCacheEnabled
                    }
                });
            }
            else
            {
                return Ok(new
                {
                    success = true,
                    status = "healthy",
                    message = "FluxIndex SDK not configured. Using basic document service.",
                    statistics = new
                    {
                        vectorStoreProvider = "PostgreSQL",
                        cacheEnabled = true
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP Status check failed");
            return StatusCode(500, new McpErrorResponse
            {
                Error = "Status check failed",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Get chunks for a specific document.
    /// </summary>
    [HttpGet("tools/chunks/{documentId}")]
    public async Task<IActionResult> GetChunks(
        string documentId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("MCP GetChunks: {DocumentId}", documentId);

        try
        {
            if (_fluxIndex != null)
            {
                var document = await _fluxIndex.GetDocumentAsync(documentId, cancellationToken);

                if (document == null)
                {
                    return NotFound(new McpErrorResponse
                    {
                        Error = "Document not found",
                        Message = $"Document '{documentId}' was not found"
                    });
                }

                var chunks = document.Chunks.Select(c => new
                {
                    chunkId = c.Id,
                    content = c.Content,
                    chunkIndex = c.ChunkIndex,
                    tokenCount = c.TokenCount,
                    metadata = c.Metadata
                }).ToList();

                return Ok(new
                {
                    success = true,
                    documentId,
                    title = document.FileName,
                    chunkCount = chunks.Count,
                    chunks
                });
            }
            else if (Guid.TryParse(documentId, out var guidId))
            {
                var detail = await _documentService.GetDetailByIdAsync(guidId, cancellationToken);

                if (detail == null)
                {
                    return NotFound(new McpErrorResponse
                    {
                        Error = "Document not found",
                        Message = $"Document '{documentId}' was not found"
                    });
                }

                return Ok(new
                {
                    success = true,
                    documentId,
                    title = detail.Title,
                    chunkCount = detail.ChunkCount,
                    chunks = detail.Chunks
                });
            }
            else
            {
                return BadRequest(new McpErrorResponse
                {
                    Error = "Invalid document ID",
                    Message = "Document ID must be a valid GUID"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP GetChunks failed");
            return StatusCode(500, new McpErrorResponse
            {
                Error = "GetChunks failed",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Re-index a document (re-memorize with updated content processing).
    /// </summary>
    [HttpPost("tools/reindex/{documentId}")]
    public async Task<IActionResult> Reindex(
        string documentId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("MCP Reindex: {DocumentId}", documentId);

        try
        {
            if (Guid.TryParse(documentId, out var guidId))
            {
                await _documentService.ReindexAsync(guidId, cancellationToken);

                return Ok(new
                {
                    success = true,
                    documentId,
                    message = "Document queued for re-indexing"
                });
            }
            else
            {
                return BadRequest(new McpErrorResponse
                {
                    Error = "Invalid document ID",
                    Message = "Document ID must be a valid GUID"
                });
            }
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new McpErrorResponse
            {
                Error = "Document not found",
                Message = $"Document '{documentId}' was not found"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP Reindex failed");
            return StatusCode(500, new McpErrorResponse
            {
                Error = "Reindex failed",
                Message = ex.Message
            });
        }
    }
}

#region Request/Response DTOs

/// <summary>
/// Request for memorizing content.
/// </summary>
public class MemorizeRequest
{
    /// <summary>Document title</summary>
    public string? Title { get; set; }

    /// <summary>Content to memorize (text)</summary>
    public string? Content { get; set; }

    /// <summary>Path to file or URL to memorize</summary>
    public string? SourcePath { get; set; }

    /// <summary>Collection ID (optional)</summary>
    public Guid? CollectionId { get; set; }

    /// <summary>Additional metadata</summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Response for memorize operation.
/// </summary>
public class MemorizeResponse
{
    public bool Success { get; set; }
    public string DocumentId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ResourceUri { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Request for search operation.
/// </summary>
public class McpSearchRequest
{
    /// <summary>Search query</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Keyword for hybrid search (optional)</summary>
    public string? Keyword { get; set; }

    /// <summary>Search type: "vector", "hybrid", "quantized"</summary>
    public string? SearchType { get; set; }

    /// <summary>Maximum results to return</summary>
    public int MaxResults { get; set; } = 10;

    /// <summary>Minimum similarity score (0-1)</summary>
    public float MinScore { get; set; } = 0.2f;

    /// <summary>Vector weight for hybrid search (0-1)</summary>
    public float VectorWeight { get; set; } = 0.7f;

    /// <summary>Collection ID filter (optional)</summary>
    public Guid? CollectionId { get; set; }

    /// <summary>Metadata filter (optional)</summary>
    public Dictionary<string, object>? Filter { get; set; }
}

/// <summary>
/// Response for search operation.
/// </summary>
public class McpSearchResponse
{
    public bool Success { get; set; }
    public string Query { get; set; } = string.Empty;
    public int ResultCount { get; set; }
    public List<McpSearchResultItem> Results { get; set; } = new();
}

/// <summary>
/// Single search result item.
/// </summary>
public class McpSearchResultItem
{
    public string DocumentId { get; set; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public float Score { get; set; }
    public int ChunkIndex { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public string? ResourceUri { get; set; }
}

/// <summary>
/// Request for unmemorize operation.
/// </summary>
public class UnmemorizeRequest
{
    public string? DocumentId { get; set; }
}

/// <summary>
/// Standard error response.
/// </summary>
public class McpErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

#endregion
