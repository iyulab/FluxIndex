using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Vault;
using FluxIndex.Stack.Vault.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FluxIndex.Stack.Api.Controllers;

/// <summary>
/// API controller for vault operations (file system synchronization).
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class VaultController : ControllerBase
{
    private readonly IVaultService _vaultService;
    private readonly ILogger<VaultController> _logger;

    public VaultController(
        IVaultService vaultService,
        ILogger<VaultController> logger)
    {
        _vaultService = vaultService;
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
        var folders = await _vaultService.GetAllWatchedFoldersAsync(cancellationToken);
        var dtos = folders.Select(f => new WatchedFolderDto
        {
            Id = f.Id,
            Path = f.Path,
            Name = f.Name,
            IsRecursive = f.IsRecursive,
            IncludePatterns = f.IncludePatterns,
            ExcludePatterns = f.ExcludePatterns,
            AutoMemorize = f.AutoMemorize,
            Status = f.Status.ToString(),
            ErrorMessage = f.ErrorMessage,
            CreatedAt = f.CreatedAt,
            LastScannedAt = f.LastScannedAt,
            CollectionId = f.CollectionId,
            TrackedFileCount = f.TrackedFiles?.Count ?? 0
        }).ToList();

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
        var folder = await _vaultService.GetWatchedFolderAsync(id, cancellationToken);
        if (folder == null)
        {
            return NotFound(ApiResponse<WatchedFolderDto>.Fail($"Folder with id '{id}' not found."));
        }

        var dto = new WatchedFolderDto
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
            CreatedAt = folder.CreatedAt,
            LastScannedAt = folder.LastScannedAt,
            CollectionId = folder.CollectionId,
            TrackedFileCount = folder.TrackedFiles?.Count ?? 0
        };

        return Ok(ApiResponse<WatchedFolderDto>.Ok(dto));
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
            var folder = await _vaultService.AddWatchedFolderAsync(
                request.Path,
                request.Name,
                request.IsRecursive,
                request.AutoMemorize,
                request.IncludePatterns,
                request.ExcludePatterns,
                request.CollectionId,
                cancellationToken);

            var dto = new WatchedFolderDto
            {
                Id = folder.Id,
                Path = folder.Path,
                Name = folder.Name,
                IsRecursive = folder.IsRecursive,
                IncludePatterns = folder.IncludePatterns,
                ExcludePatterns = folder.ExcludePatterns,
                AutoMemorize = folder.AutoMemorize,
                Status = folder.Status.ToString(),
                CreatedAt = folder.CreatedAt,
                CollectionId = folder.CollectionId
            };

            return CreatedAtAction(nameof(GetFolder), new { id = folder.Id },
                ApiResponse<WatchedFolderDto>.Ok(dto));
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
            await _vaultService.RemoveWatchedFolderAsync(id, removeTrackedFiles, cancellationToken);
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
            var result = await _vaultService.ScanFolderAsync(id, cancellationToken);
            var dto = new ScanResultDto
            {
                FolderId = result.FolderId,
                TotalFilesFound = result.TotalFilesFound,
                NewFilesQueued = result.NewFilesQueued,
                ChangedFilesQueued = result.ChangedFilesQueued,
                OrphanedFilesDetected = result.OrphanedFilesDetected,
                SkippedFiles = result.SkippedFiles,
                Errors = result.Errors,
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
            await _vaultService.PauseWatchingAsync(id, cancellationToken);
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
            await _vaultService.ResumeWatchingAsync(id, cancellationToken);
            return Ok(ApiResponse<bool>.Ok(true));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<bool>.Fail(ex.Message));
        }
    }

    #endregion

    #region File Endpoints

    /// <summary>
    /// Gets a tracked file by ID.
    /// </summary>
    [HttpGet("files/{id:guid}")]
    public async Task<ActionResult<ApiResponse<TrackedFileDto>>> GetFile(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var file = await _vaultService.GetTrackedFileAsync(id, cancellationToken);
        if (file == null)
        {
            return NotFound(ApiResponse<TrackedFileDto>.Fail($"File with id '{id}' not found."));
        }

        var dto = MapToDto(file);
        return Ok(ApiResponse<TrackedFileDto>.Ok(dto));
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
            var file = await _vaultService.MemorizeFileAsync(
                request.SourcePath,
                request.WatchedFolderId,
                cancellationToken);

            var dto = MapToDto(file);
            return CreatedAtAction(nameof(GetFile), new { id = file.Id },
                ApiResponse<TrackedFileDto>.Ok(dto));
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
    [HttpDelete("files/{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> UnmemorizeFile(
        Guid id,
        [FromQuery] bool deleteArtifacts = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _vaultService.UnmemorizeFileAsync(id, deleteArtifacts, cancellationToken);
            return Ok(ApiResponse<bool>.Ok(true));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<bool>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Reprocesses a file.
    /// </summary>
    [HttpPost("files/{id:guid}/reprocess")]
    public async Task<ActionResult<ApiResponse<TrackedFileDto>>> ReprocessFile(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var file = await _vaultService.ReprocessFileAsync(id, cancellationToken);
            var dto = MapToDto(file);
            return Ok(ApiResponse<TrackedFileDto>.Ok(dto));
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

    #endregion

    #region Sync & Status Endpoints

    /// <summary>
    /// Gets the current vault status.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<VaultStatusDto>>> GetStatus(
        CancellationToken cancellationToken = default)
    {
        var status = await _vaultService.GetStatusAsync(cancellationToken);
        var dto = new VaultStatusDto
        {
            IsEnabled = status.IsEnabled,
            ActiveWatchers = status.ActiveWatchers,
            TotalTrackedFiles = status.TotalTrackedFiles,
            MemorizedFiles = status.MemorizedFiles,
            QueuedFiles = status.QueuedFiles,
            ProcessingFiles = status.ProcessingFiles,
            StaleFiles = status.StaleFiles,
            OrphanedFiles = status.OrphanedFiles,
            ErrorFiles = status.ErrorFiles,
            LastSyncAt = status.LastSyncAt
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
        var result = await _vaultService.SyncAllAsync(cancellationToken);
        var dto = new SyncResultDto
        {
            FoldersScanned = result.FoldersScanned,
            FilesProcessed = result.FilesProcessed,
            FilesQueued = result.FilesQueued,
            OrphanedFilesCleaned = result.OrphanedFilesCleaned,
            Errors = result.Errors,
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
        var count = await _vaultService.CleanupOrphanedFilesAsync(cancellationToken);
        return Ok(ApiResponse<int>.Ok(count));
    }

    #endregion

    #region Helpers

    private static TrackedFileDto MapToDto(Vault.Entities.TrackedFile file)
    {
        return new TrackedFileDto
        {
            Id = file.Id,
            SourcePath = file.SourcePath,
            FileName = file.FileName,
            FileExtension = file.FileExtension,
            FileSize = file.FileSize,
            ContentHash = file.ContentHash,
            FileModifiedAt = file.FileModifiedAt,
            Status = file.Status.ToString(),
            Version = file.Version,
            CreatedAt = file.CreatedAt,
            MemorizedAt = file.MemorizedAt,
            LastSyncedAt = file.LastSyncedAt,
            ErrorMessage = file.ErrorMessage,
            WatchedFolderId = file.WatchedFolderId,
            DocumentId = file.DocumentId
        };
    }

    #endregion
}
