using System.Text.RegularExpressions;

namespace FluxIndex.Extensions.FileVault.Domain.ValueObjects;

/// <summary>
/// Compiled glob pattern for efficient file matching.
/// </summary>
public sealed class FilePattern
{
    private readonly Regex _regex;
    private readonly string _pattern;

    public string Pattern => _pattern;

    private FilePattern(string pattern, Regex regex)
    {
        _pattern = pattern;
        _regex = regex;
    }

    /// <summary>
    /// Creates a FilePattern from a glob pattern.
    /// Supports: * (any chars), ? (single char), ** (directory recursion)
    /// </summary>
    public static FilePattern FromGlob(string globPattern)
    {
        ArgumentNullException.ThrowIfNull(globPattern);

        var regexPattern = GlobToRegex(globPattern);
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        return new FilePattern(globPattern, regex);
    }

    /// <summary>
    /// Tests if a file path matches this pattern.
    /// </summary>
    public bool IsMatch(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        // Normalize path separators
        var normalized = filePath.Replace('\\', '/');
        return _regex.IsMatch(normalized);
    }

    /// <summary>
    /// Converts a glob pattern to regex.
    /// </summary>
    private static string GlobToRegex(string glob)
    {
        // Normalize path separators in pattern
        glob = glob.Replace('\\', '/');

        var regex = new System.Text.StringBuilder();
        regex.Append('^');

        var i = 0;
        while (i < glob.Length)
        {
            var c = glob[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        // ** matches any path including /
                        regex.Append(".*");
                        i++; // Skip next *
                    }
                    else
                    {
                        // * matches anything except /
                        regex.Append("[^/]*");
                    }
                    break;
                case '?':
                    // ? matches single character except /
                    regex.Append("[^/]");
                    break;
                case '.':
                case '+':
                case '^':
                case '$':
                case '(':
                case ')':
                case '[':
                case ']':
                case '{':
                case '}':
                case '|':
                case '\\':
                    // Escape regex special characters
                    regex.Append('\\').Append(c);
                    break;
                default:
                    regex.Append(c);
                    break;
            }
            i++;
        }

        regex.Append('$');
        return regex.ToString();
    }

    public override string ToString() => _pattern;

    public override bool Equals(object? obj) =>
        obj is FilePattern other && _pattern.Equals(other._pattern, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(_pattern);
}
