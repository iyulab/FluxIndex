namespace FluxIndex.MCP.Workspace;

/// <summary>
/// Locates .fluxindex workspace directory similar to .git directory traversal
/// </summary>
public static class WorkspaceLocator
{
    public const string WorkspaceDirectoryName = ".fluxindex";
    public const string ConfigFileName = "config.json";
    public const string DatabaseFileName = "index.db";

    /// <summary>
    /// Find the workspace root by traversing up from the start path
    /// </summary>
    public static string? FindWorkspaceRoot(string? startPath = null)
    {
        startPath ??= Directory.GetCurrentDirectory();

        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            var workspacePath = Path.Combine(current.FullName, WorkspaceDirectoryName);
            if (Directory.Exists(workspacePath))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// Get the .fluxindex directory path for a workspace root
    /// </summary>
    public static string GetWorkspaceDirectory(string workspaceRoot)
        => Path.Combine(workspaceRoot, WorkspaceDirectoryName);

    /// <summary>
    /// Get the config.json path for a workspace root
    /// </summary>
    public static string GetConfigPath(string workspaceRoot)
        => Path.Combine(workspaceRoot, WorkspaceDirectoryName, ConfigFileName);

    /// <summary>
    /// Get the database path for a workspace root
    /// </summary>
    public static string GetDatabasePath(string workspaceRoot)
        => Path.Combine(workspaceRoot, WorkspaceDirectoryName, DatabaseFileName);

    /// <summary>
    /// Check if a path is within the workspace (sandbox check)
    /// </summary>
    public static bool IsPathWithinWorkspace(string workspaceRoot, string targetPath)
    {
        var fullWorkspacePath = Path.GetFullPath(workspaceRoot);
        var fullTargetPath = Path.GetFullPath(targetPath);

        return fullTargetPath.StartsWith(fullWorkspacePath, StringComparison.OrdinalIgnoreCase);
    }
}
