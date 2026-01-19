namespace FluxIndex.Extensions.FileVault.Domain.Enums;

/// <summary>
/// Synchronization status for a vault entry.
/// Tracks the consistency state between source file, vault content, and vector store.
/// </summary>
public enum SyncStatus
{
    /// <summary>
    /// Source file, vault content, and vector store are all in sync.
    /// </summary>
    InSync = 0,

    /// <summary>
    /// Source file has been modified (content hash changed).
    /// Requires re-memorization to update vault and vector store.
    /// </summary>
    SourceModified = 1,

    /// <summary>
    /// Vault files have been modified (git status shows changes).
    /// Requires refresh to update vector store.
    /// </summary>
    VaultModified = 2,

    /// <summary>
    /// Source file has been deleted.
    /// Entry is pending removal from vault and vector store.
    /// </summary>
    SourceDeleted = 3,

    /// <summary>
    /// Removal operation is pending (queued for background processing).
    /// </summary>
    RemovalPending = 4,

    /// <summary>
    /// Removal partially completed (vector store cleared, storage pending).
    /// Used for recovery when removal is interrupted.
    /// </summary>
    RemovalPartial = 5,

    /// <summary>
    /// Entry is in an error state.
    /// Check LastError for details.
    /// </summary>
    Error = 6
}
