using System.CommandLine;
using FluxIndex.CLI.Commands;
using Spectre.Console;

namespace FluxIndex.CLI;

/// <summary>
/// FluxIndex CLI - Document processing and RAG infrastructure tool
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("FluxIndex - Document processing and RAG infrastructure tool");

        // Add subcommands
        rootCommand.Add(SetCommand.Create());
        rootCommand.Add(ProcessCommand.Create());

        // Default behavior: if first arg is a file path, treat as process command
        if (args.Length > 0 && !args[0].StartsWith('-') && args[0] != "set" && args[0] != "process")
        {
            // Check if it looks like a file path
            var possiblePath = args[0];
            if (File.Exists(possiblePath) || possiblePath.Contains('.') || possiblePath.Contains(Path.DirectorySeparatorChar))
            {
                // Prepend "process" to args
                args = args.Prepend("process").ToArray();
            }
        }

        // Handle no arguments - show help with branding
        if (args.Length == 0)
        {
            ShowBanner();
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Usage:[/]");
            AnsiConsole.MarkupLine("  fluxindex [yellow]<file>[/]                    Process a document");
            AnsiConsole.MarkupLine("  fluxindex [yellow]set[/] [cyan]<key>[/] [cyan]<value>[/]       Set configuration");
            AnsiConsole.MarkupLine("  fluxindex [yellow]set[/]                        Show all settings");
            AnsiConsole.MarkupLine("  fluxindex [yellow]process[/] [cyan]<file>[/] [[options]]  Process with options");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Examples:[/]");
            AnsiConsole.MarkupLine("  [dim]# Process a document — no API key or server needed; embeds locally (model downloaded on first use)[/]");
            AnsiConsole.MarkupLine("  fluxindex ./document.pdf");
            AnsiConsole.MarkupLine("  fluxindex ./document.pdf -o ./output -r -e");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [dim]# Extract and chunk only (skips the local embedding model entirely)[/]");
            AnsiConsole.MarkupLine("  fluxindex ./document.pdf --no-embeddings");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [dim]# Optional: use a remote OpenAI-compatible server (GPUStack) instead of the local model[/]");
            AnsiConsole.MarkupLine("  fluxindex set PROVIDER gpustack");
            AnsiConsole.MarkupLine("  fluxindex set GPUSTACK_ENDPOINT http://localhost:80");
            AnsiConsole.MarkupLine("  fluxindex set GPUSTACK_API_KEY sk-xxx");
            AnsiConsole.MarkupLine("  fluxindex set GPUSTACK_MODEL_NAME qwen2.5-7b");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Options:[/]");
            AnsiConsole.MarkupLine("  [yellow]-o, --output[/] [cyan]<dir>[/]     Output directory");
            AnsiConsole.MarkupLine("  [yellow]-l, --language[/] [cyan]<lang>[/]  Language hint (ko, en, ja, etc.)");
            AnsiConsole.MarkupLine("  [yellow]-c, --chunk-size[/] [cyan]<n>[/]   Max chunk size in tokens");
            AnsiConsole.MarkupLine("  [yellow]-r, --refine[/]             Enable text refinement via LLM");
            AnsiConsole.MarkupLine("  [yellow]-e, --enrich[/]             Enable metadata enrichment via LLM");
            AnsiConsole.MarkupLine("  [yellow]-v, --verbose[/]            Show detailed progress");
            AnsiConsole.MarkupLine("  [yellow]--no-embeddings[/]          Skip embedding generation");
            AnsiConsole.WriteLine();
            return 0;
        }

        // Parse and invoke
        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync();
    }

    private static void ShowBanner()
    {
        AnsiConsole.Write(new FigletText("FluxIndex")
            .LeftJustified()
            .Color(Color.Blue));

        AnsiConsole.MarkupLine("[dim]Document Processing & RAG Infrastructure[/]");
        AnsiConsole.MarkupLine($"[dim]Version {GetVersion()}[/]");
    }

    /// <summary>
    /// The version the assembly was actually built with, so the banner cannot drift from the
    /// project file (it previously hard-coded a version two releases behind).
    /// </summary>
    private static string GetVersion()
    {
        var assembly = typeof(Program).Assembly;
        var informational = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            // Strip a SourceLink "+<commit>" suffix if present.
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }
        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }
}
