namespace FluxIndex.Storage.PostgreSQL.Graph;

/// <summary>
/// PostgreSQL Graph 저장소 옵션
/// </summary>
public class PostgresGraphOptions
{
    /// <summary>
    /// PostgreSQL 연결 문자열
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 시작 시 자동 마이그레이션 (기본: true)
    /// </summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>
    /// 명령 타임아웃 (초, 기본: 30)
    /// </summary>
    public int CommandTimeout { get; set; } = 30;

    /// <summary>
    /// 재귀 CTE 최대 깊이 (기본: 100)
    /// </summary>
    public int MaxRecursionDepth { get; set; } = 100;

    /// <summary>
    /// JSONB 인덱스 사용 (GIN 인덱스, 기본: true)
    /// </summary>
    public bool UseJsonbIndex { get; set; } = true;
}
