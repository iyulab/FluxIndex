namespace FluxIndex.Extensions.FileVault.Interfaces;

/// <summary>
/// Service for Git operations on vault entries.
/// Each vault entry ({hash}/) has its own Git repository.
/// </summary>
public interface IGitService
{
    /// <summary>
    /// Initializes a new Git repository in the vault entry directory.
    /// </summary>
    Task InitAsync(string vaultPath, CancellationToken ct = default);

    /// <summary>
    /// Stages all changes in the vault entry.
    /// </summary>
    Task StageAllAsync(string vaultPath, CancellationToken ct = default);

    /// <summary>
    /// Creates a commit with the given message.
    /// </summary>
    Task CommitAsync(string vaultPath, string message, CancellationToken ct = default);

    /// <summary>
    /// Gets the diff for a specific file or all files.
    /// </summary>
    Task<string> DiffAsync(string vaultPath, string? filePath = null, CancellationToken ct = default);

    /// <summary>
    /// Gets the status of the repository.
    /// </summary>
    Task<GitStatus> StatusAsync(string vaultPath, CancellationToken ct = default);

    /// <summary>
    /// Gets the commit log.
    /// </summary>
    Task<IReadOnlyList<GitCommit>> LogAsync(string vaultPath, int maxCount = 10, CancellationToken ct = default);

    /// <summary>
    /// Checks out a file from a specific commit.
    /// </summary>
    Task CheckoutFileAsync(string vaultPath, string filePath, string commitish, CancellationToken ct = default);

    /// <summary>
    /// Checks if the directory is a Git repository.
    /// </summary>
    bool IsGitRepository(string vaultPath);
}

/// <summary>
/// Git repository status.
/// </summary>
public sealed class GitStatus
{
    public bool HasChanges { get; init; }
    public IReadOnlyList<string> ModifiedFiles { get; init; } = [];
    public IReadOnlyList<string> AddedFiles { get; init; } = [];
    public IReadOnlyList<string> DeletedFiles { get; init; } = [];
}

/// <summary>
/// Git commit information.
/// </summary>
public sealed class GitCommit
{
    public string Hash { get; init; } = "";
    public string ShortHash => Hash.Length > 7 ? Hash[..7] : Hash;
    public string Message { get; init; } = "";
    public DateTimeOffset Timestamp { get; init; }
}
