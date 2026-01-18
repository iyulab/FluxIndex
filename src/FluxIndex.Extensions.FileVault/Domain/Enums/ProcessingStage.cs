namespace FluxIndex.Extensions.FileVault.Domain.Enums;

/// <summary>
/// Processing pipeline stages for a vault entry.
/// </summary>
public enum ProcessingStage
{
    /// <summary>
    /// Source file registered, no processing done yet.
    /// </summary>
    Source = 0,

    /// <summary>
    /// Content extracted from source file.
    /// </summary>
    Extracted = 1,

    /// <summary>
    /// Content refined (auto or manually edited).
    /// </summary>
    Refined = 2,

    /// <summary>
    /// Content chunked into segments.
    /// </summary>
    Chunked = 3,

    /// <summary>
    /// Chunks memorized (indexed to FluxIndex).
    /// </summary>
    Memorized = 4
}
