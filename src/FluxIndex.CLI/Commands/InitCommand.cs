using System.CommandLine;
using FluxIndex.MCP.Workspace;
using Spectre.Console;

namespace FluxIndex.CLI.Commands;

public static class InitCommand
{
    public static Command Create()
    {
        var providerOption = new Option<string>(
            "--provider",
            getDefaultValue: () => "openai",
            description: "Embedding provider (openai, anthropic, local)");

        var modelOption = new Option<string>(
            "--model",
            getDefaultValue: () => "text-embedding-3-small",
            description: "Embedding model name");

        var pathArgument = new Argument<string?>(
            "path",
            getDefaultValue: () => null,
            description: "Path to initialize workspace (default: current directory)");

        var command = new Command("init", "Initialize a new FluxIndex workspace")
        {
            pathArgument,
            providerOption,
            modelOption
        };

        command.SetHandler(async (path, provider, model) =>
        {
            try
            {
                path ??= Directory.GetCurrentDirectory();

                // Check if workspace already exists
                var existingRoot = WorkspaceLocator.FindWorkspaceRoot(path);
                if (existingRoot != null)
                {
                    AnsiConsole.MarkupLine($"[yellow]Workspace already exists at {existingRoot}[/]");
                    return;
                }

                // Create configuration
                var config = new WorkspaceConfig
                {
                    Embedding = new EmbeddingConfig
                    {
                        Provider = provider,
                        Model = model
                    }
                };

                // Initialize workspace
                var workspace = FluxIndexWorkspace.Initialize(path, config);

                AnsiConsole.MarkupLine($"[green]✓[/] Initialized FluxIndex workspace at [blue]{workspace.WorkspaceDirectory}[/]");
                AnsiConsole.MarkupLine($"  Embedding: [cyan]{provider}[/] / [cyan]{model}[/]");
                AnsiConsole.MarkupLine($"  Database: [dim]{workspace.DatabasePath}[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Next steps:[/]");
                AnsiConsole.MarkupLine("  • Set OPENAI_API_KEY environment variable");
                AnsiConsole.MarkupLine("  • Run [cyan]fluxindex memorize <file>[/] to add documents");
                AnsiConsole.MarkupLine("  • Run [cyan]fluxindex serve[/] to start MCP server");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, pathArgument, providerOption, modelOption);

        return command;
    }
}
