namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL 벡터 저장소 설정 옵션
/// </summary>
public class PostgreSQLOptions
{
    /// <summary>
    /// PostgreSQL 연결 문자열
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
    
    /// <summary>
    /// 벡터 차원 (기본값: 1536 for OpenAI text-embedding-3-small)
    /// </summary>
    public int VectorDimension { get; set; } = 1536;
    
    /// <summary>
    /// HNSW 인덱스 M 파라미터 (기본값: 16)
    /// </summary>
    public int HnswM { get; set; } = 16;
    
    /// <summary>
    /// HNSW 인덱스 ef_construction 파라미터 (기본값: 64)
    /// </summary>
    public int HnswEfConstruction { get; set; } = 64;
    
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
    /// 연결 풀 최대 크기
    /// </summary>
    public int MaxPoolSize { get; set; } = 100;
    
    /// <summary>
    /// 명령 타임아웃 (초)
    /// </summary>
    public int CommandTimeout { get; set; } = 30;
}