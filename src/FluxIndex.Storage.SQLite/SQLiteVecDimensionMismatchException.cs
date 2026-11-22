namespace FluxIndex.Storage.SQLite;

/// <summary>
/// The vec0 table of the bound embedding identity already exists at another vector width than
/// <see cref="SQLiteVecOptions.VectorDimension"/>, and it holds vectors.
/// </summary>
/// <remarks>
/// The table name is derived from the embedding fingerprint, which does not include the width, so the same
/// provider and model configured at a new width finds the old table. An empty table of another width is
/// recreated at the configured width (nothing is lost). A table that holds vectors is left as it is and this
/// exception is thrown instead of writing into it: every insert would fail, and recreating it would discard the
/// stored vectors. Re-index into a store configured at the new width, or configure the width the table was
/// made with.
/// </remarks>
public sealed class SQLiteVecDimensionMismatchException : InvalidOperationException
{
    /// <summary>The vec0 table whose width differs.</summary>
    public string TableName { get; }

    /// <summary>The width the existing table was created with.</summary>
    public int TableDimension { get; }

    /// <summary>The width the store is configured with.</summary>
    public int ConfiguredDimension { get; }

    /// <summary>The number of vectors the existing table holds.</summary>
    public long StoredVectorCount { get; }

    /// <summary>Creates the exception for a table that holds vectors at another width.</summary>
    /// <param name="tableName">The vec0 table whose width differs.</param>
    /// <param name="tableDimension">The width the existing table was created with.</param>
    /// <param name="configuredDimension">The width the store is configured with.</param>
    /// <param name="storedVectorCount">The number of vectors the existing table holds.</param>
    public SQLiteVecDimensionMismatchException(string tableName, int tableDimension, int configuredDimension, long storedVectorCount)
        : base($"The vec0 table '{tableName}' was created for {tableDimension}-dimensional vectors and holds {storedVectorCount} " +
               $"of them, but the store is configured with VectorDimension = {configuredDimension}. The table is kept as it is. " +
               $"Configure VectorDimension = {tableDimension}, or re-index into a store at the new width.")
    {
        TableName = tableName;
        TableDimension = tableDimension;
        ConfiguredDimension = configuredDimension;
        StoredVectorCount = storedVectorCount;
    }
}
