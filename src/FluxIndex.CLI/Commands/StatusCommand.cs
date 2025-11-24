using System.CommandLine;
using FluxIndex.MCP.Workspace;
using Spectre.Console;

namespace FluxIndex.CLI.Commands;

public static class StatusCommand
{
    public static Command Create()
    {
        var command = new Command("status", "Show workspace status and statistics");

        var verboseOption = new Option<bool>(
            ["--verbose", "-v"],
            "Show detailed information including memorized files");

        command.AddOption(verboseOption);

        command.SetHandler(async (bool verbose) =>
        {
            try
            {
                var workspace = FluxIndexWorkspace.Open();
                var context = workspace.GetContext();
                var stats = await context.GetStatisticsAsync();

                // Workspace info
                AnsiConsole.Write(new Rule("[cyan]FluxIndex Workspace[/]").RuleStyle("dim"));
                AnsiConsole.MarkupLine($"  [dim]Root:[/] {workspace.WorkspaceRoot}");
                AnsiConsole.MarkupLine($"  [dim]Database:[/] {workspace.DatabasePath}");
                AnsiConsole.WriteLine();

                // Configuration
                AnsiConsole.Write(new Rule("[cyan]Configuration[/]").RuleStyle("dim"));
                var configTable = new Table()
                    .Border(TableBorder.Simple)
                    .HideHeaders()
                    .AddColumn("Key")
                    .AddColumn("Value");

                configTable.AddRow("[dim]embedding.provider[/]", $"[yellow]{workspace.Config.Embedding.Provider}[/]");
                configTable.AddRow("[dim]embedding.model[/]", $"[yellow]{workspace.Config.Embedding.Model}[/]");
                configTable.AddRow("[dim]search.strategy[/]", $"[yellow]{workspace.Config.Search.Strategy}[/]");
                configTable.AddRow("[dim]search.top_k[/]", $"[yellow]{workspace.Config.Search.TopK}[/]");
                configTable.AddRow("[dim]search.min_score[/]", $"[yellow]{workspace.Config.Search.MinScore:F2}[/]");

                AnsiConsole.Write(configTable);
                AnsiConsole.WriteLine();

                // Statistics
                AnsiConsole.Write(new Rule("[cyan]Statistics[/]").RuleStyle("dim"));
                var statsTable = new Table()
                    .Border(TableBorder.Simple)
                    .HideHeaders()
                    .AddColumn("Metric")
                    .AddColumn("Value");

                statsTable.AddRow("Documents", $"[green]{stats.TotalDocuments}[/]");
                statsTable.AddRow("Chunks", $"[green]{stats.TotalChunks}[/]");
                statsTable.AddRow("Cache", stats.CacheEnabled ? "[green]Enabled[/]" : "[dim]Disabled[/]");

                AnsiConsole.Write(statsTable);

                // Additional verbose info
                if (verbose)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.Write(new Rule("[cyan]Additional Info[/]").RuleStyle("dim"));

                    var infoTable = new Table()
                        .Border(TableBorder.Simple)
                        .HideHeaders()
                        .AddColumn("Key")
                        .AddColumn("Value");

                    infoTable.AddRow("Vector Store", $"[yellow]{stats.VectorStoreProvider}[/]");
                    infoTable.AddRow("Embedding Model", $"[yellow]{stats.EmbeddingModel}[/]");
                    infoTable.AddRow("Avg Chunks/Doc", $"[yellow]{stats.AverageChunksPerDocument:F1}[/]");
                    infoTable.AddRow("Chunk Size", $"[yellow]{stats.DefaultChunkSize}[/]");
                    infoTable.AddRow("Chunk Overlap", $"[yellow]{stats.DefaultChunkOverlap}[/]");

                    if (stats.SemanticCacheEnabled)
                    {
                        infoTable.AddRow("Cache Hit Rate", $"[yellow]{stats.CacheHitRate:P1}[/]");
                        infoTable.AddRow("Cached Items", $"[yellow]{stats.CachedItemsCount}[/]");
                    }

                    AnsiConsole.Write(infoTable);
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
        }, verboseOption);

        return command;
    }
}
