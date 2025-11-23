using System.ComponentModel;
using System.Text.Json;
using FluxIndex.MCP.Workspace;
using FluxIndex.SDK;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FluxIndex.MCP.Tools;

/// <summary>
/// MCP Tool for checking the status of the FluxIndex knowledge base
/// </summary>
[McpServerToolType]
public class StatusTool
{
    private readonly FluxIndexWorkspace _workspace;
    private readonly ILogger<StatusTool> _logger;

    public StatusTool(FluxIndexWorkspace workspace, ILogger<StatusTool> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    [McpServerTool(Name = "status")]
    [Description("Get the status of the knowledge base including document count and configuration")]
    public async Task<string> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting workspace status");

        try
        {
            var context = _workspace.GetContext();
            var stats = await context.GetStatisticsAsync(cancellationToken);

            return JsonSerializer.Serialize(new
            {
                workspace = new
                {
                    root = _workspace.WorkspaceRoot,
                    database = _workspace.DatabasePath,
                    config = new
                    {
                        embedding = new
                        {
                            provider = _workspace.Config.Embedding.Provider,
                            model = _workspace.Config.Embedding.Model
                        },
                        search = new
                        {
                            strategy = _workspace.Config.Search.Strategy,
                            topK = _workspace.Config.Search.TopK
                        }
                    }
                },
                statistics = new
                {
                    documentCount = stats.TotalDocuments,
                    chunkCount = stats.TotalChunks,
                    cacheEnabled = stats.CacheEnabled
                }
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get status");
            return JsonSerializer.Serialize(new
            {
                error = "Status check failed",
                message = ex.Message
            });
        }
    }
}
