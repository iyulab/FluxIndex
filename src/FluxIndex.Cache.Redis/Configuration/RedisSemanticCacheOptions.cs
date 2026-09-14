using System;
using System.Collections.Generic;

namespace FluxIndex.Cache.Redis.Configuration;

/// <summary>
/// Redis 시맨틱 캐시 구성 옵션
/// </summary>
public class RedisSemanticCacheOptions
{
    /// <summary>
    /// Redis 연결 문자열
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// 기본 캐시 TTL (생존 시간)
    /// </summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// 최대 캐시 엔트리 수 (0은 무제한)
    /// </summary>
    public long MaxCacheEntries { get; set; } = 10000;

    /// <summary>
    /// 병렬 처리 시 최대 동시 작업 수
    /// </summary>
    public int MaxParallelism { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Redis 데이터베이스 번호
    /// </summary>
    public int DatabaseNumber { get; set; }

}