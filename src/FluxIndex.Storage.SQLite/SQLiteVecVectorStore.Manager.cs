using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Application.Services.Base;
using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Exceptions;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Globalization;

namespace FluxIndex.Storage.SQLite;

public partial class SQLiteVecVectorStore
{

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectionInfo>> ListCollectionsAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = (Microsoft.Data.Sqlite.SqliteConnection)_context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await _extensionLoader.LoadExtensionAsync(connection, cancellationToken);

        var results = new List<CollectionInfo>();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'table' AND sql LIKE '%vec0%'";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(string Name, string Sql)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        foreach (var (name, sql) in rows)
        {
            var dimension = ParseDimensionFromCreateSql(sql);
            var count = await GetTableRowCountAsync(connection, name, cancellationToken);
            results.Add(new CollectionInfo(name, dimension, count, null));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<CollectionInfo?> GetCollectionInfoAsync(
        string collectionName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name must not be null or whitespace.", nameof(collectionName));

        var connection = (Microsoft.Data.Sqlite.SqliteConnection)_context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await _extensionLoader.LoadExtensionAsync(connection, cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'table' AND sql LIKE '%vec0%' AND name = @name";
        cmd.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@name", collectionName));

        string? foundSql = null;
        using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                foundSql = reader.GetString(1);
            }
        }

        if (foundSql is null)
            return null;

        var dimension = ParseDimensionFromCreateSql(foundSql);
        var count = await GetTableRowCountAsync(connection, collectionName, cancellationToken);
        return new CollectionInfo(collectionName, dimension, count, null);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteCollectionAsync(
        string collectionName,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidCollectionName(collectionName))
        {
            throw new ArgumentException(
                $"Invalid collection name '{collectionName}'. " +
                "Must start with 'chunk_embeddings_' followed by alphanumeric/underscore characters only.",
                nameof(collectionName));
        }

        var connection = (Microsoft.Data.Sqlite.SqliteConnection)_context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await _extensionLoader.LoadExtensionAsync(connection, cancellationToken);

        // Check existence
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";
        checkCmd.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@name", collectionName));
        var existsResult = await checkCmd.ExecuteScalarAsync(cancellationToken);
        if (Convert.ToInt32(existsResult, System.Globalization.CultureInfo.InvariantCulture) == 0)
            return false;

        // Drop table — name is validated above, safe to interpolate
        using var dropCmd = connection.CreateCommand();
        dropCmd.CommandText = $"DROP TABLE IF EXISTS \"{collectionName}\"";
        await dropCmd.ExecuteNonQueryAsync(cancellationToken);

        LogCollectionDeleted(_logger, collectionName);
        return true;
    }

    private static int ParseDimensionFromCreateSql(string sql)
    {
        var match = System.Text.RegularExpressions.Regex.Match(sql, @"float\[(\d+)\]");
        return match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
    }

    private static async Task<int> GetTableRowCountAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        if (!tableName.StartsWith("chunk_embeddings_", StringComparison.Ordinal))
            return 0;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{tableName}\"";
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsValidCollectionName(string name)
    {
        const string prefix = "chunk_embeddings_";
        return name.StartsWith(prefix, StringComparison.Ordinal)
            && name.Length > prefix.Length
            && name[prefix.Length..].All(c => char.IsLetterOrDigit(c) || c == '_');
    }

}
