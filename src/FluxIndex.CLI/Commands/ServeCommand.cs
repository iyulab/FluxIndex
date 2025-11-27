using System.CommandLine;
using FluxIndex.MCP;

namespace FluxIndex.CLI.Commands;

public static class ServeCommand
{
    public static Command Create()
    {
        var command = new Command("serve", "Start the MCP server (stdio transport)");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                // Note: When running as MCP server, all output goes to stderr
                // stdout is reserved for JSON-RPC protocol messages
                await FluxIndexMcpServer.RunAsync();
            }
            catch (Exception ex)
            {
                // Write to stderr since stdout is for MCP protocol
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
        });

        return command;
    }
}
