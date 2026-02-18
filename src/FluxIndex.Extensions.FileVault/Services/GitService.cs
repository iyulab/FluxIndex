using System.Diagnostics;
using System.Text;
using FluxIndex.Extensions.FileVault.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// Git operations service using CLI commands.
/// </summary>
public sealed partial class GitService : IGitService
{
    private readonly ILogger<GitService> _logger;

    public GitService(ILogger<GitService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitAsync(string vaultPath, CancellationToken ct = default)
    {
        if (IsGitRepository(vaultPath))
        {
            LogRepoAlreadyExists(_logger, vaultPath);
            return;
        }

        Directory.CreateDirectory(vaultPath);
        await RunGitAsync(vaultPath, "init", ct);

        // Configure user for this repo
        await RunGitAsync(vaultPath, "config user.email \"fluxindex@local\"", ct);
        await RunGitAsync(vaultPath, "config user.name \"FluxIndex Vault\"", ct);

        LogInitializedRepo(_logger, vaultPath);
    }

    public async Task StageAllAsync(string vaultPath, CancellationToken ct = default)
    {
        await RunGitAsync(vaultPath, "add -A", ct);
    }

    public async Task<string?> CommitAsync(string vaultPath, string message, CancellationToken ct = default)
    {
        // Check if there are changes to commit
        var status = await StatusAsync(vaultPath, ct);
        if (!status.HasChanges)
        {
            LogNoChangesToCommit(_logger, vaultPath);
            return null;
        }

        await StageAllAsync(vaultPath, ct);

        // Escape message for command line
        var escapedMessage = message.Replace("\"", "\\\"");
        await RunGitAsync(vaultPath, $"commit -m \"{escapedMessage}\"", ct);

        // Get the commit hash
        var commitHash = await RunGitAsync(vaultPath, "rev-parse HEAD", ct);

        LogCommitted(_logger, vaultPath, message, commitHash[..7]);
        return commitHash;
    }

    public async Task<string> DiffAsync(string vaultPath, string? filePath = null, CancellationToken ct = default)
    {
        var args = string.IsNullOrEmpty(filePath) ? "diff" : $"diff -- \"{filePath}\"";
        return await RunGitAsync(vaultPath, args, ct);
    }

    public async Task<GitStatus> StatusAsync(string vaultPath, CancellationToken ct = default)
    {
        if (!IsGitRepository(vaultPath))
        {
            return new GitStatus { HasChanges = false };
        }

        var output = await RunGitAsync(vaultPath, "status --porcelain", ct);

        var modified = new List<string>();
        var added = new List<string>();
        var deleted = new List<string>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 3) continue;

            var status = line[..2];
            var file = line[3..].Trim();

            if (status.Contains('M'))
                modified.Add(file);
            else if (status.Contains('A') || status.Contains('?'))
                added.Add(file);
            else if (status.Contains('D'))
                deleted.Add(file);
        }

        return new GitStatus
        {
            HasChanges = modified.Count > 0 || added.Count > 0 || deleted.Count > 0,
            ModifiedFiles = modified,
            AddedFiles = added,
            DeletedFiles = deleted
        };
    }

    public async Task<IReadOnlyList<GitCommit>> LogAsync(string vaultPath, int maxCount = 10, CancellationToken ct = default)
    {
        if (!IsGitRepository(vaultPath))
        {
            return [];
        }

        var output = await RunGitAsync(vaultPath, $"log --format=\"%H|%s|%aI\" -n {maxCount}", ct);

        var commits = new List<GitCommit>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|', 3);
            if (parts.Length >= 3)
            {
                commits.Add(new GitCommit
                {
                    Hash = parts[0],
                    Message = parts[1],
                    Timestamp = DateTimeOffset.TryParse(parts[2], out var ts) ? ts : DateTimeOffset.MinValue
                });
            }
        }

        return commits;
    }

    public async Task CheckoutFileAsync(string vaultPath, string filePath, string commitish, CancellationToken ct = default)
    {
        await RunGitAsync(vaultPath, $"checkout {commitish} -- \"{filePath}\"", ct);
        LogCheckedOut(_logger, filePath, commitish);
    }

    public bool IsGitRepository(string vaultPath)
    {
        return Directory.Exists(Path.Combine(vaultPath, ".git"));
    }

    private async Task<string> RunGitAsync(string workingDirectory, string arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };

        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) error.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0 && error.Length > 0)
        {
            var errorMessage = error.ToString().Trim();
            // Ignore "nothing to commit" type messages
            if (!errorMessage.Contains("nothing to commit") &&
                !errorMessage.Contains("Already on"))
            {
                LogGitCommandFailed(_logger, arguments, errorMessage);
            }
        }

        return output.ToString().Trim();
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Git repository already exists at {Path}")]
    private static partial void LogRepoAlreadyExists(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Initialized Git repository at {Path}")]
    private static partial void LogInitializedRepo(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No changes to commit at {Path}")]
    private static partial void LogNoChangesToCommit(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Committed changes at {Path}: {Message} ({Hash})")]
    private static partial void LogCommitted(ILogger logger, string path, string message, string hash);

    [LoggerMessage(Level = LogLevel.Information, Message = "Checked out {File} from {Commit}")]
    private static partial void LogCheckedOut(ILogger logger, string file, string commit);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Git command failed: git {Args} -> {Error}")]
    private static partial void LogGitCommandFailed(ILogger logger, string args, string error);

    #endregion
}
