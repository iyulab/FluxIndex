namespace FluxIndex.Extensions.FileVault.Domain.Enums;

/// <summary>
/// Processing pipeline stages for a vault entry.
/// Stages: Source → Extracted → Refined → Memorized (or Error on failure)
/// </summary>
public enum ProcessingStage
{
    /// <summary>
    /// Source file registered, no processing done yet.
    /// </summary>
    Source = 0,

    /// <summary>
    /// Raw content extracted to extracted.md (not git-tracked).
    /// </summary>
    Extracted = 1,

    /// <summary>
    /// Content refined with LLM processing and image descriptions to vault/refined.md.
    /// </summary>
    Refined = 2,

    /// <summary>
    /// Chunks embedded and indexed to FluxIndex (stored in DB).
    /// </summary>
    Memorized = 3,

    /// <summary>
    /// VaultEntry exists but vectors are missing or invalid (detected by integrity check).
    /// Requires re-memorization to restore search capability.
    /// </summary>
    Stale = 4,

    /// <summary>
    /// Processing failed. Check LastError for details. Can be retried via ResetToSource + re-queue.
    /// </summary>
    Error = 5
}
