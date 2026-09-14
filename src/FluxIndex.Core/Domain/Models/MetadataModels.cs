using System;
using System.Collections.Generic;

namespace FluxIndex.Core.Domain.Models;

/// <summary>
/// 배치 처리 옵션
/// </summary>
public class BatchProcessingOptions
{
    /// <summary>
    /// 배치 크기
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// 최대 재시도 횟수
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 재시도 지연 시간
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 진행률 보고 활성화
    /// </summary>
    public bool ReportProgress { get; set; } = true;

    /// <summary>
    /// 오류 시 중단 여부
    /// </summary>
    public bool StopOnError { get; set; }
}

