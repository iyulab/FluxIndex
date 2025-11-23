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
public class MemorizeTool
{
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
        _logger.LogInformation("Memorizing: {Path}", path);

        try
        {
            // Validate path is within workspace (sandbox check)
            if (!path.StartsWith("http://") && !path.StartsWith("https://"))
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

            _logger.LogInformation("Memorized {Path} with documentId: {DocumentId}", path, documentId);

            return JsonSerializer.Serialize(new
            {
                success = true,
                path,
                documentId,
                resourceUri = $"flux://docs/{documentId}",
                message = $"Successfully memorized '{Path.GetFileName(path)}'"
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to memorize: {Path}", path);
            return JsonSerializer.Serialize(new
            {
                error = "Memorize failed",
                message = ex.Message
            });
        }
    }
}
