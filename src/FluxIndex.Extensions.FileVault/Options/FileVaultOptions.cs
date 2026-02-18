namespace FluxIndex.Extensions.FileVault.Options;

/// <summary>
/// Configuration options for FileVault.
/// </summary>
public sealed class FileVaultOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "FileVault";

    /// <summary>
    /// Default vault directory name (hidden folder marker).
    /// </summary>
    public const string DefaultVaultDirectoryName = ".vault";

    /// <summary>
    /// Vault directory name (hidden folder marker).
    /// Defaults to ".vault".
    /// </summary>
    public string VaultDirectoryName { get; set; } = DefaultVaultDirectoryName;

    /// <summary>
    /// Base path for vault data.
    /// If null, uses VaultDirectoryName relative to source file's directory.
    /// </summary>
    public string? VaultBasePath { get; set; }

    /// <summary>
    /// Maximum file size in megabytes to process.
    /// Larger files will be skipped.
    /// </summary>
    public int MaxFileSizeMB { get; set; } = 100;

    /// <summary>
    /// Enable real-time file watching.
    /// </summary>
    public bool EnableRealTimeWatch { get; set; } = true;

    /// <summary>
    /// Debounce delay in milliseconds for file change events.
    /// Multiple rapid changes within this window are merged into one event.
    /// </summary>
    public int DebounceDelayMs { get; set; } = 500;

    /// <summary>
    /// Internal buffer size for FileSystemWatcher in bytes.
    /// Larger buffers reduce the chance of missing events but use more memory.
    /// </summary>
    public int WatcherBufferSize { get; set; } = 65536;

    /// <summary>
    /// Number of versions to retain for each file.
    /// </summary>
    public int VersionRetentionCount { get; set; } = 5;

    /// <summary>
    /// Automatically cleanup orphaned files during sync.
    /// </summary>
    public bool AutoCleanupOrphans { get; set; }

    /// <summary>
    /// Default glob patterns for files to include.
    /// </summary>
    public List<string> DefaultIncludePatterns { get; set; } =
    [
        "*.pdf", "*.docx", "*.doc", "*.xlsx", "*.xls", "*.pptx", "*.ppt",
        "*.txt", "*.md", "*.rtf", "*.html", "*.htm",
        "*.json", "*.xml", "*.yaml", "*.yml", "*.csv"
    ];

    /// <summary>
    /// Default glob patterns for files to exclude.
    /// </summary>
    public List<string> DefaultExcludePatterns { get; set; } =
    [
        "~$*", "*.tmp", "*.temp", "*.bak", "*.swp",
        ".*", "Thumbs.db", "desktop.ini", ".DS_Store"
    ];

    /// <summary>
    /// Additional text file extensions for fallback extraction.
    /// These extensions are added to the built-in list when FileFlux is not available.
    /// Use lowercase with leading dot (e.g., ".myext").
    /// </summary>
    /// <remarks>
    /// Built-in extensions include common text, data, source code, and config formats.
    /// Add custom extensions here for domain-specific text files.
    /// </remarks>
    public HashSet<string> AdditionalTextExtensions { get; set; } = [];

    /// <summary>
    /// Default chunking options.
    /// </summary>
    public ChunkingDefaults Chunking { get; set; } = new();

    /// <summary>
    /// Maximum file size in bytes.
    /// </summary>
    public long MaxFileSizeBytes => MaxFileSizeMB * 1024L * 1024L;

    // === Queue Processing Options ===

    /// <summary>
    /// Maximum concurrent file processing operations.
    /// </summary>
    public int MaxConcurrentProcessing { get; set; } = 4;

    /// <summary>
    /// Polling interval in milliseconds when queue is empty.
    /// </summary>
    public int QueuePollingIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Enable automatic retry on failure.
    /// </summary>
    public bool EnableAutoRetry { get; set; } = true;

    /// <summary>
    /// Maximum retry attempts for failed items.
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// Delay in milliseconds before retrying a failed item.
    /// </summary>
    public int RetryDelayMs { get; set; } = 5000;

    /// <summary>
    /// Enable background queue processing.
    /// </summary>
    public bool EnableBackgroundProcessing { get; set; } = true;
}

/// <summary>
/// Default chunking configuration.
/// </summary>
public sealed class ChunkingDefaults
{
    /// <summary>
    /// Maximum chunk size in tokens.
    /// </summary>
    public int MaxChunkSize { get; set; } = 1024;

    /// <summary>
    /// Overlap size between chunks in tokens.
    /// </summary>
    public int OverlapSize { get; set; } = 128;

    /// <summary>
    /// Default chunking strategy.
    /// </summary>
    public string Strategy { get; set; } = "Intelligent";

    /// <summary>
    /// Default language for chunking (null = auto-detect).
    /// </summary>
    public string? Language { get; set; }
}
