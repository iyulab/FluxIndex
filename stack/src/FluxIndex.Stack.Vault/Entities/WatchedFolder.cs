using FluxIndex.Stack.Vault.Enums;

namespace FluxIndex.Stack.Vault.Entities;

/// <summary>
/// Represents a folder being monitored for changes.
/// </summary>
public class WatchedFolder
{
    public Guid Id { get; private set; }
    public string Path { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsRecursive { get; private set; }
    public string[] IncludePatterns { get; private set; } = Array.Empty<string>();
    public string[] ExcludePatterns { get; private set; } = Array.Empty<string>();
    public bool AutoMemorize { get; private set; }
    public WatcherStatus Status { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastScannedAt { get; private set; }

    // Foreign key to collection
    public Guid? CollectionId { get; private set; }

    // Navigation properties
    public List<TrackedFile> TrackedFiles { get; private set; } = new();

    private WatchedFolder() { } // EF Core

    public static WatchedFolder Create(
        string path,
        string? name = null,
        bool isRecursive = true,
        bool autoMemorize = true,
        Guid? collectionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new WatchedFolder
        {
            Id = Guid.NewGuid(),
            Path = path,
            Name = name ?? System.IO.Path.GetFileName(path) ?? path,
            IsRecursive = isRecursive,
            AutoMemorize = autoMemorize,
            IncludePatterns = Array.Empty<string>(),
            ExcludePatterns = Array.Empty<string>(),
            Status = WatcherStatus.Active,
            CollectionId = collectionId,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void SetPatterns(string[]? includePatterns, string[]? excludePatterns)
    {
        IncludePatterns = includePatterns ?? Array.Empty<string>();
        ExcludePatterns = excludePatterns ?? Array.Empty<string>();
    }

    public void Pause()
    {
        Status = WatcherStatus.Paused;
        ErrorMessage = null;
    }

    public void Resume()
    {
        Status = WatcherStatus.Active;
        ErrorMessage = null;
    }

    public void MarkAsError(string errorMessage)
    {
        Status = WatcherStatus.Error;
        ErrorMessage = errorMessage;
    }

    public void MarkAsInvalid()
    {
        Status = WatcherStatus.Invalid;
    }

    public void UpdateLastScanned()
    {
        LastScannedAt = DateTime.UtcNow;
    }

    public void Update(
        string? name = null,
        bool? isRecursive = null,
        bool? autoMemorize = null,
        Guid? collectionId = null)
    {
        if (name != null) Name = name;
        if (isRecursive.HasValue) IsRecursive = isRecursive.Value;
        if (autoMemorize.HasValue) AutoMemorize = autoMemorize.Value;
        if (collectionId.HasValue) CollectionId = collectionId.Value;
    }

    public bool ShouldIncludeFile(string fileName)
    {
        // If no include patterns specified, include all
        if (IncludePatterns.Length == 0)
            return !IsExcluded(fileName);

        // Check if file matches any include pattern
        var isIncluded = IncludePatterns.Any(pattern =>
            MatchesPattern(fileName, pattern));

        return isIncluded && !IsExcluded(fileName);
    }

    private bool IsExcluded(string fileName)
    {
        return ExcludePatterns.Any(pattern =>
            MatchesPattern(fileName, pattern));
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        // Simple glob pattern matching
        // Supports * (any characters) and ? (single character)
        var regexPattern = "^" +
            System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") +
            "$";

        return System.Text.RegularExpressions.Regex.IsMatch(
            fileName,
            regexPattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
