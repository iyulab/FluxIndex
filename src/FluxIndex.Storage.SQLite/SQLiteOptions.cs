namespace FluxIndex.Storage.SQLite;

/// <summary>
/// SQLite 벡터 저장소 설정 옵션 (로컬 개발용)
/// </summary>
public class SQLiteOptions
{
    /// <summary>
    /// SQLite 데이터베이스 파일 경로
    /// 기본값: fluxindex.db
    /// </summary>
    public string DatabasePath { get; set; } = "fluxindex.db";

    /// <summary>
    /// 메모리 데이터베이스 사용 여부
    /// true일 경우 ":memory:" 사용 (테스트용)
    /// </summary>
    public bool UseInMemory { get; set; } = false;

    /// <summary>
    /// 중복 문서 허용 여부
    /// </summary>
    public bool AllowDuplicates { get; set; } = false;

    /// <summary>
    /// 데이터베이스 마이그레이션 자동 실행 여부
    /// </summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>
    /// 벡터 검색 시 기본 임계값
    /// </summary>
    public double DefaultSearchThreshold { get; set; } = 0.7;

    /// <summary>
    /// 하이브리드 검색 시 벡터 가중치 (0.0 ~ 1.0)
    /// </summary>
    public double DefaultVectorWeight { get; set; } = 0.5;

    /// <summary>
    /// 배치 작업 크기
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// 명령 타임아웃 (초)
    /// </summary>
    public int CommandTimeout { get; set; } = 30;

    /// <summary>
    /// 벡터 검색 시 메모리 캐시 사용 여부
    /// </summary>
    public bool EnableVectorCache { get; set; } = true;

    /// <summary>
    /// 벡터 캐시 크기 (문서 수)
    /// </summary>
    public int VectorCacheSize { get; set; } = 1000;

    // ============================================================
    // PRAGMA 성능 최적화 옵션
    // ============================================================

    /// <summary>
    /// 메모리 맵 크기 (바이트). 0이면 비활성화.
    /// 기본값: 256MB (268435456). 대용량 데이터 처리 시 512MB~1GB 권장.
    /// </summary>
    public long MmapSize { get; set; } = 268435456; // 256MB

    /// <summary>
    /// 페이지 크기 (바이트). 일반적으로 4096 또는 8192 권장.
    /// 벡터 데이터의 경우 4096이 효율적. 변경 시 새 DB 생성 필요.
    /// </summary>
    public int PageSize { get; set; } = 4096;

    /// <summary>
    /// 캐시 크기 (KB 단위, 음수로 지정). 기본값: 20MB (-20000).
    /// 읽기 성능에 직접적 영향. 가용 메모리에 따라 조정.
    /// </summary>
    public int CacheSize { get; set; } = -20000; // 20MB

    /// <summary>
    /// 동기화 모드 (OFF, NORMAL, FULL).
    /// NORMAL: 성능/안정성 균형 (기본값)
    /// OFF: 최대 성능 (전원 손실 시 데이터 손실 가능)
    /// FULL: 최대 안정성 (느림)
    /// </summary>
    public SynchronousMode Synchronous { get; set; } = SynchronousMode.Normal;

    /// <summary>
    /// 임시 저장소 위치 (FILE, MEMORY).
    /// MEMORY: 중간 결과를 메모리에 저장 (성능 향상, 메모리 사용 증가)
    /// </summary>
    public TempStoreMode TempStore { get; set; } = TempStoreMode.Memory;

    /// <summary>
    /// Busy timeout (밀리초). Lock 경합 시 재시도 대기 시간.
    /// 기본값: 5000ms
    /// </summary>
    public int BusyTimeout { get; set; } = 5000;

    /// <summary>
    /// Auto vacuum 모드 (NONE, FULL, INCREMENTAL).
    /// INCREMENTAL: 점진적 파일 크기 관리 (권장)
    /// </summary>
    public AutoVacuumMode AutoVacuum { get; set; } = AutoVacuumMode.Incremental;
    
    /// <summary>
    /// 연결 문자열 생성
    /// </summary>
    public string GetConnectionString()
    {
        if (UseInMemory)
        {
            // If DatabasePath contains additional parameters (e.g., ;Mode=Memory;Cache=Shared), use it as-is
            if (DatabasePath.Contains(';'))
                return DatabasePath;

            // Use a shared in-memory database to allow multiple DbContext instances
            // to access the same data. Without Cache=Shared, each connection gets its own database.
            // For test isolation, use unique DatabasePath values (e.g., "test_guid.db").
            var dbName = Path.GetFileNameWithoutExtension(DatabasePath);
            if (string.IsNullOrEmpty(dbName))
            {
                dbName = "fluxindex";
            }
            return $"Data Source=file:{dbName}?mode=memory&cache=shared";
        }

        return $"Data Source={DatabasePath}";
    }
}

/// <summary>
/// SQLite 동기화 모드
/// </summary>
public enum SynchronousMode
{
    /// <summary>최대 성능. 전원 손실 시 데이터 손실 가능</summary>
    Off = 0,
    /// <summary>성능과 안정성의 균형 (권장)</summary>
    Normal = 1,
    /// <summary>최대 안정성. 성능 저하</summary>
    Full = 2
}

/// <summary>
/// SQLite 임시 저장소 모드
/// </summary>
public enum TempStoreMode
{
    /// <summary>기본 위치 사용</summary>
    Default = 0,
    /// <summary>파일 시스템 사용</summary>
    File = 1,
    /// <summary>메모리 사용 (빠름)</summary>
    Memory = 2
}

/// <summary>
/// SQLite Auto Vacuum 모드
/// </summary>
public enum AutoVacuumMode
{
    /// <summary>Auto vacuum 비활성화</summary>
    None = 0,
    /// <summary>전체 vacuum</summary>
    Full = 1,
    /// <summary>점진적 vacuum (권장)</summary>
    Incremental = 2
}