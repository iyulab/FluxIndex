using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;
using System.Globalization;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// sqlite-vec 확장 로더 구현
/// </summary>
public partial class SQLiteVecExtensionLoader : ISQLiteVecExtensionLoader
{
    private readonly ILogger<SQLiteVecExtensionLoader> _logger;
    private readonly SQLiteVecOptions _options;

    public SQLiteVecExtensionLoader(
        ILogger<SQLiteVecExtensionLoader> logger,
        IOptions<SQLiteVecOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<bool> LoadExtensionAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            // SQLite 확장 로딩 활성화
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }
            connection.EnableExtensions(true);

            // Strategy 1: 파일시스템 경로 탐색 (baseDir, native search dirs, NuGet cache)
            var extensionPath = GetExtensionPath();
            if (ExtensionFileExists())
            {
                connection.LoadExtension(extensionPath);
                LogExtensionLoaded(_logger, extensionPath);
            }
            // Strategy 2: .NET NativeLibrary 런타임 로더에 위임
            // (파일시스템 탐색으로 해결 안 되는 엣지 케이스 대응)
            else if (TryLoadViaRuntimeResolver(connection))
            {
                LogExtensionLoadedViaRuntime(_logger);
            }
            else
            {
                LogExtensionNotFound(_logger, extensionPath);

                if (_options.FallbackToInMemoryOnError)
                {
                    LogFallbackActivated(_logger);
                    return false;
                }

                throw new FileNotFoundException($"sqlite-vec 확장 파일을 찾을 수 없습니다: {extensionPath}");
            }

            // 로드 확인
            var isLoaded = await IsExtensionLoadedAsync(connection, cancellationToken);
            if (isLoaded)
            {
                var version = await GetExtensionVersionAsync(connection, cancellationToken);
                LogExtensionVersion(_logger, version ?? "unknown");
            }

            return isLoaded;
        }
        catch (Exception ex)
        {
            LogExtensionLoadFailed(_logger, ex, GetExtensionPath());

            if (_options.FallbackToInMemoryOnError)
            {
                LogFallbackActivated(_logger);
                return false;
            }

            throw;
        }
    }

    /// <summary>
    /// .NET NativeLibrary API를 사용하여 런타임 로더에 확장 로딩을 위임.
    /// NativeLibrary가 라이브러리를 프로세스에 로드한 후,
    /// SQLite의 OS 로더가 이미 로드된 모듈을 찾아 확장을 활성화한다.
    /// </summary>
    private static bool TryLoadViaRuntimeResolver(SqliteConnection connection)
    {
        try
        {
            var libraryName = SQLiteVecOptions.GetPlatformFileName();
            if (!NativeLibrary.TryLoad(libraryName, typeof(SQLiteVecExtensionLoader).Assembly, null, out _))
            {
                return false;
            }

            // 라이브러리가 프로세스에 로드됨 — SQLite의 LoadExtension에 확장자 없는 이름 전달.
            // sqlite3_load_extension은 OS 로더(LoadLibrary/dlopen)를 사용하며,
            // 이미 로드된 모듈을 발견하여 확장을 초기화한다.
            connection.LoadExtension(Path.GetFileNameWithoutExtension(libraryName));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsExtensionLoadedAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pragma_module_list WHERE name = 'vec0'";

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
        }
        catch
        {
            return false;
        }
    }

    public string GetExtensionPath()
    {
        return _options.GetDefaultExtensionPath();
    }

    public bool ExtensionFileExists()
    {
        // GetExtensionPath() now auto-detects available extension file
        var path = GetExtensionPath();
        return File.Exists(path);
    }

    /// <summary>
    /// 모든 가능한 확장 파일 경로에서 사용 가능한 파일이 있는지 확인
    /// </summary>
    public bool AnyExtensionFileExists()
    {
        // GetExtensionPath() will return the first found path, or default expected path
        var path = GetExtensionPath();
        return File.Exists(path);
    }

    public async Task<bool> CreateVecTableAsync(
        SqliteConnection connection,
        string tableName,
        int vectorDimension,
        string options = "distance_metric=cosine",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = $"CREATE VIRTUAL TABLE IF NOT EXISTS {tableName} USING vec0(chunk_id TEXT PRIMARY KEY, embedding float[{vectorDimension}] {options})";

            using var command = connection.CreateCommand();
            command.CommandText = sql;

            await command.ExecuteNonQueryAsync(cancellationToken);

            LogVecTableCreated(_logger, tableName, vectorDimension);
            return true;
        }
        catch (Exception ex)
        {
            LogVecTableCreateFailed(_logger, ex, tableName);
            return false;
        }
    }

    public async Task<bool> RecreateVecTableAsync(
        SqliteConnection connection,
        string tableName,
        int vectorDimension,
        string options = "distance_metric=cosine",
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try normal DROP first
            try
            {
                using var dropCmd = connection.CreateCommand();
                dropCmd.CommandText = $"DROP TABLE IF EXISTS {tableName}";
                await dropCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch
            {
                // DROP TABLE on corrupted vec0 fails because xDestroy cannot access
                // broken shadow tables. Force-remove by dropping shadow tables first,
                // then removing the virtual table entry from sqlite_master directly.
                await ForceDropVecTableAsync(connection, tableName, cancellationToken);
            }

            LogVecTableDropped(_logger, tableName);

            return await CreateVecTableAsync(connection, tableName, vectorDimension, options, cancellationToken);
        }
        catch (Exception ex)
        {
            LogVecTableRecreateFailed(_logger, ex, tableName);
            return false;
        }
    }

    /// <summary>
    /// Force-drops a corrupted vec0 virtual table by removing its shadow tables
    /// and sqlite_master entry directly. Used when normal DROP TABLE fails.
    /// </summary>
    private static async Task ForceDropVecTableAsync(
        SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        // Find all shadow tables (vec0 uses patterns like {name}_rowids, {name}_chunks, etc.)
        var shadowTables = new List<string>();
        using (var listCmd = connection.CreateCommand())
        {
            listCmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name LIKE '{tableName}_%'";
            using var reader = await listCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                shadowTables.Add(reader.GetString(0));
            }
        }

        // Drop each shadow table
        foreach (var shadow in shadowTables)
        {
            using var dropShadow = connection.CreateCommand();
            dropShadow.CommandText = $"DROP TABLE IF EXISTS \"{shadow}\"";
            await dropShadow.ExecuteNonQueryAsync(cancellationToken);
        }

        // Remove the virtual table entry itself
        // writable_schema allows modifying sqlite_master directly
        using var enableWritable = connection.CreateCommand();
        enableWritable.CommandText = "PRAGMA writable_schema = ON";
        await enableWritable.ExecuteNonQueryAsync(cancellationToken);

        using var deleteEntry = connection.CreateCommand();
        deleteEntry.CommandText = $"DELETE FROM sqlite_master WHERE type='table' AND name='{tableName}'";
        await deleteEntry.ExecuteNonQueryAsync(cancellationToken);

        using var disableWritable = connection.CreateCommand();
        disableWritable.CommandText = "PRAGMA writable_schema = OFF";
        await disableWritable.ExecuteNonQueryAsync(cancellationToken);

        // Rebuild internal schema cache
        using var integrityCmd = connection.CreateCommand();
        integrityCmd.CommandText = "PRAGMA integrity_check";
        await integrityCmd.ExecuteScalarAsync(cancellationToken);
    }

    public async Task<string?> GetExtensionVersionAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT vec_version()";

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result?.ToString();
        }
        catch
        {
            // 버전 함수가 없거나 확장이 로드되지 않은 경우
            return null;
        }
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Warning, Message = "sqlite-vec 확장 파일을 찾을 수 없습니다: {ExtensionPath}")]
    private static partial void LogExtensionNotFound(ILogger logger, string extensionPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "폴백 모드 활성화: in-memory 벡터 검색 사용")]
    private static partial void LogFallbackActivated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "sqlite-vec 확장이 성공적으로 로드되었습니다: {ExtensionPath}")]
    private static partial void LogExtensionLoaded(ILogger logger, string extensionPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "sqlite-vec 확장이 .NET NativeLibrary 런타임 로더를 통해 로드되었습니다")]
    private static partial void LogExtensionLoadedViaRuntime(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "sqlite-vec 확장 버전: {Version}")]
    private static partial void LogExtensionVersion(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Error, Message = "sqlite-vec 확장 로드 실패: {ExtensionPath}")]
    private static partial void LogExtensionLoadFailed(ILogger logger, Exception exception, string extensionPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "vec0 가상 테이블 생성됨: {TableName}, 차원: {Dimension}")]
    private static partial void LogVecTableCreated(ILogger logger, string tableName, int dimension);

    [LoggerMessage(Level = LogLevel.Error, Message = "vec0 테이블 생성 실패: {TableName}")]
    private static partial void LogVecTableCreateFailed(ILogger logger, Exception exception, string tableName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "vec0 테이블 삭제됨 (복구용): {TableName}")]
    private static partial void LogVecTableDropped(ILogger logger, string tableName);

    [LoggerMessage(Level = LogLevel.Error, Message = "vec0 테이블 재생성 실패: {TableName}")]
    private static partial void LogVecTableRecreateFailed(ILogger logger, Exception exception, string tableName);

    #endregion
}

/// <summary>
/// sqlite-vec 확장을 사용하지 않는 더미 로더 (폴백용)
/// </summary>
public partial class NoOpSQLiteVecExtensionLoader : ISQLiteVecExtensionLoader
{
    private readonly ILogger<NoOpSQLiteVecExtensionLoader> _logger;

    public NoOpSQLiteVecExtensionLoader(ILogger<NoOpSQLiteVecExtensionLoader> logger)
    {
        _logger = logger;
    }

    public Task<bool> LoadExtensionAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        LogNoOpExtensionSkipped(_logger);
        return Task.FromResult(false);
    }

    public Task<bool> IsExtensionLoadedAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public string GetExtensionPath()
    {
        return string.Empty;
    }

    public bool ExtensionFileExists()
    {
        return false;
    }

    public Task<bool> CreateVecTableAsync(
        SqliteConnection connection,
        string tableName,
        int vectorDimension,
        string options = "distance_metric=cosine",
        CancellationToken cancellationToken = default)
    {
        LogNoOpVecTableSkipped(_logger);
        return Task.FromResult(false);
    }

    public Task<bool> RecreateVecTableAsync(
        SqliteConnection connection,
        string tableName,
        int vectorDimension,
        string options = "distance_metric=cosine",
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<string?> GetExtensionVersionAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "sqlite-vec 확장을 사용하지 않음 (in-memory 벡터 검색 사용)")]
    private static partial void LogNoOpExtensionSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "vec0 테이블 생성 요청이 있었지만 sqlite-vec 확장을 사용하지 않음")]
    private static partial void LogNoOpVecTableSkipped(ILogger logger);

    #endregion
}