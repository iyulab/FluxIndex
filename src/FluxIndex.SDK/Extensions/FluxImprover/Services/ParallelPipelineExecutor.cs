using System.Collections.Concurrent;
using System.Threading.Channels;
using FluxImprover.Options;
using FluxImprover.Evaluation;
using FluxImprover.QAGeneration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using FluxIndexChunk = Flux.Abstractions.IEnrichedChunk;
using FluxImproverEnrichedChunk = FluxImprover.Models.ILlmEnrichedChunk;

namespace FluxIndex.SDK.Extensions.FluxImprover.Services;

/// <summary>
/// 병렬 파이프라인 실행기 - 대용량 청크 처리를 위한 최적화된 병렬 실행
/// </summary>
public sealed partial class ParallelPipelineExecutor
{
    private readonly ChunkEnrichmentServiceWrapper? _enrichmentService;
    private readonly QAGenerationService? _qaService;
    private readonly RAGEvaluationService? _evaluationService;
    private readonly ILogger<ParallelPipelineExecutor> _logger;

    /// <summary>
    /// 병렬 파이프라인 실행기를 초기화합니다.
    /// </summary>
    public ParallelPipelineExecutor(
        ChunkEnrichmentServiceWrapper? enrichmentService = null,
        QAGenerationService? qaService = null,
        RAGEvaluationService? evaluationService = null,
        ILogger<ParallelPipelineExecutor>? logger = null)
    {
        _enrichmentService = enrichmentService;
        _qaService = qaService;
        _evaluationService = evaluationService;
        _logger = logger ?? NullLogger<ParallelPipelineExecutor>.Instance;
    }

    /// <summary>
    /// 병렬로 여러 청크를 인리치먼트합니다.
    /// </summary>
    /// <param name="chunks">인리치먼트할 청크들</param>
    /// <param name="options">인리치먼트 옵션</param>
    /// <param name="parallelOptions">병렬 실행 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>인리치먼트된 청크들</returns>
    public async Task<IReadOnlyList<EnrichmentResult>> EnrichParallelAsync(
        IEnumerable<FluxIndexChunk> chunks,
        EnrichmentOptions? options = null,
        ParallelExecutionOptions? parallelOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (_enrichmentService == null)
            throw new InvalidOperationException("Enrichment service is not available.");

        parallelOptions ??= new ParallelExecutionOptions();
        var chunkList = chunks.ToList();
        var results = new ConcurrentBag<EnrichmentResult>();

        await Parallel.ForEachAsync(
            chunkList,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelOptions.MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (chunk, ct) =>
            {
                try
                {
                    var enriched = await _enrichmentService.EnrichAsync(chunk, options, ct);
                    results.Add(new EnrichmentResult
                    {
                        ChunkId = chunk.ChunkId,
                        EnrichedChunk = enriched,
                        Success = true
                    });
                }
                catch (Exception ex)
                {
                    LogFailedToEnrichChunk(_logger, chunk.ChunkId, ex);
                    results.Add(new EnrichmentResult
                    {
                        ChunkId = chunk.ChunkId,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            });

        return results.ToList();
    }

    /// <summary>
    /// 병렬로 여러 청크에서 QA 쌍을 생성합니다.
    /// </summary>
    /// <param name="chunks">QA 생성할 청크들</param>
    /// <param name="options">QA 생성 옵션</param>
    /// <param name="parallelOptions">병렬 실행 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>청크별 QA 쌍 결과</returns>
    public async Task<IReadOnlyList<QAGenerationResult>> GenerateQAParallelAsync(
        IEnumerable<FluxIndexChunk> chunks,
        QAGenerationOptions? options = null,
        ParallelExecutionOptions? parallelOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (_qaService == null)
            throw new InvalidOperationException("QA Generation service is not available.");

        parallelOptions ??= new ParallelExecutionOptions();
        var chunkList = chunks.ToList();
        var results = new ConcurrentBag<QAGenerationResult>();

        await Parallel.ForEachAsync(
            chunkList,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelOptions.MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (chunk, ct) =>
            {
                try
                {
                    var qaPairs = await _qaService.GenerateFromChunkAsync(chunk, options, ct);
                    results.Add(new QAGenerationResult
                    {
                        ChunkId = chunk.ChunkId,
                        SourceId = chunk.Source.SourceId,
                        QAPairs = qaPairs,
                        Success = true
                    });
                }
                catch (Exception ex)
                {
                    LogFailedToGenerateQA(_logger, chunk.ChunkId, ex);
                    results.Add(new QAGenerationResult
                    {
                        ChunkId = chunk.ChunkId,
                        SourceId = chunk.Source.SourceId,
                        QAPairs = Array.Empty<GeneratedQAPair>(),
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            });

        return results.ToList();
    }

    /// <summary>
    /// 스트리밍 방식으로 청크를 처리합니다 (메모리 효율적).
    /// </summary>
    /// <param name="chunks">처리할 청크들</param>
    /// <param name="options">파이프라인 옵션</param>
    /// <param name="parallelOptions">병렬 실행 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>스트리밍 파이프라인 결과</returns>
    public async IAsyncEnumerable<PipelineResult> ProcessStreamAsync(
        IEnumerable<FluxIndexChunk> chunks,
        PipelineOptions? options = null,
        ParallelExecutionOptions? parallelOptions = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new PipelineOptions();
        parallelOptions ??= new ParallelExecutionOptions();

        var channel = Channel.CreateBounded<PipelineResult>(
            new BoundedChannelOptions(parallelOptions.MaxDegreeOfParallelism * 2)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

        var producerTask = Task.Run(async () =>
        {
            try
            {
                await Parallel.ForEachAsync(
                    chunks,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = parallelOptions.MaxDegreeOfParallelism,
                        CancellationToken = cancellationToken
                    },
                    async (chunk, ct) =>
                    {
                        var result = await ProcessSingleChunkAsync(chunk, options, ct);
                        await channel.Writer.WriteAsync(result, ct);
                    });
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        await foreach (var result in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return result;
        }

        await producerTask;
    }

    /// <summary>
    /// 배치 단위로 청크를 처리합니다.
    /// </summary>
    /// <param name="chunks">처리할 청크들</param>
    /// <param name="batchSize">배치 크기</param>
    /// <param name="options">파이프라인 옵션</param>
    /// <param name="parallelOptions">병렬 실행 옵션</param>
    /// <param name="progressCallback">진행 상황 콜백</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>전체 배치 결과</returns>
    public async Task<BatchPipelineResult> ProcessBatchesAsync(
        IEnumerable<FluxIndexChunk> chunks,
        int batchSize = 10,
        PipelineOptions? options = null,
        ParallelExecutionOptions? parallelOptions = null,
        Action<BatchProgressInfo>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        var totalChunks = chunkList.Count;
        var batches = chunkList.Chunk(batchSize).ToList();
        var allResults = new List<PipelineResult>();
        var processedCount = 0;

        for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = batches[batchIndex];
            var batchResults = new List<PipelineResult>();

            await foreach (var result in ProcessStreamAsync(batch, options, parallelOptions, cancellationToken))
            {
                batchResults.Add(result);
                processedCount++;

                progressCallback?.Invoke(new BatchProgressInfo
                {
                    CurrentBatch = batchIndex + 1,
                    TotalBatches = batches.Count,
                    ProcessedInBatch = batchResults.Count,
                    BatchSize = batch.Length,
                    TotalProcessed = processedCount,
                    TotalChunks = totalChunks
                });
            }

            allResults.AddRange(batchResults);
        }

        return new BatchPipelineResult
        {
            Results = allResults,
            TotalChunks = totalChunks,
            SuccessfulChunks = allResults.Count(r => r.Success),
            FailedChunks = allResults.Count(r => !r.Success),
            TotalQAPairsGenerated = allResults.Sum(r => r.GeneratedQAPairs?.Count ?? 0),
            TotalQAPairsEvaluated = allResults.Sum(r => r.EvaluatedQAPairs?.Count ?? 0)
        };
    }

    private async Task<PipelineResult> ProcessSingleChunkAsync(
        FluxIndexChunk chunk,
        PipelineOptions options,
        CancellationToken cancellationToken)
    {
        var result = new PipelineResult
        {
            ChunkId = chunk.ChunkId,
            SourceId = chunk.Source.SourceId
        };

        try
        {
            // Step 1: Enrichment
            if (options.EnableEnrichment && _enrichmentService != null)
            {
                result.EnrichedChunk = await _enrichmentService.EnrichAsync(
                    chunk, options.EnrichmentOptions, cancellationToken);
                result.EnrichmentCompleted = true;
            }

            // Step 2: QA Generation
            if (options.EnableQAGeneration && _qaService != null)
            {
                result.GeneratedQAPairs = await _qaService.GenerateFromChunkAsync(
                    chunk, options.QAGenerationOptions, cancellationToken);
                result.QAGenerationCompleted = true;
            }

            // Step 3: Evaluation
            if (options.EnableEvaluation && _evaluationService != null && result.GeneratedQAPairs?.Count > 0)
            {
                var evaluations = new List<QAPairWithEvaluation>();
                foreach (var qa in result.GeneratedQAPairs)
                {
                    var evaluation = await _evaluationService.EvaluateAsync(
                        chunk.Content, qa.Question, qa.Answer,
                        options.EvaluationOptions, cancellationToken);

                    evaluations.Add(new QAPairWithEvaluation
                    {
                        Question = qa.Question,
                        Answer = qa.Answer,
                        Context = qa.Context ?? string.Empty,
                        Evaluation = evaluation
                    });
                }
                result.EvaluatedQAPairs = evaluations;
                result.EvaluationCompleted = true;
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            LogFailedToProcessChunk(_logger, chunk.ChunkId, ex);
        }

        return result;
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to enrich chunk {ChunkId}")]
    private static partial void LogFailedToEnrichChunk(ILogger logger, string chunkId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to generate QA for chunk {ChunkId}")]
    private static partial void LogFailedToGenerateQA(ILogger logger, string chunkId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to process chunk {ChunkId}")]
    private static partial void LogFailedToProcessChunk(ILogger logger, string chunkId, Exception exception);

    #endregion
}

/// <summary>
/// 병렬 실행 옵션
/// </summary>
public sealed class ParallelExecutionOptions
{
    /// <summary>최대 병렬 실행 수 (기본값: 프로세서 수)</summary>
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
}

/// <summary>
/// 인리치먼트 결과
/// </summary>
public sealed class EnrichmentResult
{
    /// <summary>청크 ID</summary>
    public required string ChunkId { get; init; }

    /// <summary>인리치먼트된 청크</summary>
    public FluxImproverEnrichedChunk? EnrichedChunk { get; set; }

    /// <summary>성공 여부</summary>
    public bool Success { get; set; }

    /// <summary>오류 메시지</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// QA 생성 결과
/// </summary>
public sealed class QAGenerationResult
{
    /// <summary>청크 ID</summary>
    public required string ChunkId { get; init; }

    /// <summary>소스 문서 ID</summary>
    public required string SourceId { get; init; }

    /// <summary>생성된 QA 쌍</summary>
    public required IReadOnlyList<GeneratedQAPair> QAPairs { get; init; }

    /// <summary>성공 여부</summary>
    public bool Success { get; set; }

    /// <summary>오류 메시지</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 배치 진행 상황 정보
/// </summary>
public sealed class BatchProgressInfo
{
    /// <summary>현재 배치 번호</summary>
    public int CurrentBatch { get; init; }

    /// <summary>총 배치 수</summary>
    public int TotalBatches { get; init; }

    /// <summary>현재 배치에서 처리된 수</summary>
    public int ProcessedInBatch { get; init; }

    /// <summary>배치 크기</summary>
    public int BatchSize { get; init; }

    /// <summary>총 처리된 수</summary>
    public int TotalProcessed { get; init; }

    /// <summary>총 청크 수</summary>
    public int TotalChunks { get; init; }

    /// <summary>전체 진행률</summary>
    public double OverallProgress => TotalChunks > 0 ? (double)TotalProcessed / TotalChunks : 0;
}
