using System.CommandLine;
using FileFlux;
using FluxIndex.CLI.AI;
using FluxIndex.CLI.Configuration;
using FluxIndex.SDK.Extensions;
using FluxIndex.SDK.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Globalization;

namespace FluxIndex.CLI.Commands;

/// <summary>
/// Default command to process a document file
/// </summary>
public static class ProcessCommand
{
    public static Command Create()
    {
        var fileArgument = new Argument<FileInfo>("file")
        {
            Description = "Path to the document file to process"
        };

        var outputOption = new Option<DirectoryInfo?>("--output", "-o")
        {
            Description = "Output directory (default: <filename>_output)"
        };

        var languageOption = new Option<string?>("--language", "-l")
        {
            Description = "Language hint for processing (auto-detect if not specified)"
        };

        var chunkSizeOption = new Option<int?>("--chunk-size", "-c")
        {
            Description = "Maximum chunk size in tokens (default: 1024)"
        };

        var noEmbeddingsOption = new Option<bool>("--no-embeddings")
        {
            Description = "Skip embedding generation"
        };

        var cleanOption = new Option<bool>("--clean", "-c")
        {
            Description = "Enable text cleaning/preprocessing before chunking"
        };

        var contextualEnrichOption = new Option<bool>("--contextual-enrich")
        {
            Description = "Enable contextual enrichment (Anthropic Contextual Retrieval) for 49-67% better retrieval"
        };

        var generateQaOption = new Option<bool>("--generate-qa")
        {
            Description = "Generate QA pairs for RAG evaluation"
        };

        var maxQaPairsOption = new Option<int>("--max-qa-pairs")
        {
            Description = "Maximum QA pairs per chunk (default: 3)"
        };

        var enrichOption = new Option<bool>("--enrich", "-e")
        {
            Description = "Enable metadata enrichment via LLM"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Show detailed progress information"
        };

        var command = new Command("process", "Process a document file")
        {
            fileArgument,
            outputOption,
            languageOption,
            chunkSizeOption,
            noEmbeddingsOption,
            cleanOption,
            contextualEnrichOption,
            generateQaOption,
            maxQaPairsOption,
            enrichOption,
            verboseOption
        };

        // Make this the root command handler as well
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var file = parseResult.GetValue(fileArgument)!;
            var output = parseResult.GetValue(outputOption);
            var language = parseResult.GetValue(languageOption);
            var chunkSize = parseResult.GetValue(chunkSizeOption);
            var noEmbeddings = parseResult.GetValue(noEmbeddingsOption);
            var clean = parseResult.GetValue(cleanOption);
            var contextualEnrich = parseResult.GetValue(contextualEnrichOption);
            var generateQa = parseResult.GetValue(generateQaOption);
            var maxQaPairs = parseResult.GetValue(maxQaPairsOption);
            var enrich = parseResult.GetValue(enrichOption);
            var verbose = parseResult.GetValue(verboseOption);

            await ExecuteAsync(file, output, language, chunkSize, noEmbeddings, clean, contextualEnrich, generateQa, maxQaPairs, enrich, verbose);
        });

        return command;
    }

    public static async Task ExecuteAsync(
        FileInfo file,
        DirectoryInfo? output,
        string? language,
        int? chunkSize,
        bool noEmbeddings,
        bool clean,
        bool contextualEnrich,
        bool generateQa,
        int maxQaPairs,
        bool enrich,
        bool verbose)
    {
        if (!file.Exists)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] File not found: [yellow]{file.FullName}[/]");
            return;
        }

        var settings = CliSettings.Load();

        // Determine output directory
        var outputDir = output?.FullName ?? Path.Combine(
            file.DirectoryName ?? ".",
            file.Name + "_output");

        AnsiConsole.MarkupLine($"[bold blue]FluxIndex Document Processor[/]");
        AnsiConsole.MarkupLine($"[dim]Processing:[/] [cyan]{file.FullName}[/]");
        AnsiConsole.MarkupLine($"[dim]Output:[/] [cyan]{outputDir}[/]");
        AnsiConsole.WriteLine();

        try
        {
            // Build services
            var services = BuildServices(settings, verbose);
            var pipeline = services.GetRequiredService<DocumentProcessingPipeline>();

            // Build options
            var options = new DocumentProcessingOptions
            {
                OutputDirectory = outputDir,
                Language = language ?? settings.DefaultLanguage,
                ChunkingStrategy = settings.ChunkingStrategy,
                MaxChunkSize = chunkSize ?? settings.MaxChunkSize,
                OverlapSize = settings.OverlapSize,
                GenerateEmbeddings = !noEmbeddings,
                EnableTextCleaning = clean || settings.EnableTextCleaning,
                EnableContextualEnrichment = contextualEnrich || settings.EnableContextualEnrichment,
                EnableQAGeneration = generateQa,
                MaxQAPairsPerChunk = maxQaPairs > 0 ? maxQaPairs : 3,
                EnableMetadataEnrichment = enrich || settings.EnableMetadataEnrichment,
                ExtractImages = true,
                SaveExtractedText = true,
                SaveCleanedText = true,
                SaveMetadata = true,
                SaveChunks = true,
                SaveQAPairs = true
            };

            // Progress display
            DocumentProcessingResult? result = null;

            await AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn()
                })
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[green]Processing document[/]", maxValue: 100);

                    options.OnProgress = progress =>
                    {
                        task.Value = progress.Percentage;
                        task.Description = $"[green]{progress.Message}[/]";
                    };

                    result = await pipeline.ProcessAsync(file.FullName, options);
                    task.Value = 100;
                });

            if (result == null)
            {
                AnsiConsole.MarkupLine("[red]✗[/] Processing returned null result");
                return;
            }

            // Display results
            AnsiConsole.WriteLine();

            if (result.Success)
            {
                DisplaySuccessResult(result, verbose);
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Processing failed: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Error: {ex.Message}");
            if (verbose)
            {
                AnsiConsole.WriteException(ex);
            }
        }
    }

    private static ServiceProvider BuildServices(CliSettings settings, bool verbose)
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Warning);
            if (verbose)
            {
                builder.AddConsole();
            }
        });

        // FileFlux document processor
        services.AddFileFlux();

        // Embedding service based on provider
        ConfigureEmbeddingService(services, settings);

        // Text completion service (for enrichment features)
        ConfigureTextCompletionService(services, settings);

        // Document processing pipeline with fallback to mock services
        // SDK provides the extension method that registers pipeline + mock services if not already registered
        services.AddDocumentProcessingPipelineWithFallback();

        return services.BuildServiceProvider();
    }

    private static void ConfigureEmbeddingService(IServiceCollection services, CliSettings settings)
    {
        // Use LMSupply embedder - external AI providers should implement IEmbeddingService in consuming apps
        services.AddLMSupplyEmbedding();
    }

    private static void ConfigureTextCompletionService(IServiceCollection services, CliSettings settings)
    {
        // LMSupply text completion is the default - enables all LLM features without API key
        // External providers (OpenAI, Azure, GPUStack) can override when configured
        services.AddLMSupplyTextCompletion();
    }

    private static void DisplaySuccessResult(DocumentProcessingResult result, bool verbose)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Metric[/]")
            .AddColumn("[bold]Value[/]");

        table.AddRow("[yellow]Document ID[/]", result.DocumentId);
        table.AddRow("[yellow]Characters[/]", result.Metadata.CharacterCount.ToString("N0", CultureInfo.InvariantCulture));
        table.AddRow("[yellow]Words[/]", result.Metadata.WordCount.ToString("N0", CultureInfo.InvariantCulture));
        table.AddRow("[yellow]Chunks[/]", result.Stats.TotalChunks.ToString(CultureInfo.InvariantCulture));
        table.AddRow("[yellow]Images[/]", result.Stats.TotalImages.ToString(CultureInfo.InvariantCulture));
        table.AddRow("[yellow]Duration[/]", $"{result.Stats.Duration.TotalSeconds:F2}s");

        if (!string.IsNullOrEmpty(result.Metadata.DetectedLanguage))
            table.AddRow("[yellow]Language[/]", result.Metadata.DetectedLanguage);

        if (!string.IsNullOrEmpty(result.Metadata.Title))
            table.AddRow("[yellow]Title[/]", result.Metadata.Title);

        AnsiConsole.Write(new Panel(table)
            .Header("[bold green]✓ Processing Complete[/]")
            .Border(BoxBorder.Rounded));

        // Show output files
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Output files:[/]");

        var tree = new Tree($"[cyan]{Path.GetFileName(result.SourcePath)}_output/[/]");

        if (File.Exists(Path.Combine(Path.GetDirectoryName(result.SourcePath) ?? ".", Path.GetFileName(result.SourcePath) + "_output", "extract.md")))
            tree.AddNode("[dim]extract.md[/]");

        if (result.CleanedText != null)
            tree.AddNode("[dim]cleaned.md[/]");

        tree.AddNode("[dim]metadata.json[/]");

        if (result.Images.Count != 0)
        {
            tree.AddNode($"[dim]images/ ({result.Images.Count} files)[/]");
        }

        if (result.Chunks.Count != 0)
        {
            tree.AddNode($"[dim]chunks/ ({result.Chunks.Count * 2} files)[/]");
        }

        if (result.QAPairs.Count != 0)
        {
            tree.AddNode($"[dim]qa_pairs.json ({result.QAPairs.Count} pairs)[/]");
        }

        AnsiConsole.Write(tree);

        if (verbose)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Timing breakdown:[/]");
            AnsiConsole.MarkupLine($"  Extraction: {result.Stats.ExtractionTime.TotalMilliseconds:F0}ms");
            if (result.Stats.CleaningTime.HasValue)
                AnsiConsole.MarkupLine($"  Cleaning: {result.Stats.CleaningTime.Value.TotalMilliseconds:F0}ms");
            AnsiConsole.MarkupLine($"  Chunking: {result.Stats.ChunkingTime.TotalMilliseconds:F0}ms");
            if (result.Stats.ContextualEnrichmentTime.HasValue)
                AnsiConsole.MarkupLine($"  Contextual Enrichment: {result.Stats.ContextualEnrichmentTime.Value.TotalMilliseconds:F0}ms");
            AnsiConsole.MarkupLine($"  Embedding: {result.Stats.EmbeddingTime.TotalMilliseconds:F0}ms");
            if (result.Stats.QAGenerationTime.HasValue)
                AnsiConsole.MarkupLine($"  QA Generation: {result.Stats.QAGenerationTime.Value.TotalMilliseconds:F0}ms");
        }
    }
}
