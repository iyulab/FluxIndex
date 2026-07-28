using FluxIndex.Integrations.FluxImprover.Adapters;
using FluxImprover.Options;
using FluxImprover.QAGeneration;
using FluxIndexChunk = Flux.Abstractions.IEnrichedChunk;

namespace FluxIndex.Integrations.FluxImprover.Services;

/// <summary>
/// QA (Question-Answer) 생성 서비스 - FluxIndex 청크에서 Q&amp;A 쌍을 자동 생성합니다.
/// FluxImprover의 QAGeneratorService와 QAFilterService를 래핑합니다.
/// </summary>
public sealed class QAGenerationService
{
    private readonly QAGeneratorService _generatorService;
    private readonly QAFilterService _filterService;
    private readonly QAPipeline _pipeline;

    /// <summary>
    /// QA 생성 서비스를 초기화합니다.
    /// </summary>
    /// <param name="generatorService">QA 생성기 서비스</param>
    /// <param name="filterService">QA 필터링 서비스</param>
    /// <param name="pipeline">QA 파이프라인</param>
    public QAGenerationService(
        QAGeneratorService generatorService,
        QAFilterService filterService,
        QAPipeline pipeline)
    {
        _generatorService = generatorService ?? throw new ArgumentNullException(nameof(generatorService));
        _filterService = filterService ?? throw new ArgumentNullException(nameof(filterService));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <summary>
    /// FluxIndex 청크에서 QA 쌍을 생성합니다.
    /// </summary>
    /// <param name="chunk">원본 FluxIndex 청크</param>
    /// <param name="options">생성 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>생성된 QA 쌍 목록</returns>
    public async Task<IReadOnlyList<GeneratedQAPair>> GenerateFromChunkAsync(
        FluxIndexChunk chunk,
        QAGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var adapter = new EnrichedChunkAdapter(chunk);
        var sourceId = adapter.SourceId;

        return await _generatorService.GenerateAsync(
            adapter.Text,
            options,
            sourceId,
            cancellationToken);
    }

    /// <summary>
    /// 여러 FluxIndex 청크에서 QA 쌍을 일괄 생성합니다.
    /// </summary>
    /// <param name="chunks">원본 FluxIndex 청크 목록</param>
    /// <param name="options">생성 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>청크별 생성된 QA 쌍 목록</returns>
    public async Task<IReadOnlyList<ChunkQAPairs>> GenerateFromChunksAsync(
        IEnumerable<FluxIndexChunk> chunks,
        QAGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ChunkQAPairs>();

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var qaPairs = await GenerateFromChunkAsync(chunk, options, cancellationToken);
            results.Add(new ChunkQAPairs
            {
                ChunkId = chunk.ChunkId,
                SourceId = chunk.Source.SourceId,
                QAPairs = qaPairs
            });
        }

        return results;
    }

    /// <summary>
    /// 생성된 QA 쌍을 품질 기준으로 필터링합니다.
    /// </summary>
    /// <param name="pairs">필터링할 QA 쌍 목록</param>
    /// <param name="options">필터링 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>필터링된 QA 쌍 목록</returns>
    public async Task<IReadOnlyList<GeneratedQAPair>> FilterAsync(
        IReadOnlyList<GeneratedQAPair> pairs,
        QAFilterOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        return await _filterService.FilterAsync(pairs, options, cancellationToken);
    }

    /// <summary>
    /// FluxIndex 청크에서 QA 쌍을 생성하고 필터링하는 전체 파이프라인을 실행합니다.
    /// </summary>
    /// <param name="chunk">원본 FluxIndex 청크</param>
    /// <param name="options">파이프라인 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>파이프라인 실행 결과</returns>
    public async Task<QAPipelineResult> ExecutePipelineAsync(
        FluxIndexChunk chunk,
        QAPipelineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var adapter = new EnrichedChunkAdapter(chunk);
        return await _pipeline.ExecuteAsync(adapter.Text, options, cancellationToken);
    }

    /// <summary>
    /// 여러 청크에서 QA 파이프라인을 일괄 실행합니다.
    /// </summary>
    /// <param name="chunks">원본 FluxIndex 청크 목록</param>
    /// <param name="options">파이프라인 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>청크별 파이프라인 결과</returns>
    public async Task<IReadOnlyList<ChunkQAPipelineResult>> ExecutePipelineBatchAsync(
        IEnumerable<FluxIndexChunk> chunks,
        QAPipelineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ChunkQAPipelineResult>();

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pipelineResult = await ExecutePipelineAsync(chunk, options, cancellationToken);
            results.Add(new ChunkQAPipelineResult
            {
                ChunkId = chunk.ChunkId,
                SourceId = chunk.Source.SourceId,
                Result = pipelineResult
            });
        }

        return results;
    }

    /// <summary>
    /// 문서 전체에서 고품질 QA 데이터셋을 생성합니다.
    /// </summary>
    /// <param name="chunks">문서의 모든 청크</param>
    /// <param name="options">파이프라인 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>문서 전체 QA 데이터셋</returns>
    public async Task<DocumentQADataset> GenerateDatasetAsync(
        IEnumerable<FluxIndexChunk> chunks,
        QAPipelineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var chunkResults = await ExecutePipelineBatchAsync(chunks, options, cancellationToken);

        var allPairs = chunkResults
            .SelectMany(cr => cr.Result.QAPairs.Select(qa => new DatasetQAPair
            {
                ChunkId = cr.ChunkId,
                SourceId = cr.SourceId,
                Question = qa.Question,
                Answer = qa.Answer,
                Context = qa.Context ?? string.Empty,
                Evaluation = qa.Evaluation
            }))
            .ToList();

        var totalGenerated = chunkResults.Sum(cr => cr.Result.GeneratedCount);
        var totalFiltered = chunkResults.Sum(cr => cr.Result.FilteredCount);

        return new DocumentQADataset
        {
            QAPairs = allPairs,
            TotalGenerated = totalGenerated,
            TotalFiltered = totalFiltered,
            ChunkCount = chunkResults.Count
        };
    }
}

/// <summary>
/// 청크별 QA 쌍 결과
/// </summary>
public sealed class ChunkQAPairs
{
    /// <summary>
    /// 청크 ID
    /// </summary>
    public required string ChunkId { get; init; }

    /// <summary>
    /// 소스 문서 ID
    /// </summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// 생성된 QA 쌍 목록
    /// </summary>
    public required IReadOnlyList<GeneratedQAPair> QAPairs { get; init; }
}

/// <summary>
/// 청크별 QA 파이프라인 결과
/// </summary>
public sealed class ChunkQAPipelineResult
{
    /// <summary>
    /// 청크 ID
    /// </summary>
    public required string ChunkId { get; init; }

    /// <summary>
    /// 소스 문서 ID
    /// </summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// 파이프라인 실행 결과
    /// </summary>
    public required QAPipelineResult Result { get; init; }
}

/// <summary>
/// 문서 전체 QA 데이터셋
/// </summary>
public sealed class DocumentQADataset
{
    /// <summary>
    /// 모든 QA 쌍
    /// </summary>
    public required IReadOnlyList<DatasetQAPair> QAPairs { get; init; }

    /// <summary>
    /// 총 생성된 QA 쌍 수
    /// </summary>
    public int TotalGenerated { get; init; }

    /// <summary>
    /// 필터링 후 남은 QA 쌍 수
    /// </summary>
    public int TotalFiltered { get; init; }

    /// <summary>
    /// 처리된 청크 수
    /// </summary>
    public int ChunkCount { get; init; }

    /// <summary>
    /// 필터링 통과율
    /// </summary>
    public double PassRate => TotalGenerated > 0 ? (double)TotalFiltered / TotalGenerated : 0;
}

/// <summary>
/// 데이터셋용 QA 쌍 (청크 정보 포함)
/// </summary>
public sealed class DatasetQAPair
{
    /// <summary>
    /// 청크 ID
    /// </summary>
    public required string ChunkId { get; init; }

    /// <summary>
    /// 소스 문서 ID
    /// </summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// 질문
    /// </summary>
    public required string Question { get; init; }

    /// <summary>
    /// 답변
    /// </summary>
    public required string Answer { get; init; }

    /// <summary>
    /// 컨텍스트
    /// </summary>
    public required string Context { get; init; }

    /// <summary>
    /// 품질 평가 결과
    /// </summary>
    public QAPairEvaluation? Evaluation { get; init; }
}
