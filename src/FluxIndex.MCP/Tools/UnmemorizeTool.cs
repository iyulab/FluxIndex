using System.ComponentModel;
using System.Text.Json;
using FluxIndex.MCP.Workspace;
using FluxIndex.SDK;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FluxIndex.MCP.Tools;

/// <summary>
/// MCP Tool for removing content from the FluxIndex knowledge base
/// </summary>
[McpServerToolType]
public class UnmemorizeTool
{
    private readonly FluxIndexWorkspace _workspace;
    private readonly ILogger<UnmemorizeTool> _logger;

    public UnmemorizeTool(FluxIndexWorkspace workspace, ILogger<UnmemorizeTool> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    [McpServerTool(Name = "unmemorize")]
    [Description("Remove a file from the knowledge base. PERMANENTLY deletes the indexed data.")]
    public async Task<string> UnmemorizeAsync(
        [Description("Path or identifier of the file to remove")]
        string path,

        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Unmemorizing: {Path}", path);

        try
        {
            var context = _workspace.GetContext();

            // Delete by document identifier
            var deleted = await context.Indexer.DeleteByDocumentIdAsync(path, cancellationToken);

            if (deleted)
            {
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    path,
                    message = $"Successfully removed '{path}' from knowledge base"
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            else
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    path,
                    message = $"Document '{path}' not found in knowledge base"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unmemorize: {Path}", path);
            return JsonSerializer.Serialize(new
            {
                error = "Unmemorize failed",
                message = ex.Message
            });
        }
    }
}
