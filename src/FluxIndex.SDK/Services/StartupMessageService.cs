using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.SDK.Services;

/// <summary>
/// Displays startup guidance for FluxIndex AI service configuration.
/// Shows which AI services are active and suggests LMSupply options for enhanced RAG capabilities.
/// </summary>
public static class StartupMessageService
{
    private static bool _messageDisplayed = false;

    /// <summary>
    /// Display AI service configuration status and LMSupply guidance.
    /// Only displays once per application lifecycle.
    /// </summary>
    /// <param name="serviceProvider">The built service provider to check registrations</param>
    /// <param name="embeddingProvider">The configured embedding provider name</param>
    public static void DisplayAIServiceGuidance(IServiceProvider serviceProvider, string? embeddingProvider)
    {
        if (_messageDisplayed) return;
        _messageDisplayed = true;

        var hasEmbedding = serviceProvider.GetService<IEmbeddingService>() != null;
        var hasTextCompletion = serviceProvider.GetService<ITextCompletionService>() != null;
        var hasReranker = serviceProvider.GetService<IReranker>() != null;
        var hasContextualEnrichment = serviceProvider.GetService<IContextualEnrichmentService>() != null;

        var isLMSupplyEmbedding = embeddingProvider?.ToLower() is "LMSupply" or "localembedder" or null;
        var isInMemoryEmbedding = embeddingProvider?.ToLower() == "inmemory";

        // Count active AI services
        var activeCount = (hasEmbedding ? 1 : 0) + (hasTextCompletion ? 1 : 0) + (hasReranker ? 1 : 0);
        var missingServices = new List<string>();

        if (!hasTextCompletion) missingServices.Add("Text Completion");
        if (!hasReranker) missingServices.Add("Reranking");
        if (!hasContextualEnrichment) missingServices.Add("Contextual Enrichment");

        // If all services are configured, show minimal success message
        if (missingServices.Count == 0)
        {
            WriteColored(ConsoleColor.DarkGray, "FluxIndex: All AI services configured.");
            Console.WriteLine();
            return;
        }

        // Show guidance for missing services
        Console.WriteLine();
        WriteBoxTop();

        // Title
        WriteBoxLine("FluxIndex - AI Service Status", ConsoleColor.Cyan);
        WriteBoxSeparator();

        // Current status
        WriteServiceStatus("Embedding", hasEmbedding, GetEmbeddingDescription(embeddingProvider));
        WriteServiceStatus("Text Completion", hasTextCompletion, hasTextCompletion ? "Active" : "Not configured");
        WriteServiceStatus("Reranking", hasReranker, hasReranker ? "Active" : "Not configured");
        WriteServiceStatus("Contextual Enrichment", hasContextualEnrichment, hasContextualEnrichment ? "Active" : "Not configured");

        // Show LMSupply suggestion if services are missing
        if (missingServices.Count > 0 && !isInMemoryEmbedding)
        {
            WriteBoxSeparator();
            WriteBoxLine("Enable additional AI features with LMSupply (no API key required):", ConsoleColor.Yellow);
            WriteBoxEmpty();

            // Show code example
            WriteBoxLine("  builder.ConfigureServices(services => {", ConsoleColor.Gray);

            if (!hasTextCompletion)
            {
                WriteBoxLine("      services.AddLMSupplyTextCompletion();  // HyDE, metadata enrichment", ConsoleColor.White);
            }
            if (!hasReranker)
            {
                WriteBoxLine("      services.AddLMSupplyReranker();        // Semantic reranking", ConsoleColor.White);
            }
            if (!hasContextualEnrichment && hasTextCompletion)
            {
                WriteBoxLine("      // Contextual Enrichment available via WithContextualEmbedding()", ConsoleColor.DarkGray);
            }

            WriteBoxLine("  });", ConsoleColor.Gray);
        }

        // Show benefit hints
        if (!hasTextCompletion || !hasReranker)
        {
            WriteBoxSeparator();
            WriteBoxLine("Benefits:", ConsoleColor.DarkCyan);

            if (!hasTextCompletion)
            {
                WriteBoxLine("  - Text Completion: Enables HyDE query expansion (+20-30% recall)", ConsoleColor.DarkGray);
                WriteBoxLine("                     Metadata enrichment, contextual headers", ConsoleColor.DarkGray);
            }
            if (!hasReranker)
            {
                WriteBoxLine("  - Reranking: Cross-encoder semantic scoring (+15-25% precision)", ConsoleColor.DarkGray);
            }
        }

        WriteBoxBottom();
        Console.WriteLine();
    }

    /// <summary>
    /// Suppress the startup message (for testing or production environments).
    /// </summary>
    public static void Suppress()
    {
        _messageDisplayed = true;
    }

    /// <summary>
    /// Reset the message display state (for testing).
    /// </summary>
    public static void Reset()
    {
        _messageDisplayed = false;
    }

    private static string GetEmbeddingDescription(string? provider)
    {
        return provider?.ToLower() switch
        {
            "LMSupply" or "localembedder" => "LMSupply (ONNX)",
            "inmemory" => "InMemory (Test)",
            "custom" => "Custom Provider",
            null => "LMSupply (Default)",
            _ => provider
        };
    }

    private static void WriteServiceStatus(string name, bool isActive, string description)
    {
        var status = isActive ? "\u2713" : "\u25cb"; // ✓ or ○
        var statusColor = isActive ? ConsoleColor.Green : ConsoleColor.DarkGray;

        Console.Write("\u2502  ");
        WriteColored(statusColor, status);
        Console.Write($" {name,-22}");
        WriteColored(isActive ? ConsoleColor.White : ConsoleColor.DarkGray, description);
        PadToBoxEnd(name.Length + description.Length + 25);
        Console.WriteLine("\u2502");
    }

    private static void WriteBoxTop()
    {
        WriteColored(ConsoleColor.DarkGray, "\u256d" + new string('\u2500', 68) + "\u256e");
        Console.WriteLine();
    }

    private static void WriteBoxBottom()
    {
        WriteColored(ConsoleColor.DarkGray, "\u2570" + new string('\u2500', 68) + "\u256f");
        Console.WriteLine();
    }

    private static void WriteBoxSeparator()
    {
        WriteColored(ConsoleColor.DarkGray, "\u251c" + new string('\u2500', 68) + "\u2524");
        Console.WriteLine();
    }

    private static void WriteBoxLine(string text, ConsoleColor color)
    {
        Console.Write("\u2502  ");
        WriteColored(color, text);
        PadToBoxEnd(text.Length + 2);
        Console.WriteLine("\u2502");
    }

    private static void WriteBoxEmpty()
    {
        Console.Write("\u2502");
        Console.Write(new string(' ', 68));
        Console.WriteLine("\u2502");
    }

    private static void PadToBoxEnd(int currentLength)
    {
        var padding = 68 - currentLength;
        if (padding > 0)
        {
            Console.Write(new string(' ', padding));
        }
    }

    private static void WriteColored(ConsoleColor color, string text)
    {
        var originalColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            Console.Write(text);
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }
    }
}
