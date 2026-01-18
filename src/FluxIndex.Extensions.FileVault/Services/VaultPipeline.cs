using System.Text.Json;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Enums;
using FluxIndex.Extensions.FileVault.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// Pipeline service for processing vault entries.
/// Integrates with FileFlux for extraction and chunking.
/// </summary>
public sealed class VaultPipeline : IVaultPipeline
{
    private readonly IGitService _git;
    private readonly IContentHasher _hasher;
    private readonly ILogger<VaultPipeline> _logger;

    // FileFlux integration will be injected when available
    private readonly IExtractor? _extractor;
    private readonly IChunker? _chunker;
    private readonly IMemorizer? _memorizer;

    public VaultPipeline(
        IGitService git,
        IContentHasher hasher,
        ILogger<VaultPipeline> logger,
        IExtractor? extractor = null,
        IChunker? chunker = null,
        IMemorizer? memorizer = null)
    {
        _git = git ?? throw new ArgumentNullException(nameof(git));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _extractor = extractor;
        _chunker = chunker;
        _memorizer = memorizer;
    }

    public async Task ExtractAsync(VaultEntry entry, CancellationToken ct = default)
    {
        _logger.LogInformation("Extracting content from {SourcePath}", entry.SourcePath);

        // Ensure Git is initialized
        await _git.InitAsync(entry.VaultPath, ct);

        // Save source info
        entry.SaveSourceInfo();

        string extractedContent;
        Dictionary<string, byte[]>? images = null;

        if (_extractor != null)
        {
            // Use FileFlux extractor
            var result = await _extractor.ExtractAsync(entry.SourcePath, ct);
            extractedContent = result.Content;
            images = result.Images;
        }
        else
        {
            // Fallback: simple text extraction
            extractedContent = await ExtractFallbackAsync(entry.SourcePath, ct);
        }

        // Write extracted content
        await File.WriteAllTextAsync(entry.ExtractedPath, extractedContent, ct);

        // Write images if any
        if (images?.Count > 0)
        {
            Directory.CreateDirectory(entry.ImagesPath);
            foreach (var (name, data) in images)
            {
                var imagePath = Path.Combine(entry.ImagesPath, name);
                await File.WriteAllBytesAsync(imagePath, data, ct);
            }
        }

        // Git commit
        await _git.CommitAsync(entry.VaultPath, "extract: from source", ct);

        entry.MarkExtracted();
        entry.SaveSourceInfo();

        _logger.LogInformation("Extracted content to {ExtractedPath}", entry.ExtractedPath);
    }

    public async Task RefineAsync(VaultEntry entry, CancellationToken ct = default)
    {
        if (entry.Stage < ProcessingStage.Extracted)
        {
            await ExtractAsync(entry, ct);
        }

        _logger.LogInformation("Refining content for {SourcePath}", entry.SourcePath);

        // Read extracted content
        var extractedContent = await File.ReadAllTextAsync(entry.ExtractedPath, ct);

        // For now, refine is a copy (can be enhanced with cleanup/normalization)
        var refinedContent = RefineContent(extractedContent);

        // Write refined content
        await File.WriteAllTextAsync(entry.RefinedPath, refinedContent, ct);

        // Git commit
        await _git.CommitAsync(entry.VaultPath, "refine: auto-processed", ct);

        entry.MarkRefined(isManualEdit: false);
        entry.SaveSourceInfo();

        _logger.LogInformation("Refined content to {RefinedPath}", entry.RefinedPath);
    }

    public async Task ChunkAsync(VaultEntry entry, ChunkingOptions? options = null, CancellationToken ct = default)
    {
        if (entry.Stage < ProcessingStage.Refined)
        {
            await RefineAsync(entry, ct);
        }

        options ??= new ChunkingOptions();

        _logger.LogInformation("Chunking content for {SourcePath}", entry.SourcePath);

        // Read refined content
        var refinedContent = await File.ReadAllTextAsync(entry.RefinedPath, ct);

        IReadOnlyList<string> chunks;

        if (_chunker != null)
        {
            // Use FileFlux chunker
            chunks = await _chunker.ChunkAsync(refinedContent, options, ct);
        }
        else
        {
            // Fallback: simple chunking
            chunks = ChunkFallback(refinedContent, options);
        }

        // Write chunks
        Directory.CreateDirectory(entry.ChunksPath);

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunkPath = Path.Combine(entry.ChunksPath, $"{i:D3}.md");
            await File.WriteAllTextAsync(chunkPath, chunks[i], ct);
        }

        // Write manifest
        var manifest = new ChunkManifest
        {
            ChunkCount = chunks.Count,
            CreatedAt = DateTimeOffset.UtcNow,
            Options = options,
            IsMemorized = false
        };

        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(entry.ManifestPath, manifestJson, ct);

        // Git commit
        await _git.CommitAsync(entry.VaultPath, $"chunks: {chunks.Count} chunks created", ct);

        entry.MarkChunked(chunks.Count);
        entry.SaveSourceInfo();

        _logger.LogInformation("Created {ChunkCount} chunks at {ChunksPath}", chunks.Count, entry.ChunksPath);
    }

    public async Task MemorizeAsync(VaultEntry entry, CancellationToken ct = default)
    {
        if (entry.Stage < ProcessingStage.Chunked)
        {
            await ChunkAsync(entry, null, ct);
        }

        _logger.LogInformation("Memorizing chunks for {SourcePath}", entry.SourcePath);

        // Read chunks
        var chunkFiles = Directory.GetFiles(entry.ChunksPath, "*.md")
            .OrderBy(f => f)
            .ToList();

        if (_memorizer != null)
        {
            // Use FluxIndex memorizer
            var chunks = new List<string>();
            foreach (var chunkFile in chunkFiles)
            {
                chunks.Add(await File.ReadAllTextAsync(chunkFile, ct));
            }

            await _memorizer.MemorizeAsync(entry, chunks, ct);
        }
        else
        {
            _logger.LogWarning("No memorizer configured, skipping indexing");
        }

        // Update manifest
        var manifestJson = await File.ReadAllTextAsync(entry.ManifestPath, ct);
        var manifest = JsonSerializer.Deserialize<ChunkManifest>(manifestJson) ?? new ChunkManifest();
        manifest.IsMemorized = true;
        manifest.MemorizedAt = DateTimeOffset.UtcNow;

        await File.WriteAllTextAsync(entry.ManifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), ct);

        // Git commit
        await _git.CommitAsync(entry.VaultPath, "memorize: indexed to FluxIndex", ct);

        entry.MarkMemorized();
        entry.SaveSourceInfo();

        _logger.LogInformation("Memorized {ChunkCount} chunks", chunkFiles.Count);
    }

    public async Task ProcessToStageAsync(VaultEntry entry, ProcessingStage targetStage, CancellationToken ct = default)
    {
        while (entry.Stage < targetStage)
        {
            ct.ThrowIfCancellationRequested();

            switch (entry.Stage)
            {
                case ProcessingStage.Source:
                    await ExtractAsync(entry, ct);
                    break;
                case ProcessingStage.Extracted:
                    await RefineAsync(entry, ct);
                    break;
                case ProcessingStage.Refined:
                    await ChunkAsync(entry, null, ct);
                    break;
                case ProcessingStage.Chunked:
                    await MemorizeAsync(entry, ct);
                    break;
            }
        }
    }

    public async Task ReprocessFromStageAsync(VaultEntry entry, ProcessingStage fromStage, CancellationToken ct = default)
    {
        entry.ResetToStage(fromStage);
        await ProcessToStageAsync(entry, ProcessingStage.Memorized, ct);
    }

    private static async Task<string> ExtractFallbackAsync(string sourcePath, CancellationToken ct)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();

        // Simple text extraction for supported formats
        if (extension is ".txt" or ".md" or ".json" or ".xml" or ".yaml" or ".yml" or ".csv")
        {
            return await File.ReadAllTextAsync(sourcePath, ct);
        }

        // For unsupported formats, return a placeholder
        return $"[Content extraction required for {extension} files]";
    }

    private static string RefineContent(string content)
    {
        // Basic refinement: normalize whitespace, remove excessive blank lines
        var lines = content.Split('\n')
            .Select(l => l.TrimEnd())
            .ToList();

        // Remove more than 2 consecutive blank lines
        var result = new List<string>();
        var blankCount = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                blankCount++;
                if (blankCount <= 2)
                    result.Add(line);
            }
            else
            {
                blankCount = 0;
                result.Add(line);
            }
        }

        return string.Join('\n', result);
    }

    private static IReadOnlyList<string> ChunkFallback(string content, ChunkingOptions options)
    {
        var chunks = new List<string>();
        var lines = content.Split('\n');
        var currentChunk = new List<string>();
        var currentLength = 0;

        foreach (var line in lines)
        {
            var lineLength = line.Length;

            if (currentLength + lineLength > options.MaxChunkSize && currentChunk.Count > 0)
            {
                chunks.Add(string.Join('\n', currentChunk));
                currentChunk.Clear();
                currentLength = 0;
            }

            currentChunk.Add(line);
            currentLength += lineLength + 1;
        }

        if (currentChunk.Count > 0)
        {
            chunks.Add(string.Join('\n', currentChunk));
        }

        return chunks;
    }

    private sealed class ChunkManifest
    {
        public int ChunkCount { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public ChunkingOptions? Options { get; set; }
        public bool IsMemorized { get; set; }
        public DateTimeOffset? MemorizedAt { get; set; }
    }
}

/// <summary>
/// Interface for content extraction (FileFlux integration).
/// </summary>
public interface IExtractor
{
    Task<ExtractionResult> ExtractAsync(string sourcePath, CancellationToken ct = default);
}

/// <summary>
/// Result of content extraction.
/// </summary>
public sealed class ExtractionResult
{
    public string Content { get; init; } = "";
    public Dictionary<string, byte[]>? Images { get; init; }
}

/// <summary>
/// Interface for content chunking (FileFlux integration).
/// </summary>
public interface IChunker
{
    Task<IReadOnlyList<string>> ChunkAsync(string content, ChunkingOptions options, CancellationToken ct = default);
}

/// <summary>
/// Interface for chunk memorization (FluxIndex integration).
/// </summary>
public interface IMemorizer
{
    Task MemorizeAsync(VaultEntry entry, IReadOnlyList<string> chunks, CancellationToken ct = default);
}
