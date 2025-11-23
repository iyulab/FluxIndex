using System.CommandLine;
using FluxIndex.MCP.Workspace;
using Spectre.Console;

namespace FluxIndex.CLI.Commands;

public static class StatusCommand
{
    public static Command Create()
    {
        var command = new Command("status", "Show workspace status and statistics");

        command.SetHandler(async () =>
        {
            try
            {
                var workspace = FluxIndexWorkspace.Open();
                var context = workspace.GetContext();
                var stats = await context.GetStatisticsAsync();

                AnsiConsole.Write(new Rule("[cyan]FluxIndex Workspace[/]").RuleStyle("dim"));

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("Property")
                    .AddColumn("Value");

                table.AddRow("Root", workspace.WorkspaceRoot);
                table.AddRow("Database", workspace.DatabasePath);
                table.AddRow("", "");
                table.AddRow("[cyan]Embedding[/]", "");
                table.AddRow("  Provider", workspace.Config.Embedding.Provider);
                table.AddRow("  Model", workspace.Config.Embedding.Model);
                table.AddRow("", "");
                table.AddRow("[cyan]Statistics[/]", "");
                table.AddRow("  Documents", stats.TotalDocuments.ToString());
                table.AddRow("  Chunks", stats.TotalChunks.ToString());
                table.AddRow("  Cache Enabled", stats.CacheEnabled.ToString());

                AnsiConsole.Write(table);
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
        });

        return command;
    }
}
