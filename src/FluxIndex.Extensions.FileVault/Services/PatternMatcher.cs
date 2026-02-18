using FluxIndex.Extensions.FileVault.Domain.ValueObjects;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// Service for matching file paths against glob patterns.
/// </summary>
public sealed class PatternMatcher
{
    private readonly List<FilePattern> _includePatterns;
    private readonly List<FilePattern> _excludePatterns;

    public PatternMatcher(
        IEnumerable<string>? includePatterns = null,
        IEnumerable<string>? excludePatterns = null)
    {
        _includePatterns = (includePatterns ?? [])
            .Select(FilePattern.FromGlob)
            .ToList();

        _excludePatterns = (excludePatterns ?? [])
            .Select(FilePattern.FromGlob)
            .ToList();
    }

    /// <summary>
    /// Checks if a file path matches the include/exclude patterns.
    /// </summary>
    public bool IsMatch(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var fileName = Path.GetFileName(filePath);

        // Check exclude patterns first (takes precedence)
        if (_excludePatterns.Any(p => p.IsMatch(fileName) || p.IsMatch(filePath)))
            return false;

        // If no include patterns, include all non-excluded files
        if (_includePatterns.Count == 0)
            return true;

        // Check include patterns
        return _includePatterns.Any(p => p.IsMatch(fileName) || p.IsMatch(filePath));
    }

    /// <summary>
    /// Filters file paths to only those matching the patterns.
    /// </summary>
    public IEnumerable<string> Filter(IEnumerable<string> filePaths)
    {
        return filePaths.Where(IsMatch);
    }

    /// <summary>
    /// Checks if a file should be included based on dynamic patterns.
    /// </summary>
    /// <param name="filePath">The file path to check.</param>
    /// <param name="includePatterns">Patterns to include.</param>
    /// <param name="excludePatterns">Patterns to exclude.</param>
    /// <returns>True if the file should be included.</returns>
    public static bool ShouldInclude(string filePath, IEnumerable<string>? includePatterns, IEnumerable<string>? excludePatterns)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var fileName = Path.GetFileName(filePath);
        var includeList = (includePatterns ?? []).Select(FilePattern.FromGlob).ToList();
        var excludeList = (excludePatterns ?? []).Select(FilePattern.FromGlob).ToList();

        // Check exclude patterns first (takes precedence)
        if (excludeList.Any(p => p.IsMatch(fileName) || p.IsMatch(filePath)))
            return false;

        // If no include patterns, include all non-excluded files
        if (includeList.Count == 0)
            return true;

        // Check include patterns
        return includeList.Any(p => p.IsMatch(fileName) || p.IsMatch(filePath));
    }

    /// <summary>
    /// Creates a default pattern matcher for common document types.
    /// </summary>
    public static PatternMatcher CreateDefault()
    {
        return new PatternMatcher(
            includePatterns: [
                "*.pdf", "*.docx", "*.doc", "*.xlsx", "*.xls", "*.pptx", "*.ppt",
                "*.txt", "*.md", "*.rtf", "*.html", "*.htm",
                "*.json", "*.xml", "*.yaml", "*.yml", "*.csv"
            ],
            excludePatterns: [
                "~$*", "*.tmp", "*.temp", "*.bak", "*.swp",
                ".*", "Thumbs.db", "desktop.ini", ".DS_Store"
            ]);
    }
}
