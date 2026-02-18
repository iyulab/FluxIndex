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
public partial class UnmemorizeTool
{
    private static readonly JsonSerializerOptions s_indentedJsonOptions = new()
    {
        WriteIndented = true
    };

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
        LogUnmemorizing(_logger, path);

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
                }, s_indentedJsonOptions);
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
            LogUnmemorizeFailed(_logger, ex, path);
            return JsonSerializer.Serialize(new
            {
                error = "Unmemorize failed",
                message = ex.Message
            });
        }
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Unmemorizing: {Path}")]
    private static partial void LogUnmemorizing(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to unmemorize: {Path}")]
    private static partial void LogUnmemorizeFailed(ILogger logger, Exception exception, string path);

    #endregion
}
