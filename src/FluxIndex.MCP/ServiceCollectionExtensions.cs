using FluxIndex.MCP.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FluxIndex.MCP;

/// <summary>
/// Extension methods for configuring FluxIndex MCP services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add FluxIndex MCP server services
    /// </summary>
    public static IServiceCollection AddFluxIndexMcp(
        this IServiceCollection services,
        string? workspacePath = null)
    {
        // Register workspace
        services.AddSingleton(sp =>
        {
            var logger = sp.GetService<ILogger<FluxIndexWorkspace>>();
            return FluxIndexWorkspace.Open(workspacePath, logger);
        });

        // Register MCP tools
        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(ServiceCollectionExtensions).Assembly);

        return services;
    }

    /// <summary>
    /// Add FluxIndex MCP server with custom workspace configuration
    /// </summary>
    public static IServiceCollection AddFluxIndexMcp(
        this IServiceCollection services,
        Func<IServiceProvider, FluxIndexWorkspace> workspaceFactory)
    {
        services.AddSingleton(workspaceFactory);

        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(ServiceCollectionExtensions).Assembly);

        return services;
    }
}
