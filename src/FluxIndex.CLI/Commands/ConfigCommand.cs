using System.CommandLine;
using FluxIndex.MCP.Workspace;
using Spectre.Console;

namespace FluxIndex.CLI.Commands;

public static class ConfigCommand
{
    public static Command Create()
    {
        var command = new Command("config", "Manage workspace configuration");

        command.Add(CreateGetCommand());
        command.Add(CreateSetCommand());
        command.Add(CreateListCommand());

        return command;
    }

    private static Command CreateGetCommand()
    {
        var keyArg = new Argument<string>("key")
        {
            Description = "Configuration key (e.g., embedding.provider, search.top_k)"
        };
        var command = new Command("get", "Get a configuration value")
        {
            keyArg
        };

        command.SetAction((parseResult) =>
        {
            var key = parseResult.GetValue(keyArg)!;

            try
            {
                var workspace = FluxIndexWorkspace.Open();
                var value = GetConfigValue(workspace.Config, key);

                if (value != null)
                {
                    AnsiConsole.WriteLine(value);
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning:[/] Configuration key '{key}' not found");
                    return 1;
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No FluxIndex workspace"))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] No FluxIndex workspace found. Run 'fluxindex init' first.");
                return 1;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                return 1;
            }

            return 0;
        });

        return command;
    }

    private static Command CreateSetCommand()
    {
        var keyArg = new Argument<string>("key")
        {
            Description = "Configuration key (e.g., embedding.model, search.top_k)"
        };
        var valueArg = new Argument<string>("value")
        {
            Description = "Configuration value"
        };
        var command = new Command("set", "Set a configuration value")
        {
            keyArg,
            valueArg
        };

        command.SetAction((parseResult) =>
        {
            var key = parseResult.GetValue(keyArg)!;
            var value = parseResult.GetValue(valueArg)!;

            try
            {
                var workspace = FluxIndexWorkspace.Open();
                var success = SetConfigValue(workspace.Config, key, value);

                if (success)
                {
                    workspace.SaveConfig();
                    AnsiConsole.MarkupLine($"[green]✓[/] Set [cyan]{key}[/] = [yellow]{value}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] Unknown configuration key '{key}'");
                    AnsiConsole.MarkupLine("");
                    PrintAvailableKeys();
                    return 1;
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No FluxIndex workspace"))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] No FluxIndex workspace found. Run 'fluxindex init' first.");
                return 1;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                return 1;
            }

            return 0;
        });

        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List all configuration values");

        command.SetAction((parseResult) =>
        {
            try
            {
                var workspace = FluxIndexWorkspace.Open();
                var config = workspace.Config;

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("Key")
                    .AddColumn("Value");

                // Embedding settings
                table.AddRow("[cyan]embedding.provider[/]", config.Embedding.Provider);
                table.AddRow("[cyan]embedding.model[/]", config.Embedding.Model);
                table.AddRow("[cyan]embedding.dimensions[/]", config.Embedding.Dimensions?.ToString() ?? "[dim](auto)[/]");

                // Completion settings
                if (config.Completion != null)
                {
                    table.AddRow("[cyan]completion.provider[/]", config.Completion.Provider);
                    table.AddRow("[cyan]completion.model[/]", config.Completion.Model);
                }

                // Search settings
                table.AddRow("[cyan]search.strategy[/]", config.Search.Strategy);
                table.AddRow("[cyan]search.top_k[/]", config.Search.TopK.ToString());
                table.AddRow("[cyan]search.min_score[/]", config.Search.MinScore.ToString("F2"));

                AnsiConsole.Write(table);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No FluxIndex workspace"))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] No FluxIndex workspace found. Run 'fluxindex init' first.");
                return 1;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                return 1;
            }

            return 0;
        });

        return command;
    }

    private static string? GetConfigValue(WorkspaceConfig config, string key)
    {
        return key.ToLowerInvariant() switch
        {
            "embedding.provider" => config.Embedding.Provider,
            "embedding.model" => config.Embedding.Model,
            "embedding.dimensions" => config.Embedding.Dimensions?.ToString(),
            "completion.provider" => config.Completion?.Provider,
            "completion.model" => config.Completion?.Model,
            "search.strategy" => config.Search.Strategy,
            "search.top_k" => config.Search.TopK.ToString(),
            "search.min_score" => config.Search.MinScore.ToString(),
            "version" => config.Version,
            "created_at" => config.CreatedAt.ToString("O"),
            "updated_at" => config.UpdatedAt.ToString("O"),
            _ => null
        };
    }

    private static bool SetConfigValue(WorkspaceConfig config, string key, string value)
    {
        switch (key.ToLowerInvariant())
        {
            case "embedding.provider":
                config.Embedding.Provider = value;
                return true;
            case "embedding.model":
                config.Embedding.Model = value;
                return true;
            case "embedding.dimensions":
                if (int.TryParse(value, out var dims))
                {
                    config.Embedding.Dimensions = dims;
                    return true;
                }
                throw new ArgumentException($"Invalid dimensions value: {value}");
            case "completion.provider":
                config.Completion ??= new CompletionConfig();
                config.Completion.Provider = value;
                return true;
            case "completion.model":
                config.Completion ??= new CompletionConfig();
                config.Completion.Model = value;
                return true;
            case "search.strategy":
                config.Search.Strategy = value;
                return true;
            case "search.top_k":
                if (int.TryParse(value, out var topK))
                {
                    config.Search.TopK = topK;
                    return true;
                }
                throw new ArgumentException($"Invalid top_k value: {value}");
            case "search.min_score":
                if (float.TryParse(value, out var minScore))
                {
                    config.Search.MinScore = minScore;
                    return true;
                }
                throw new ArgumentException($"Invalid min_score value: {value}");
            default:
                return false;
        }
    }

    private static void PrintAvailableKeys()
    {
        AnsiConsole.MarkupLine("[dim]Available keys:[/]");
        AnsiConsole.MarkupLine("  embedding.provider, embedding.model, embedding.dimensions");
        AnsiConsole.MarkupLine("  completion.provider, completion.model");
        AnsiConsole.MarkupLine("  search.strategy, search.top_k, search.min_score");
    }
}
