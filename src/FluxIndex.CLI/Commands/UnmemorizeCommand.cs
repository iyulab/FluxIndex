using System.CommandLine;
using FluxIndex.MCP.Workspace;
using Spectre.Console;

namespace FluxIndex.CLI.Commands;

public static class UnmemorizeCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "File path or document ID to remove"
        };

        var command = new Command("unmemorize", "Remove files from the knowledge base")
        {
            pathArgument
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var path = parseResult.GetValue(pathArgument)!;

            try
            {
                var workspace = FluxIndexWorkspace.Open();
                var context = workspace.GetContext();

                var deleted = await context.Indexer.DeleteByDocumentIdAsync(path, cancellationToken);

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
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            }
        });

        return command;
    }
}
