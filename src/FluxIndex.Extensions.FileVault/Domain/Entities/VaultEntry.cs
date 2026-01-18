using FluxIndex.Extensions.FileVault.Domain.Enums;
using FluxIndex.Extensions.FileVault.Domain.ValueObjects;

namespace FluxIndex.Extensions.FileVault.Domain.Entities;

/// <summary>
/// Represents a file entry in the vault with its processing state.
/// Each entry has its own Git repository for version tracking.
/// </summary>
public sealed class VaultEntry
{
    /// <summary>
    /// Unique identifier for this vault entry.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Content hash of the source file (used as directory name).
    /// </summary>
    public ContentHash SourceHash { get; private set; }

    /// <summary>
    /// Original file path.
    /// </summary>
    public string SourcePath { get; private set; }

    /// <summary>
    /// File name without path.
    /// </summary>
    public string FileName => Path.GetFileName(SourcePath);

    /// <summary>
    /// Current processing stage.
    /// </summary>
    public ProcessingStage Stage { get; private set; }

    /// <summary>
    /// Path to the vault entry directory (.fluxindex/{hash}/).
    /// </summary>
    public string VaultPath { get; private set; }

    /// <summary>
    /// Number of chunks generated.
    /// </summary>
    public int ChunkCount { get; private set; }

    /// <summary>
    /// Timestamp when source was registered.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Timestamp of last processing.
    /// </summary>
    public DateTimeOffset? LastProcessedAt { get; private set; }

    /// <summary>
    /// Whether the refined content was manually edited.
    /// </summary>
    public bool IsRefinedEdited { get; private set; }

    private VaultEntry() { }

    /// <summary>
    /// Creates a new vault entry for a source file.
    /// </summary>
    public static VaultEntry Create(string sourcePath, ContentHash sourceHash, string vaultBasePath)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(vaultBasePath);

        var vaultPath = Path.Combine(vaultBasePath, sourceHash.Value);

        return new VaultEntry
        {
            Id = Guid.NewGuid(),
            SourcePath = Path.GetFullPath(sourcePath),
            SourceHash = sourceHash,
            VaultPath = vaultPath,
            Stage = ProcessingStage.Source,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Loads an existing vault entry from disk.
    /// </summary>
    public static VaultEntry Load(string vaultPath)
    {
        var sourceJsonPath = Path.Combine(vaultPath, "source.json");
        if (!File.Exists(sourceJsonPath))
            throw new InvalidOperationException($"source.json not found in {vaultPath}");

        var json = File.ReadAllText(sourceJsonPath);
        var sourceInfo = System.Text.Json.JsonSerializer.Deserialize<SourceInfo>(json)
            ?? throw new InvalidOperationException("Failed to deserialize source.json");

        var entry = new VaultEntry
        {
            Id = sourceInfo.Id ?? Guid.NewGuid(),
            SourcePath = sourceInfo.SourcePath,
            SourceHash = ContentHash.FromHex(sourceInfo.SourceHash),
            VaultPath = vaultPath,
            CreatedAt = sourceInfo.CreatedAt,
            LastProcessedAt = sourceInfo.LastProcessedAt,
            IsRefinedEdited = sourceInfo.IsRefinedEdited
        };

        entry.DetermineStage();
        return entry;
    }

    /// <summary>
    /// Advances to Extracted stage.
    /// </summary>
    public void MarkExtracted()
    {
        Stage = ProcessingStage.Extracted;
        LastProcessedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Advances to Refined stage.
    /// </summary>
    public void MarkRefined(bool isManualEdit = false)
    {
        Stage = ProcessingStage.Refined;
        IsRefinedEdited = isManualEdit;
        LastProcessedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Advances to Chunked stage.
    /// </summary>
    public void MarkChunked(int chunkCount)
    {
        Stage = ProcessingStage.Chunked;
        ChunkCount = chunkCount;
        LastProcessedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Advances to Memorized stage.
    /// </summary>
    public void MarkMemorized()
    {
        Stage = ProcessingStage.Memorized;
        LastProcessedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Resets to a specific stage (for reprocessing).
    /// </summary>
    public void ResetToStage(ProcessingStage stage)
    {
        Stage = stage;
        if (stage < ProcessingStage.Chunked)
            ChunkCount = 0;
    }

    /// <summary>
    /// Saves source info to disk.
    /// </summary>
    public void SaveSourceInfo()
    {
        Directory.CreateDirectory(VaultPath);

        var sourceInfo = new SourceInfo
        {
            Id = Id,
            SourcePath = SourcePath,
            SourceHash = SourceHash.Value,
            CreatedAt = CreatedAt,
            LastProcessedAt = LastProcessedAt,
            IsRefinedEdited = IsRefinedEdited
        };

        var json = System.Text.Json.JsonSerializer.Serialize(sourceInfo, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(Path.Combine(VaultPath, "source.json"), json);
    }

    private void DetermineStage()
    {
        // Determine stage based on existing files
        var chunksDir = Path.Combine(VaultPath, "chunks");
        var manifestPath = Path.Combine(chunksDir, "manifest.json");

        if (File.Exists(manifestPath))
        {
            var manifest = System.Text.Json.JsonSerializer.Deserialize<ChunkManifest>(
                File.ReadAllText(manifestPath));
            ChunkCount = manifest?.ChunkCount ?? 0;

            Stage = manifest?.IsMemorized == true
                ? ProcessingStage.Memorized
                : ProcessingStage.Chunked;
            return;
        }

        if (File.Exists(Path.Combine(VaultPath, "refined.md")))
        {
            Stage = ProcessingStage.Refined;
            return;
        }

        if (File.Exists(Path.Combine(VaultPath, "extracted.md")))
        {
            Stage = ProcessingStage.Extracted;
            return;
        }

        Stage = ProcessingStage.Source;
    }

    // File paths
    public string SourceJsonPath => Path.Combine(VaultPath, "source.json");
    public string ExtractedPath => Path.Combine(VaultPath, "extracted.md");
    public string ImagesPath => Path.Combine(VaultPath, "images");
    public string RefinedPath => Path.Combine(VaultPath, "refined.md");
    public string ChunksPath => Path.Combine(VaultPath, "chunks");
    public string ManifestPath => Path.Combine(ChunksPath, "manifest.json");

    private sealed class SourceInfo
    {
        public Guid? Id { get; set; }
        public string SourcePath { get; set; } = "";
        public string SourceHash { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? LastProcessedAt { get; set; }
        public bool IsRefinedEdited { get; set; }
    }

    private sealed class ChunkManifest
    {
        public int ChunkCount { get; set; }
        public bool IsMemorized { get; set; }
    }
}
