using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Enums;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Vault;
using Microsoft.AspNetCore.Mvc;

namespace FluxIndex.Stack.Api.Controllers;

/// <summary>
/// API controller for vault operations (file system synchronization).
/// Uses FluxIndex.Extensions.FileVault for core functionality.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public partial class VaultController : ControllerBase
{
    private readonly IVault _vault;
    private readonly IDocumentRepository _documentRepository;
    private readonly ILogger<VaultController> _logger;

    public VaultController(
        IVault vault,
        IDocumentRepository documentRepository,
        ILogger<VaultController> logger)
    {
        _vault = vault;
        _documentRepository = documentRepository;
        _logger = logger;
    }

    #region Folder Endpoints

    /// <summary>
    /// Gets all watched folders.
    /// </summary>
    [HttpGet("folders")]
    public async Task<ActionResult<ApiResponse<List<WatchedFolderDto>>>> GetFolders(
        CancellationToken cancellationToken = default)
    {
        var folders = await _vault.GetAllWatchedFoldersAsync(cancellationToken);
        var dtos = folders.Select(MapToDto).ToList();

        return Ok(ApiResponse<List<WatchedFolderDto>>.Ok(dtos));
    }

    /// <summary>
    /// Gets a watched folder by ID.
    /// </summary>
    [HttpGet("folders/{id:guid}")]
    public async Task<ActionResult<ApiResponse<WatchedFolderDto>>> GetFolder(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var folder = await _vault.GetWatchedFolderAsync(id, cancellationToken);
        if (folder == null)
        {
            return NotFound(ApiResponse<WatchedFolderDto>.Fail($"Folder with id '{id}' not found."));
        }

        return Ok(ApiResponse<WatchedFolderDto>.Ok(MapToDto(folder)));
    }

    /// <summary>
    /// Adds a new watched folder.
    /// </summary>
    [HttpPost("folders")]
    public async Task<ActionResult<ApiResponse<WatchedFolderDto>>> AddFolder(
        [FromBody] AddWatchedFolderRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var folder = await _vault.AddWatchedFolderAsync(
                request.Path,
                request.Name,
                request.IsRecursive,
                request.AutoMemorize,
                request.IncludePatterns,
                request.ExcludePatterns,
                cancellationToken);

            return CreatedAtAction(nameof(GetFolder), new { id = folder.Id },
                ApiResponse<WatchedFolderDto>.Ok(MapToDto(folder)));
        }
        catch (DirectoryNotFoundException ex)
        {
            return BadRequest(ApiResponse<WatchedFolderDto>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiResponse<WatchedFolderDto>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Removes a watched folder.
    /// </summary>
    [HttpDelete("folders/{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> RemoveFolder(
        Guid id,
        [FromQuery] bool removeTrackedFiles = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _vault.RemoveWatchedFolderAsync(id, removeTrackedFiles, cancellationToken);
            return Ok(ApiResponse<bool>.Ok(true));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<bool>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Triggers a scan of a watched folder.
    /// </summary>
    [HttpPost("folders/{id:guid}/scan")]
    public async Task<ActionResult<ApiResponse<ScanResultDto>>> ScanFolder(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _vault.ScanFolderAsync(id, cancellationToken);
            var dto = new ScanResultDto
            {
                FolderId = id,
                TotalFilesFound = result.ScannedCount,
                NewFilesQueued = result.NewFilesCount,
                ChangedFilesQueued = result.ChangedFilesCount,
                OrphanedFilesDetected = result.OrphanedFilesCount,
                SkippedFiles = result.SkippedFilesCount,
                Errors = result.Errors.Select(e => e.ErrorMessage).ToList(),
                DurationSeconds = result.Duration.TotalSeconds
            };
            return Ok(ApiResponse<ScanResultDto>.Ok(dto));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<ScanResultDto>.Fail(ex.Message));
        }
        catch (DirectoryNotFoundException ex)
        {
            return BadRequest(ApiResponse<ScanResultDto>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Pauses watching a folder.
    /// </summary>
    [HttpPost("folders/{id:guid}/pause")]
    public async Task<ActionResult<ApiResponse<bool>>> PauseFolder(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _vault.PauseWatchingAsync(id, cancellationToken);
            return Ok(ApiResponse<bool>.Ok(true));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<bool>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Resumes watching a folder.
    /// </summary>
    [HttpPost("folders/{id:guid}/resume")]
    public async Task<ActionResult<ApiResponse<bool>>> ResumeFolder(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _vault.ResumeWatchingAsync(id, cancellationToken);
            return Ok(ApiResponse<bool>.Ok(true));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<bool>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Gets all tracked files for a watched folder.
    /// </summary>
    [HttpGet("folders/{id:guid}/files")]
    public async Task<ActionResult<ApiResponse<List<TrackedFileDto>>>> GetFolderFiles(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var folder = await _vault.GetWatchedFolderAsync(id, cancellationToken);
        if (folder == null)
        {
            return NotFound(ApiResponse<List<TrackedFileDto>>.Fail($"Folder with id '{id}' not found."));
        }

        // List all entries in the vault
        var entries = await _vault.ListAsync(null, cancellationToken);
        var dtos = entries.Select(MapToTrackedFileDto).ToList();

        return Ok(ApiResponse<List<TrackedFileDto>>.Ok(dtos));
    }

    #endregion

    #region File Endpoints

    /// <summary>
    /// Gets a tracked file by path.
    /// </summary>
    [HttpGet("files")]
    public async Task<ActionResult<ApiResponse<TrackedFileDto>>> GetFileByPath(
        [FromQuery] string path,
        CancellationToken cancellationToken = default)
    {
        var entry = await _vault.GetAsync(path, cancellationToken);
        if (entry == null)
        {
            return NotFound(ApiResponse<TrackedFileDto>.Fail($"File not tracked: {path}"));
        }

        return Ok(ApiResponse<TrackedFileDto>.Ok(MapToTrackedFileDto(entry)));
    }

    /// <summary>
    /// Memorizes a file (adds it to the vault).
    /// </summary>
    [HttpPost("files/memorize")]
    public async Task<ActionResult<ApiResponse<TrackedFileDto>>> MemorizeFile(
        [FromBody] MemorizeFileRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entry = await _vault.MemorizeAsync(request.SourcePath, cancellationToken);
            return Ok(ApiResponse<TrackedFileDto>.Ok(MapToTrackedFileDto(entry)));
        }
        catch (FileNotFoundException ex)
        {
            return BadRequest(ApiResponse<TrackedFileDto>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<TrackedFileDto>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Unmemorizes a file (removes it from the vault).
    /// </summary>
    [HttpDelete("files")]
    public async Task<ActionResult<ApiResponse<bool>>> UnmemorizeFile(
        [FromQuery] string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _vault.RemoveAsync(path, cancellationToken);
            return Ok(ApiResponse<bool>.Ok(true));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<bool>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Refreshes a file (re-processes vault content without re-extraction).
    /// </summary>
    [HttpPost("files/refresh")]
    public async Task<ActionResult<ApiResponse<TrackedFileDto>>> RefreshFile(
        [FromBody] RefreshFileRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entry = await _vault.RefreshAsync(request.SourcePath, cancellationToken);
            return Ok(ApiResponse<TrackedFileDto>.Ok(MapToTrackedFileDto(entry)));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<TrackedFileDto>.Fail(ex.Message));
        }
        catch (FileNotFoundException ex)
        {
            return BadRequest(ApiResponse<TrackedFileDto>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Detects changes for a file.
    /// </summary>
    [HttpGet("files/changes")]
    public async Task<ActionResult<ApiResponse<ChangeDetectionResultDto>>> DetectChanges(
        [FromQuery] string path,
        CancellationToken cancellationToken = default)
    {
        var result = await _vault.DetectChangesAsync(path, cancellationToken);
        var dto = new ChangeDetectionResultDto
        {
            FilePath = result.FilePath,
            FileName = result.FileName,
            FileExtension = result.FileExtension,
            FileSize = result.FileSize,
            FileModifiedAt = result.FileModifiedAt,
            EntryExists = result.EntryExists,
            SourceExists = result.SourceExists,
            SourceChanged = result.SourceChanged,
            VaultChanged = result.VaultChanged,
            HasChanges = result.HasChanges,
            RecommendedAction = result.RecommendedAction.ToString(),
            Stage = result.Stage?.ToString(),
            SyncStatus = result.SyncStatus?.ToString(),
            ChunkCount = result.ChunkCount,
            LastError = result.LastError
        };

        return Ok(ApiResponse<ChangeDetectionResultDto>.Ok(dto));
    }

    #endregion

    #region Sync & Status Endpoints

    /// <summary>
    /// Gets the current vault status.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<VaultStatusDto>>> GetStatus(
        CancellationToken cancellationToken = default)
    {
        var status = await _vault.StatusAsync(cancellationToken);
        var dto = new VaultStatusDto
        {
            IsEnabled = true,
            ActiveWatchers = status.ActiveWatcherCount,
            TotalTrackedFiles = status.TotalEntries,
            MemorizedFiles = status.MemorizedCount,
            QueuedFiles = status.QueuedCount,
            ProcessingFiles = status.ProcessingCount,
            StaleFiles = status.SourceModifiedCount + status.VaultModifiedCount,
            OrphanedFiles = status.OrphanedCount,
            ErrorFiles = status.ErrorCount,
            LastSyncAt = status.LastSyncTime?.DateTime
        };
        return Ok(ApiResponse<VaultStatusDto>.Ok(dto));
    }

    /// <summary>
    /// Performs a full sync across all watched folders.
    /// </summary>
    [HttpPost("sync")]
    public async Task<ActionResult<ApiResponse<SyncResultDto>>> Sync(
        CancellationToken cancellationToken = default)
    {
        var result = await _vault.SyncAsync(cancellationToken);
        var dto = new SyncResultDto
        {
            FoldersScanned = result.FoldersScanned,
            FilesProcessed = result.NewFilesDiscovered + result.ChangedFilesDetected,
            FilesQueued = result.TotalQueuedCount,
            OrphanedFilesCleaned = result.OrphansQueued,
            Errors = result.Errors.Select(e => e.ErrorMessage).ToList(),
            DurationSeconds = result.Duration.TotalSeconds
        };
        return Ok(ApiResponse<SyncResultDto>.Ok(dto));
    }

    /// <summary>
    /// Cleans up orphaned files.
    /// </summary>
    [HttpPost("cleanup")]
    public async Task<ActionResult<ApiResponse<int>>> Cleanup(
        CancellationToken cancellationToken = default)
    {
        var count = await _vault.CleanupOrphanedEntriesAsync(cancellationToken);
        return Ok(ApiResponse<int>.Ok(count));
    }

    /// <summary>
    /// Gets the queue status.
    /// </summary>
    [HttpGet("queue")]
    public async Task<ActionResult<ApiResponse<QueueStatusDto>>> GetQueueStatus(
        CancellationToken cancellationToken = default)
    {
        var status = await _vault.GetQueueStatusAsync(cancellationToken);
        var dto = new QueueStatusDto
        {
            QueuedCount = status.QueuedCount,
            ProcessingCount = status.ProcessingCount,
            CompletedCount = status.CompletedCount,
            FailedCount = status.FailedCount,
            IsPaused = status.IsPaused,
            LastProcessedAt = status.LastProcessedAt
        };
        return Ok(ApiResponse<QueueStatusDto>.Ok(dto));
    }

    /// <summary>
    /// Pauses queue processing.
    /// </summary>
    [HttpPost("queue/pause")]
    public async Task<ActionResult<ApiResponse<bool>>> PauseQueue(
        CancellationToken cancellationToken = default)
    {
        await _vault.PauseQueueAsync(cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>
    /// Resumes queue processing.
    /// </summary>
    [HttpPost("queue/resume")]
    public async Task<ActionResult<ApiResponse<bool>>> ResumeQueue(
        CancellationToken cancellationToken = default)
    {
        await _vault.ResumeQueueAsync(cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    #endregion

    #region Git Operations

    /// <summary>
    /// Gets the diff for a vault entry.
    /// </summary>
    [HttpGet("files/diff")]
    public async Task<ActionResult<ApiResponse<string>>> GetDiff(
        [FromQuery] string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var diff = await _vault.DiffAsync(path, cancellationToken);
            return Ok(ApiResponse<string>.Ok(diff));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<string>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Gets the commit history for a vault entry.
    /// </summary>
    [HttpGet("files/history")]
    public async Task<ActionResult<ApiResponse<List<GitCommitDto>>>> GetHistory(
        [FromQuery] string path,
        [FromQuery] int maxCount = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var commits = await _vault.LogAsync(path, maxCount, cancellationToken);
            var dtos = commits.Select(c => new GitCommitDto
            {
                Hash = c.Hash,
                ShortHash = c.ShortHash,
                Message = c.Message,
                // Author defaults to empty — Extensions.FileVault GitCommit doesn't include Author
                Timestamp = c.Timestamp
            }).ToList();
            return Ok(ApiResponse<List<GitCommitDto>>.Ok(dtos));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<List<GitCommitDto>>.Fail(ex.Message));
        }
    }

    #endregion

    #region Search

    /// <summary>
    /// Searches indexed vault content with path-based scope filtering.
    /// </summary>
    /// <remarks>
    /// Path scope examples:
    /// - Empty: Search all indexed files
    /// - ["folder/"]: Search all files in folder (recursive)
    /// - ["folder/file.pdf"]: Search only in specific file
    /// - ["folder/a.pdf", "folder/sub/"]: Search in multiple paths
    /// </remarks>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<VaultSearchResponseDto>>> Search(
        [FromBody] VaultSearchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new VaultSearchOptions
            {
                PathScope = request.PathScope ?? [],
                TopK = request.TopK,
                MinScore = request.MinScore,
                IncludeContent = request.IncludeContent,
                IncludeMetadata = request.IncludeMetadata
            };

            var result = await _vault.SearchAsync(request.Query, options, cancellationToken);

            var response = new VaultSearchResponseDto
            {
                Query = result.Query,
                Items = result.Items.Select(i => new VaultSearchResultItemDto
                {
                    SourcePath = i.SourcePath,
                    FileName = i.FileName,
                    ChunkIndex = i.ChunkIndex,
                    Content = i.Content,
                    Score = i.Score,
                    Metadata = i.Metadata
                }).ToList(),
                TotalCount = result.TotalCount,
                SearchedPaths = result.SearchedPaths.ToList(),
                DocumentsSearched = result.DocumentsSearched,
                DurationMs = result.Duration.TotalMilliseconds,
                IsSuccess = result.IsSuccess,
                ErrorMessage = result.ErrorMessage
            };

            return Ok(ApiResponse<VaultSearchResponseDto>.Ok(response));
        }
        catch (Exception ex)
        {
            LogVaultSearchFailed(_logger, ex, request.Query);
            return BadRequest(ApiResponse<VaultSearchResponseDto>.Fail(ex.Message));
        }
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Error, Message = "Vault search failed for query: {Query}")]
    private static partial void LogVaultSearchFailed(ILogger logger, Exception? exception, string query);

    #endregion

    #region Helpers

    private static WatchedFolderDto MapToDto(WatchedFolder folder)
    {
        return new WatchedFolderDto
        {
            Id = folder.Id,
            Path = folder.Path,
            Name = folder.Name,
            IsRecursive = folder.IsRecursive,
            IncludePatterns = folder.IncludePatterns,
            ExcludePatterns = folder.ExcludePatterns,
            AutoMemorize = folder.AutoMemorize,
            Status = folder.Status.ToString(),
            ErrorMessage = folder.ErrorMessage,
            CreatedAt = folder.CreatedAt.DateTime,
            LastScannedAt = folder.LastScannedAt?.DateTime,
            CollectionId = null, // Extensions.FileVault doesn't have CollectionId
            TrackedFileCount = 0, // Not tracked in Extensions.FileVault WatchedFolder
            PathExists = folder.PathExists
        };
    }

    private static TrackedFileDto MapToTrackedFileDto(VaultEntry entry)
    {
        // Get file info from file system for size and modification time
        var fileInfo = new FileInfo(entry.SourcePath);

        return new TrackedFileDto
        {
            Id = entry.Id,
            SourcePath = entry.SourcePath,
            FileName = entry.FileName,
            FileExtension = Path.GetExtension(entry.SourcePath),
            FileSize = fileInfo.Exists ? fileInfo.Length : 0,
            ContentHash = entry.SourceContentHash?.Value,
            FileModifiedAt = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.MinValue,
            Status = entry.Stage.ToString(),
            Version = 1, // Extensions.FileVault uses git for versioning
            CreatedAt = entry.CreatedAt.DateTime,
            MemorizedAt = entry.Stage == ProcessingStage.Memorized ? entry.LastProcessedAt?.DateTime : null,
            LastSyncedAt = entry.LastProcessedAt?.DateTime,
            ErrorMessage = entry.LastError,
            WatchedFolderId = null, // Not tracked in Extensions.FileVault VaultEntry
            DocumentId = null, // Not directly available
            DocumentStatus = null,
            EffectiveStatus = entry.SyncStatus.ToString()
        };
    }

    #endregion
}

/// <summary>
/// Request to refresh a file's vault content.
/// </summary>
public class RefreshFileRequest
{
    public string SourcePath { get; set; } = string.Empty;
}

/// <summary>
/// DTO for change detection results.
/// </summary>
public class ChangeDetectionResultDto
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public long? FileSize { get; set; }
    public DateTimeOffset? FileModifiedAt { get; set; }
    public bool EntryExists { get; set; }
    public bool SourceExists { get; set; }
    public bool SourceChanged { get; set; }
    public bool VaultChanged { get; set; }
    public bool HasChanges { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
    public string? Stage { get; set; }
    public string? SyncStatus { get; set; }
    public int? ChunkCount { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// DTO for queue status.
/// </summary>
public class QueueStatusDto
{
    public int QueuedCount { get; set; }
    public int ProcessingCount { get; set; }
    public int CompletedCount { get; set; }
    public int FailedCount { get; set; }
    public bool IsPaused { get; set; }
    public DateTimeOffset? LastProcessedAt { get; set; }
}

/// <summary>
/// DTO for git commits.
/// </summary>
public class GitCommitDto
{
    public string Hash { get; set; } = string.Empty;
    public string ShortHash { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}
