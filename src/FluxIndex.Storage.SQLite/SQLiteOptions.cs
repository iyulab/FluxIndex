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
    
    /// <summary>
    /// 연결 문자열 생성
    /// </summary>
    public string GetConnectionString()
    {
        if (UseInMemory)
            return "Data Source=:memory:";
        
        return $"Data Source={DatabasePath}";
    }
}