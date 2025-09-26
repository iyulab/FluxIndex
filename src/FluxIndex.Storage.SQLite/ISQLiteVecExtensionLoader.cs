using Microsoft.Data.Sqlite;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// sqlite-vec 확장 로더 인터페이스
/// </summary>
public interface ISQLiteVecExtensionLoader
{
    /// <summary>
    /// sqlite-vec 확장을 연결에 로드
    /// </summary>
    /// <param name="connection">SQLite 연결</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>로드 성공 여부</returns>
    Task<bool> LoadExtensionAsync(SqliteConnection connection, CancellationToken cancellationToken = default);

    /// <summary>
    /// 확장이 로드되었는지 확인
    /// </summary>
    /// <param name="connection">SQLite 연결</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>로드 여부</returns>
    Task<bool> IsExtensionLoadedAsync(SqliteConnection connection, CancellationToken cancellationToken = default);

    /// <summary>
    /// 확장 파일 경로 반환
    /// </summary>
    /// <returns>확장 파일 경로</returns>
    string GetExtensionPath();

    /// <summary>
    /// 확장 파일이 존재하는지 확인
    /// </summary>
    /// <returns>파일 존재 여부</returns>
    bool ExtensionFileExists();

    /// <summary>
    /// vec0 가상 테이블 생성
    /// </summary>
    /// <param name="connection">SQLite 연결</param>
    /// <param name="tableName">테이블 이름</param>
    /// <param name="vectorDimension">벡터 차원</param>
    /// <param name="options">추가 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>생성 성공 여부</returns>
    Task<bool> CreateVecTableAsync(
        SqliteConnection connection,
        string tableName,
        int vectorDimension,
        string options = "metric=cosine",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 확장 버전 정보 반환
    /// </summary>
    /// <param name="connection">SQLite 연결</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>버전 문자열 (로드되지 않은 경우 null)</returns>
    Task<string?> GetExtensionVersionAsync(SqliteConnection connection, CancellationToken cancellationToken = default);
}