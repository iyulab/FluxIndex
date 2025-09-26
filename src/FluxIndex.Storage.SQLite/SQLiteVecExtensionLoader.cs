using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// sqlite-vec 확장 로더 구현
/// </summary>
public class SQLiteVecExtensionLoader : ISQLiteVecExtensionLoader
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
            var extensionPath = GetExtensionPath();

            if (!ExtensionFileExists())
            {
                _logger.LogWarning("sqlite-vec 확장 파일을 찾을 수 없습니다: {ExtensionPath}", extensionPath);

                if (_options.FallbackToInMemoryOnError)
                {
                    _logger.LogInformation("폴백 모드 활성화: in-memory 벡터 검색 사용");
                    return false;
                }

                throw new FileNotFoundException($"sqlite-vec 확장 파일을 찾을 수 없습니다: {extensionPath}");
            }

            // SQLite 확장 로딩 활성화
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            // Enable extension loading (Microsoft.Data.Sqlite specific method)
            connection.EnableExtensions(true);

            // Load the extension using connection method
            connection.LoadExtension(extensionPath);

            _logger.LogInformation("sqlite-vec 확장이 성공적으로 로드되었습니다: {ExtensionPath}", extensionPath);

            // 로드 확인
            var isLoaded = await IsExtensionLoadedAsync(connection, cancellationToken);
            if (isLoaded)
            {
                var version = await GetExtensionVersionAsync(connection, cancellationToken);
                _logger.LogInformation("sqlite-vec 확장 버전: {Version}", version ?? "unknown");
            }

            return isLoaded;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "sqlite-vec 확장 로드 실패: {ExtensionPath}", GetExtensionPath());

            if (_options.FallbackToInMemoryOnError)
            {
                _logger.LogInformation("폴백 모드 활성화: in-memory 벡터 검색 사용");
                return false;
            }

            throw;
        }
    }

    public async Task<bool> IsExtensionLoadedAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pragma_module_list WHERE name = 'vec0'";

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) > 0;
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
        var path = GetExtensionPath();
        return File.Exists(path);
    }

    public async Task<bool> CreateVecTableAsync(
        SqliteConnection connection,
        string tableName,
        int vectorDimension,
        string options = "metric=cosine",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = $"CREATE VIRTUAL TABLE IF NOT EXISTS {tableName} USING vec0(chunk_id TEXT PRIMARY KEY, embedding float[{vectorDimension}])";

            using var command = connection.CreateCommand();
            command.CommandText = sql;

            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("vec0 가상 테이블 생성됨: {TableName}, 차원: {Dimension}", tableName, vectorDimension);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "vec0 테이블 생성 실패: {TableName}", tableName);
            return false;
        }
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
}

/// <summary>
/// sqlite-vec 확장을 사용하지 않는 더미 로더 (폴백용)
/// </summary>
public class NoOpSQLiteVecExtensionLoader : ISQLiteVecExtensionLoader
{
    private readonly ILogger<NoOpSQLiteVecExtensionLoader> _logger;

    public NoOpSQLiteVecExtensionLoader(ILogger<NoOpSQLiteVecExtensionLoader> logger)
    {
        _logger = logger;
    }

    public Task<bool> LoadExtensionAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("sqlite-vec 확장을 사용하지 않음 (in-memory 벡터 검색 사용)");
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
        string options = "metric=cosine",
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("vec0 테이블 생성 요청이 있었지만 sqlite-vec 확장을 사용하지 않음");
        return Task.FromResult(false);
    }

    public Task<string?> GetExtensionVersionAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }
}