using System.CommandLine;
using FluxIndex.MCP.Workspace;
using Spectre.Console;

namespace FluxIndex.CLI.Commands;

public static class MemorizeCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string>(
            "path",
            description: "File path or glob pattern to memorize");

        var recursiveOption = new Option<bool>(
            "--recursive",
            getDefaultValue: () => false,
            description: "Recursively process directories");

        var command = new Command("memorize", "Index files into the knowledge base")
        {
            pathArgument,
            recursiveOption
        };

        command.SetHandler(async (path, recursive) =>
        {
            try
            {
                var workspace = FluxIndexWorkspace.Open();
                var context = workspace.GetContext();

                // Expand glob patterns or get single file
                var files = GetFiles(path, workspace.WorkspaceRoot, recursive);

                if (!files.Any())
                {
                    AnsiConsole.MarkupLine($"[yellow]No files found matching '{path}'[/]");
                    return;
                }

                await AnsiConsole.Progress()
                    .AutoClear(false)
                    .Columns(
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new SpinnerColumn())
                    .StartAsync(async ctx =>
                    {
                        var task = ctx.AddTask($"[cyan]Memorizing {files.Count} file(s)[/]", maxValue: files.Count);

                        foreach (var file in files)
                        {
                            task.Description = $"[cyan]Processing {Path.GetFileName(file)}[/]";

                            try
                            {
                                var content = await File.ReadAllTextAsync(file);

                                var documentId = await context.Indexer.IndexDocumentAsync(
                                    content,
                                    file,
                                    null);

                                AnsiConsole.MarkupLine($"  [green]✓[/] {workspace.GetRelativePath(file)}");
                            }
                            catch (Exception ex)
                            {
                                AnsiConsole.MarkupLine($"  [red]✗[/] {workspace.GetRelativePath(file)} - {ex.Message}");
                            }

                            task.Increment(1);
                        }
                    });

                AnsiConsole.MarkupLine($"\n[green]Done![/] Memorized {files.Count} file(s)");
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
        }, pathArgument, recursiveOption);

        return command;
    }

    private static List<string> GetFiles(string pattern, string basePath, bool recursive)
    {
        var fullPath = Path.IsPathRooted(pattern)
            ? pattern
            : Path.Combine(basePath, pattern);

        if (File.Exists(fullPath))
        {
            return [fullPath];
        }

        if (Directory.Exists(fullPath))
        {
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.GetFiles(fullPath, "*.*", searchOption)
                .Where(f => IsSupported(f))
                .ToList();
        }

        // Glob pattern
        var directory = Path.GetDirectoryName(fullPath) ?? basePath;
        var searchPattern = Path.GetFileName(fullPath);
        var searchOption2 = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        if (Directory.Exists(directory))
        {
            return Directory.GetFiles(directory, searchPattern, searchOption2)
                .Where(f => IsSupported(f))
                .ToList();
        }

        return [];
    }

    private static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".txt" or ".md" or ".pdf" or ".docx" or ".doc" or ".html" or ".htm"
            or ".cs" or ".js" or ".ts" or ".py" or ".java" or ".json" or ".xml" or ".yaml" or ".yml";
    }
}
