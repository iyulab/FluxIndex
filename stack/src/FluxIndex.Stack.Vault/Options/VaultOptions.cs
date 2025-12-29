namespace FluxIndex.Stack.Vault.Options;

/// <summary>
/// Configuration options for FluxIndex.Vault.
/// </summary>
public class VaultOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "FluxIndex:Vault";

    /// <summary>
    /// Whether the vault feature is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Base path for vault artifact storage.
    /// </summary>
    public string StoragePath { get; set; } = "./vault";

    /// <summary>
    /// Hash algorithm used for content change detection.
    /// </summary>
    public string HashAlgorithm { get; set; } = "SHA256";

    /// <summary>
    /// Whether to enable real-time file watching.
    /// </summary>
    public bool EnableRealTimeWatch { get; set; } = true;

    /// <summary>
    /// Interval for periodic full scan in minutes.
    /// </summary>
    public int ScanIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Debounce delay in milliseconds for file change events.
    /// </summary>
    public int DebounceDelayMs { get; set; } = 500;

    /// <summary>
    /// Internal buffer size for FileSystemWatcher.
    /// </summary>
    public int WatcherBufferSize { get; set; } = 65536;

    /// <summary>
    /// Maximum file size in MB to process.
    /// </summary>
    public int MaxFileSizeMB { get; set; } = 100;

    /// <summary>
    /// Number of versions to retain for each file.
    /// </summary>
    public int VersionRetentionCount { get; set; } = 5;

    /// <summary>
    /// Whether to automatically clean up orphaned files.
    /// </summary>
    public bool AutoCleanupOrphans { get; set; } = false;

    /// <summary>
    /// Default file patterns configuration.
    /// </summary>
    public PatternOptions DefaultPatterns { get; set; } = new();
}

/// <summary>
/// File pattern matching options.
/// </summary>
public class PatternOptions
{
    /// <summary>
    /// File patterns to include (e.g., "*.docx", "*.pdf").
    /// </summary>
    public string[] Include { get; set; } = new[]
    {
        "*.docx", "*.pdf", "*.txt", "*.md", "*.html", "*.htm",
        "*.pptx", "*.xlsx", "*.doc", "*.xls", "*.ppt", "*.rtf"
    };

    /// <summary>
    /// File patterns to exclude (e.g., "~$*", "*.tmp").
    /// </summary>
    public string[] Exclude { get; set; } = new[]
    {
        "~$*", "*.tmp", "*.bak", "Thumbs.db", ".DS_Store",
        "*.lock", "*.log", "desktop.ini"
    };
}
