using System.CommandLine;
using FluxIndex.MCP.Workspace;
using Spectre.Console;

namespace FluxIndex.CLI.Commands;

public static class UnmemorizeCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string>(
            "path",
            description: "File path or document ID to remove");

        var command = new Command("unmemorize", "Remove files from the knowledge base")
        {
            pathArgument
        };

        command.SetHandler(async (path) =>
        {
            try
            {
                var workspace = FluxIndexWorkspace.Open();
                var context = workspace.GetContext();

                var deleted = await context.Indexer.DeleteByDocumentIdAsync(path);

                if (deleted)
                {
                    AnsiConsole.MarkupLine($"[green]✓[/] Removed '{path}' from knowledge base");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]Document '{path}' not found in knowledge base[/]");
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No FluxIndex workspace"))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] No FluxIndex workspace found. Run 'fluxindex init' first.");
                Environment.ExitCode = 1;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, pathArgument);

        return command;
    }
}
