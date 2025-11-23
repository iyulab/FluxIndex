using FluxIndex.MCP.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FluxIndex.MCP;

/// <summary>
/// FluxIndex MCP Server configuration and startup
/// </summary>
public static class FluxIndexMcpServer
{
    /// <summary>
    /// Run the FluxIndex MCP server with stdio transport
    /// </summary>
    public static async Task RunAsync(string? workspacePath = null, CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateApplicationBuilder();

        // CRITICAL: Redirect all logging to stderr to keep stdout clean for MCP protocol
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        // Configure FluxIndex workspace
        builder.Services.AddSingleton(sp =>
        {
            var logger = sp.GetService<ILogger<FluxIndexWorkspace>>();
            return FluxIndexWorkspace.Open(workspacePath, logger);
        });

        // Configure MCP Server with stdio transport
        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(FluxIndexMcpServer).Assembly);

        var app = builder.Build();
        await app.RunAsync(cancellationToken);
    }
}
