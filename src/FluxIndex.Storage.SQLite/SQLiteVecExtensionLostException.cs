namespace FluxIndex.Storage.SQLite;

/// <summary>
/// The sqlite-vec extension was loaded by this store and a later connection cannot load it again - the native file was
/// removed or replaced (a temporary-file cleanup, an antivirus quarantine, an update that swaps files).
/// </summary>
/// <remarks>
/// <see cref="SQLiteVecOptions.FallbackToInMemoryOnError"/> covers a platform that never had the extension: the store runs
/// without persistent vectors from its first operation. It does not cover this case. Once the store has used the
/// extension, the vectors are on disk, and switching to the in-memory store would split them from what the caller writes
/// and reads next (a search would miss everything on disk, a write would be lost at exit while reporting success).
/// The operation fails instead, naming the file, so the caller can surface it or restart.
/// </remarks>
public sealed class SQLiteVecExtensionLostException : InvalidOperationException
{
    /// <summary>The file the extension was loaded from, when the store loaded it.</summary>
    public string ExtensionPath { get; }

    /// <summary>Creates the exception for the extension file the store loaded earlier.</summary>
    /// <param name="extensionPath">The file the extension was loaded from.</param>
    /// <param name="innerException">The failure of the later load, when there was one.</param>
    public SQLiteVecExtensionLostException(string extensionPath, Exception? innerException = null)
        : base(FormatMessage(extensionPath), innerException)
    {
        ExtensionPath = extensionPath;
    }

    private static string FormatMessage(string extensionPath)
        => $"The sqlite-vec extension was loaded from '{extensionPath}' but cannot be loaded again: the file is missing or " +
           "unusable. The store holds vectors on disk, so it does not switch to in-memory search. Restore the file " +
           "(or set SQLiteVecOptions.CustomExtensionPath to a location that is not cleaned up) and retry.";
}
