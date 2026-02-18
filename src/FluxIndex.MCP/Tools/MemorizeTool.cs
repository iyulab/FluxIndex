using System.ComponentModel;
using System.Text.Json;
using FluxIndex.MCP.Workspace;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FluxIndex.MCP.Tools;

/// <summary>
/// MCP Tool for indexing content into the FluxIndex knowledge base
/// </summary>
[McpServerToolType]
public partial class MemorizeTool
{
    private static readonly JsonSerializerOptions s_indentedJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly FluxIndexWorkspace _workspace;
    private readonly ILogger<MemorizeTool> _logger;

    public MemorizeTool(FluxIndexWorkspace workspace, ILogger<MemorizeTool> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    [McpServerTool(Name = "memorize")]
    [Description("Index a file or URL into the knowledge base. Supports PDF, DOCX, TXT, MD, and other text formats.")]
    public async Task<string> MemorizeAsync(
        [Description("Path to the file or URL to memorize")]
        string path,

        [Description("Optional metadata to attach to the document")]
        Dictionary<string, string>? metadata = null,

        CancellationToken cancellationToken = default)
    {
        LogMemorizing(_logger, path);

        try
        {
            // Validate path is within workspace (sandbox check)
            if (!path.StartsWith("http://", StringComparison.Ordinal) && !path.StartsWith("https://", StringComparison.Ordinal))
            {
                var fullPath = Path.GetFullPath(path, _workspace.WorkspaceRoot);
                if (!_workspace.IsPathAllowed(fullPath))
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = "Access denied",
                        message = $"Path '{path}' is outside the workspace boundary"
                    });
                }

                if (!File.Exists(fullPath))
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = "File not found",
                        message = $"File '{path}' does not exist"
                    });
                }

                path = fullPath;
            }

            var context = _workspace.GetContext();

            // Read file content
            var content = await File.ReadAllTextAsync(path, cancellationToken);

            // Convert metadata to Dictionary<string, object>
            var metadataDict = metadata?.ToDictionary(
                kvp => kvp.Key,
                kvp => (object)kvp.Value);

            // Index the document
            var documentId = await context.Indexer.IndexDocumentAsync(
                content,
                path,
                metadataDict,
                cancellationToken);

            LogMemorized(_logger, path, documentId);

            return JsonSerializer.Serialize(new
            {
                success = true,
                path,
                documentId,
                resourceUri = $"flux://docs/{documentId}",
                message = $"Successfully memorized '{Path.GetFileName(path)}'"
            }, s_indentedJsonOptions);
        }
        catch (Exception ex)
        {
            LogMemorizeFailed(_logger, ex, path);
            return JsonSerializer.Serialize(new
            {
                error = "Memorize failed",
                message = ex.Message
            });
        }
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Memorizing: {Path}")]
    private static partial void LogMemorizing(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Memorized {Path} with documentId: {DocumentId}")]
    private static partial void LogMemorized(ILogger logger, string path, string documentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to memorize: {Path}")]
    private static partial void LogMemorizeFailed(ILogger logger, Exception exception, string path);

    #endregion
}
