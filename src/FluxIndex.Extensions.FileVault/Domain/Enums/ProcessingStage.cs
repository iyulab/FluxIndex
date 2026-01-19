namespace FluxIndex.Extensions.FileVault.Domain.Enums;

/// <summary>
/// Processing pipeline stages for a vault entry.
/// Simplified stages: Source → Extracted → Memorized
/// (Chunks are stored directly in DB, not on disk)
/// </summary>
public enum ProcessingStage
{
    /// <summary>
    /// Source file registered, no processing done yet.
    /// </summary>
    Source = 0,

    /// <summary>
    /// Content extracted and refined to vault/refined.md.
    /// </summary>
    Extracted = 1,

    /// <summary>
    /// Chunks embedded and indexed to FluxIndex (stored in DB).
    /// </summary>
    Memorized = 2
}
