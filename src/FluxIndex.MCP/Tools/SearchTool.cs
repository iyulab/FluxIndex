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
public class SearchTool
{
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
        _logger.LogInformation("Searching for: {Query} with strategy: {Strategy}", query, strategy);

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
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query: {Query}", query);
            return JsonSerializer.Serialize(new
            {
                error = "Search failed",
                message = ex.Message
            });
        }
    }
}
