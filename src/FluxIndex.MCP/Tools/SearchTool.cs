using System.ComponentModel;
using System.Text.Json;
using FluxIndex.MCP.Workspace;
using FluxIndex.SDK;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FluxIndex.MCP.Tools;

/// <summary>
/// MCP Tool for searching the FluxIndex knowledge base
/// </summary>
[McpServerToolType]
public partial class SearchTool
{
    private static readonly JsonSerializerOptions s_indentedJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly FluxIndexWorkspace _workspace;
    private readonly ILogger<SearchTool> _logger;

    public SearchTool(FluxIndexWorkspace workspace, ILogger<SearchTool> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    [McpServerTool(Name = "search")]
    [Description("Search the local knowledge base using hybrid vector+keyword strategy. Use this tool to find information, code snippets, or documents relevant to the query.")]
    public async Task<string> SearchAsync(
        [Description("The search terms or semantic question")]
        string query,

        [Description("Maximum number of chunks to return (default 5)")]
        int maxResults = 5,

        [Description("Search strategy: 'hybrid' (recommended), 'vector' (semantic), or 'keyword' (exact match)")]
        string strategy = "hybrid",

        CancellationToken cancellationToken = default)
    {
        LogSearching(_logger, query, strategy);

        try
        {
            var context = _workspace.GetContext();
            var results = await context.Retriever.SearchAsync(query, maxResults, 0.2f, null, cancellationToken);

            if (results == null || !results.Any())
            {
                return JsonSerializer.Serialize(new
                {
                    message = $"No documents found matching query '{query}'. Try broadening your search terms or checking the available resource topics.",
                    results = Array.Empty<object>()
                });
            }

            var response = results.Select(r => new
            {
                id = r.DocumentChunk.Id,
                documentId = r.DocumentChunk.DocumentId,
                content = r.DocumentChunk.Content,
                score = r.Score,
                metadata = r.DocumentChunk.Metadata,
                resourceUri = $"flux://docs/{r.DocumentChunk.DocumentId}"
            });

            return JsonSerializer.Serialize(new
            {
                query,
                strategy,
                count = results.Count(),
                results = response
            }, s_indentedJsonOptions);
        }
        catch (Exception ex)
        {
            LogSearchFailed(_logger, ex, query);
            return JsonSerializer.Serialize(new
            {
                error = "Search failed",
                message = ex.Message
            });
        }
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Searching for: {Query} with strategy: {Strategy}")]
    private static partial void LogSearching(ILogger logger, string query, string strategy);

    [LoggerMessage(Level = LogLevel.Error, Message = "Search failed for query: {Query}")]
    private static partial void LogSearchFailed(ILogger logger, Exception exception, string query);

    #endregion
}
