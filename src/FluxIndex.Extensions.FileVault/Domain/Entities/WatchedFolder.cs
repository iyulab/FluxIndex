using System.Text.RegularExpressions;
using FluxIndex.Extensions.FileVault.Domain.Enums;

namespace FluxIndex.Extensions.FileVault.Domain.Entities;

/// <summary>
/// Represents a folder being watched by the vault.
/// </summary>
public sealed class WatchedFolder
{
    public Guid Id { get; private set; }
    public string Path { get; private set; }
    public string Name { get; private set; }
    public bool IsRecursive { get; private set; }
    public bool AutoMemorize { get; private set; }
    public string[] IncludePatterns { get; private set; }
    public string[] ExcludePatterns { get; private set; }
    public WatcherStatus Status { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastScannedAt { get; private set; }

    // Compiled regex patterns for efficient matching
    private List<Regex>? _includeRegexes;
    private List<Regex>? _excludeRegexes;

    private WatchedFolder()
    {
        Id = Guid.NewGuid();
        Path = string.Empty;
        Name = string.Empty;
        IncludePatterns = Array.Empty<string>();
        ExcludePatterns = Array.Empty<string>();
        Status = WatcherStatus.Active;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Creates a new watched folder.
    /// </summary>
    public static WatchedFolder Create(
        string path,
        string? name = null,
        bool isRecursive = true,
        bool autoMemorize = false,
        string[]? includePatterns = null,
        string[]? excludePatterns = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty", nameof(path));

        var normalizedPath = System.IO.Path.GetFullPath(path);

        var folder = new WatchedFolder
        {
            Path = normalizedPath,
            Name = name ?? System.IO.Path.GetFileName(normalizedPath) ?? normalizedPath,
            IsRecursive = isRecursive,
            AutoMemorize = autoMemorize
        };

        folder.SetPatterns(includePatterns, excludePatterns);

        // Validate folder exists
        if (!Directory.Exists(normalizedPath))
        {
            folder.MarkAsInvalid();
        }

        return folder;
    }

    /// <summary>
    /// Sets include and exclude patterns for file filtering.
    /// </summary>
    public void SetPatterns(string[]? includePatterns, string[]? excludePatterns)
    {
        IncludePatterns = includePatterns ?? GetDefaultIncludePatterns();
        ExcludePatterns = excludePatterns ?? GetDefaultExcludePatterns();

        // Compile patterns for efficient matching
        _includeRegexes = CompilePatterns(IncludePatterns);
        _excludeRegexes = CompilePatterns(ExcludePatterns);
    }

    /// <summary>
    /// Pauses watching for this folder.
    /// </summary>
    public void Pause()
    {
        if (Status == WatcherStatus.Active)
        {
            Status = WatcherStatus.Paused;
            ErrorMessage = null;
        }
    }

    /// <summary>
    /// Resumes watching for this folder.
    /// </summary>
    public void Resume()
    {
        if (Status == WatcherStatus.Paused || Status == WatcherStatus.Error)
        {
            if (Directory.Exists(Path))
            {
                Status = WatcherStatus.Active;
                ErrorMessage = null;
            }
            else
            {
                MarkAsInvalid();
            }
        }
    }

    /// <summary>
    /// Marks the folder as having an error.
    /// </summary>
    public void MarkAsError(string errorMessage)
    {
        Status = WatcherStatus.Error;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Marks the folder as invalid (e.g., deleted).
    /// </summary>
    public void MarkAsInvalid()
    {
        Status = WatcherStatus.Invalid;
        ErrorMessage = "Folder does not exist or is inaccessible";
    }

    /// <summary>
    /// Updates the folder path (e.g., after folder rename/move).
    /// </summary>
    public void UpdatePath(string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath))
            throw new ArgumentException("Path cannot be empty", nameof(newPath));

        Path = System.IO.Path.GetFullPath(newPath);

        if (Directory.Exists(Path))
        {
            if (Status == WatcherStatus.Invalid)
            {
                Status = WatcherStatus.Active;
                ErrorMessage = null;
            }
        }
        else
        {
            MarkAsInvalid();
        }
    }

    /// <summary>
    /// Updates the last scanned timestamp.
    /// </summary>
    public void UpdateLastScanned()
    {
        LastScannedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Updates folder settings.
    /// </summary>
    public void Update(
        string? name = null,
        bool? isRecursive = null,
        bool? autoMemorize = null)
    {
        if (name != null)
            Name = name;
        if (isRecursive.HasValue)
            IsRecursive = isRecursive.Value;
        if (autoMemorize.HasValue)
            AutoMemorize = autoMemorize.Value;
    }

    /// <summary>
    /// Checks if a file should be included based on patterns.
    /// </summary>
    public bool ShouldIncludeFile(string filePath)
    {
        var fileName = System.IO.Path.GetFileName(filePath);

        // Check exclude patterns first
        if (_excludeRegexes != null)
        {
            foreach (var regex in _excludeRegexes)
            {
                if (regex.IsMatch(fileName))
                    return false;
            }
        }

        // If no include patterns, include all (that aren't excluded)
        if (_includeRegexes == null || _includeRegexes.Count == 0)
            return true;

        // Check include patterns
        foreach (var regex in _includeRegexes)
        {
            if (regex.IsMatch(fileName))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if the folder path currently exists.
    /// </summary>
    public bool PathExists => Directory.Exists(Path);

    private static List<Regex> CompilePatterns(string[] patterns)
    {
        var regexes = new List<Regex>();
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            var regexPattern = GlobToRegex(pattern);
            regexes.Add(new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
        }
        return regexes;
    }

    private static string GlobToRegex(string glob)
    {
        var regex = "^";
        foreach (var c in glob)
        {
            regex += c switch
            {
                '*' => ".*",
                '?' => ".",
                '.' => @"\.",
                '\\' => @"\\",
                _ => Regex.Escape(c.ToString())
            };
        }
        return regex + "$";
    }

    private static string[] GetDefaultIncludePatterns() =>
    [
        "*.pdf", "*.docx", "*.doc", "*.xlsx", "*.xls", "*.pptx", "*.ppt",
        "*.txt", "*.md", "*.html", "*.htm", "*.rtf", "*.json", "*.xml"
    ];

    private static string[] GetDefaultExcludePatterns() =>
    [
        "~$*", "*.tmp", "*.bak", "Thumbs.db", ".DS_Store",
        "*.lock", "*.log", "desktop.ini", ".*"
    ];
}
