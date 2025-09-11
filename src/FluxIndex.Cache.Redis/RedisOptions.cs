namespace FluxIndex.Cache.Redis;

/// <summary>
/// Redis 캐시 서비스 설정 옵션
/// </summary>
public class RedisOptions
{
    /// <summary>
    /// Redis 연결 문자열
    /// 예: localhost:6379, redis.example.com:6380,password=secret
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";
    
    /// <summary>
    /// Redis 데이터베이스 번호 (0-15)
    /// </summary>
    public int Database { get; set; } = 0;
    
    /// <summary>
    /// 캐시 키 접두사
    /// </summary>
    public string KeyPrefix { get; set; } = "fluxindex";
    
    /// <summary>
    /// 기본 TTL (초)
    /// </summary>
    public int DefaultTtlSeconds { get; set; } = 3600; // 1시간
    
    /// <summary>
    /// 검색 결과 캐시 TTL (초)
    /// </summary>
    public int SearchResultsTtlSeconds { get; set; } = 300; // 5분
    
    /// <summary>
    /// 임베딩 캐시 TTL (초)
    /// </summary>
    public int EmbeddingTtlSeconds { get; set; } = 86400; // 24시간
    
    /// <summary>
    /// 연결 타임아웃 (밀리초)
    /// </summary>
    public int ConnectTimeout { get; set; } = 5000;
    
    /// <summary>
    /// 동기 타임아웃 (밀리초)
    /// </summary>
    public int SyncTimeout { get; set; } = 5000;
    
    /// <summary>
    /// 비동기 타임아웃 (밀리초)
    /// </summary>
    public int AsyncTimeout { get; set; } = 5000;
    
    /// <summary>
    /// Keep Alive 간격 (초)
    /// </summary>
    public int KeepAlive { get; set; } = 60;
    
    /// <summary>
    /// 연결 재시도 횟수
    /// </summary>
    public int ConnectRetry { get; set; } = 3;
    
    /// <summary>
    /// SSL 사용 여부
    /// </summary>
    public bool UseSsl { get; set; } = false;
    
    /// <summary>
    /// 명령 실패 시 재연결 허용
    /// </summary>
    public bool AbortOnConnectFail { get; set; } = false;
    
    /// <summary>
    /// 압축 사용 여부 (큰 데이터에 유용)
    /// </summary>
    public bool UseCompression { get; set; } = false;
    
    /// <summary>
    /// 압축 임계값 (바이트)
    /// </summary>
    public int CompressionThreshold { get; set; } = 2048;
}