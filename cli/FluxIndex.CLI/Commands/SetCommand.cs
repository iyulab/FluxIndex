using System.CommandLine;
using FluxIndex.CLI.Configuration;
using Spectre.Console;

namespace FluxIndex.CLI.Commands;

/// <summary>
/// Command to set configuration values
/// </summary>
public static class SetCommand
{
    public static Command Create()
    {
        var keyArgument = new Argument<string?>("key")
        {
            Description = "Configuration key (e.g., PROVIDER, GPUSTACK_ENDPOINT)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var valueArgument = new Argument<string?>("value")
        {
            Description = "Configuration value to set",
            Arity = ArgumentArity.ZeroOrOne
        };

        var command = new Command("set", "Set configuration values")
        {
            keyArgument,
            valueArgument
        };

        command.SetAction(parseResult =>
        {
            var key = parseResult.GetValue(keyArgument);
            var value = parseResult.GetValue(valueArgument);

            var settings = CliSettings.Load();

            // No arguments: show all settings
            if (string.IsNullOrEmpty(key))
            {
                ShowAllSettings(settings);
                return;
            }

            // Only key: show specific setting
            if (string.IsNullOrEmpty(value))
            {
                ShowSetting(settings, key);
                return;
            }

            // Both key and value: set the setting
            if (settings.Set(key, value))
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Set [yellow]{key}[/] = [cyan]{MaskIfSensitive(key, value)}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Unknown configuration key: [yellow]{key}[/]");
                ShowAvailableKeys();
            }
        });

        return command;
    }

    private static void ShowAllSettings(CliSettings settings)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[yellow]Key[/]").LeftAligned())
            .AddColumn(new TableColumn("[cyan]Value[/]").LeftAligned());

        foreach (var kvp in settings.GetAll())
        {
            var value = kvp.Value ?? "[dim](not set)[/]";
            table.AddRow($"[yellow]{kvp.Key}[/]", $"[cyan]{value}[/]");
        }

        AnsiConsole.Write(new Panel(table)
            .Header("[bold blue]FluxIndex Configuration[/]")
            .Border(BoxBorder.Rounded));

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Settings are stored in ~/.vault/settings.json[/]");
    }

    private static void ShowSetting(CliSettings settings, string key)
    {
        var value = settings.Get(key);
        if (value != null)
        {
            AnsiConsole.MarkupLine($"[yellow]{key}[/] = [cyan]{value}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Unknown configuration key: [yellow]{key}[/]");
            ShowAvailableKeys();
        }
    }

    private static void ShowAvailableKeys()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Available configuration keys:[/]");

        var groups = new Dictionary<string, string[]>
        {
            ["Provider"] = new[] { "PROVIDER" },
            ["OpenAI"] = new[] { "OPENAI_API_KEY", "OPENAI_MODEL_NAME" },
            ["Azure OpenAI"] = new[] { "AZURE_ENDPOINT", "AZURE_API_KEY", "AZURE_DEPLOYMENT_NAME" },
            ["GPUStack"] = new[] { "GPUSTACK_ENDPOINT", "GPUSTACK_API_KEY", "GPUSTACK_MODEL_NAME", "GPUSTACK_EMBEDDING_MODEL_NAME" },
            ["Processing"] = new[] { "DEFAULT_LANGUAGE", "CHUNKING_STRATEGY", "MAX_CHUNK_SIZE", "OVERLAP_SIZE" },
            ["Features"] = new[] { "ENABLE_METADATA_ENRICHMENT", "ENABLE_TEXT_REFINEMENT" }
        };

        foreach (var group in groups)
        {
            AnsiConsole.MarkupLine($"  [bold]{group.Key}:[/]");
            foreach (var key in group.Value)
            {
                AnsiConsole.MarkupLine($"    [yellow]{key}[/]");
            }
        }
    }

    private static string MaskIfSensitive(string key, string value)
    {
        var sensitiveKeys = new[] { "API_KEY", "SECRET" };
        if (sensitiveKeys.Any(k => key.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            if (value.Length <= 8) return "****";
            return value[..4] + "****" + value[^4..];
        }
        return value;
    }
}
