using FluxImprover.Options;
using FluxImprover.Evaluation;
using FluxImprover.QAGeneration;
using FluxIndexChunk = FluxIndex.Core.Application.Interfaces.IEnrichedChunk;
using FluxImproverEnrichedChunk = FluxImprover.Models.IEnrichedChunk;

namespace FluxIndex.Extensions.FluxImprover.Services;

/// <summary>
/// FluxImprover 통합 파이프라인 - 청크 처리의 전체 흐름을 오케스트레이션합니다.
/// Enrichment → QA Generation → Evaluation의 전체 워크플로우를 자동화합니다.
/// </summary>
public sealed class FluxImproverPipeline
{
    private readonly ChunkEnrichmentServiceWrapper? _enrichmentService;
    private readonly QAGenerationService? _qaService;
    private readonly RAGEvaluationService? _evaluationService;

    /// <summary>
    /// 파이프라인을 초기화합니다.
    /// </summary>
    /// <param name="enrichmentService">청크 인리치먼트 서비스 (선택적)</param>
    /// <param name="qaService">QA 생성 서비스 (선택적)</param>
    /// <param name="evaluationService">RAG 평가 서비스 (선택적)</param>
    public FluxImproverPipeline(
        ChunkEnrichmentServiceWrapper? enrichmentService = null,
        QAGenerationService? qaService = null,
        RAGEvaluationService? evaluationService = null)
    {
        _enrichmentService = enrichmentService;
        _qaService = qaService;
        _evaluationService = evaluationService;
    }

    /// <summary>
    /// 사용 가능한 서비스 확인
    /// </summary>
    public PipelineCapabilities Capabilities => new()
    {
        CanEnrich = _enrichmentService != null,
        CanGenerateQA = _qaService != null,
        CanEvaluate = _evaluationService != null
    };

    /// <summary>
    /// 단일 청크에 대한 전체 파이프라인을 실행합니다.
    /// </summary>
    /// <param name="chunk">처리할 청크</param>
    /// <param name="options">파이프라인 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>파이프라인 실행 결과</returns>
    public async Task<PipelineResult> ProcessChunkAsync(
        FluxIndexChunk chunk,
        PipelineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        options ??= new PipelineOptions();

        var result = new PipelineResult
        {
            ChunkId = chunk.ChunkId,
            SourceId = chunk.Source.SourceId
        };

        try
        {
            // Step 1: Enrichment (optional)
            if (options.EnableEnrichment && _enrichmentService != null)
            {
                result.EnrichedChunk = await _enrichmentService.EnrichAsync(
                    chunk, options.EnrichmentOptions, cancellationToken);
                result.EnrichmentCompleted = true;
            }

            // Step 2: QA Generation (optional)
            if (options.EnableQAGeneration && _qaService != null)
            {
                result.GeneratedQAPairs = await _qaService.GenerateFromChunkAsync(
                    chunk, options.QAGenerationOptions, cancellationToken);
                result.QAGenerationCompleted = true;
            }

            // Step 3: Evaluation (optional) - evaluate generated QA pairs
            if (options.EnableEvaluation && _evaluationService != null && result.GeneratedQAPairs?.Count > 0)
            {
                var evaluations = new List<QAPairWithEvaluation>();
                foreach (var qa in result.GeneratedQAPairs)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var evaluation = await _evaluationService.EvaluateAsync(
                        chunk.Content,
                        qa.Question,
                        qa.Answer,
                        options.EvaluationOptions,
                        cancellationToken);

                    evaluations.Add(new QAPairWithEvaluation
                    {
                        Question = qa.Question,
                        Answer = qa.Answer,
                        Context = qa.Context,
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
        }

        return result;
    }

    /// <summary>
    /// 여러 청크에 대한 배치 파이프라인을 실행합니다.
    /// </summary>
    /// <param name="chunks">처리할 청크들</param>
    /// <param name="options">파이프라인 옵션</param>
    /// <param name="progressCallback">진행 상황 콜백</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>배치 파이프라인 결과</returns>
    public async Task<BatchPipelineResult> ProcessBatchAsync(
        IEnumerable<FluxIndexChunk> chunks,
        PipelineOptions? options = null,
        Action<int, int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        var results = new List<PipelineResult>();
        var processedCount = 0;

        foreach (var chunk in chunkList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await ProcessChunkAsync(chunk, options, cancellationToken);
            results.Add(result);

            processedCount++;
            progressCallback?.Invoke(processedCount, chunkList.Count);
        }

        return new BatchPipelineResult
        {
            Results = results,
            TotalChunks = chunkList.Count,
            SuccessfulChunks = results.Count(r => r.Success),
            FailedChunks = results.Count(r => !r.Success),
            TotalQAPairsGenerated = results.Sum(r => r.GeneratedQAPairs?.Count ?? 0),
            TotalQAPairsEvaluated = results.Sum(r => r.EvaluatedQAPairs?.Count ?? 0)
        };
    }

    /// <summary>
    /// 문서 전체에 대한 완전한 QA 데이터셋을 생성합니다.
    /// </summary>
    /// <param name="chunks">문서의 모든 청크</param>
    /// <param name="options">파이프라인 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>문서 QA 데이터셋</returns>
    public async Task<DocumentQADataset> GenerateDocumentQADatasetAsync(
        IEnumerable<FluxIndexChunk> chunks,
        PipelineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_qaService == null)
            throw new InvalidOperationException("QA Generation service is not available.");

        options ??= new PipelineOptions { EnableQAGeneration = true, EnableEvaluation = true };

        return await _qaService.GenerateDatasetAsync(
            chunks,
            options.QAPipelineOptions,
            cancellationToken);
    }

    /// <summary>
    /// RAG 응답의 품질을 평가합니다.
    /// </summary>
    /// <param name="context">검색된 컨텍스트</param>
    /// <param name="question">사용자 질문</param>
    /// <param name="answer">생성된 답변</param>
    /// <param name="options">평가 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>RAG 평가 결과</returns>
    public async Task<RAGEvaluationResult> EvaluateRAGResponseAsync(
        string context,
        string question,
        string answer,
        EvaluationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_evaluationService == null)
            throw new InvalidOperationException("RAG Evaluation service is not available.");

        return await _evaluationService.EvaluateAsync(
            context, question, answer, options, cancellationToken);
    }
}

/// <summary>
/// 파이프라인 기능 가용성
/// </summary>
public sealed class PipelineCapabilities
{
    /// <summary>청크 인리치먼트 가능 여부</summary>
    public bool CanEnrich { get; init; }

    /// <summary>QA 생성 가능 여부</summary>
    public bool CanGenerateQA { get; init; }

    /// <summary>RAG 평가 가능 여부</summary>
    public bool CanEvaluate { get; init; }

    /// <summary>모든 기능이 사용 가능한지 여부</summary>
    public bool IsFullyCapable => CanEnrich && CanGenerateQA && CanEvaluate;
}

/// <summary>
/// 파이프라인 실행 옵션
/// </summary>
public sealed class PipelineOptions
{
    /// <summary>청크 인리치먼트 활성화 여부</summary>
    public bool EnableEnrichment { get; set; } = true;

    /// <summary>QA 생성 활성화 여부</summary>
    public bool EnableQAGeneration { get; set; } = true;

    /// <summary>RAG 평가 활성화 여부</summary>
    public bool EnableEvaluation { get; set; } = true;

    /// <summary>인리치먼트 옵션</summary>
    public EnrichmentOptions? EnrichmentOptions { get; set; }

    /// <summary>QA 생성 옵션</summary>
    public QAGenerationOptions? QAGenerationOptions { get; set; }

    /// <summary>QA 파이프라인 옵션 (배치용)</summary>
    public QAPipelineOptions? QAPipelineOptions { get; set; }

    /// <summary>평가 옵션</summary>
    public EvaluationOptions? EvaluationOptions { get; set; }
}

/// <summary>
/// 단일 청크 파이프라인 결과
/// </summary>
public sealed class PipelineResult
{
    /// <summary>청크 ID</summary>
    public required string ChunkId { get; init; }

    /// <summary>소스 문서 ID</summary>
    public required string SourceId { get; init; }

    /// <summary>인리치먼트된 청크</summary>
    public FluxImproverEnrichedChunk? EnrichedChunk { get; set; }

    /// <summary>생성된 QA 쌍</summary>
    public IReadOnlyList<GeneratedQAPair>? GeneratedQAPairs { get; set; }

    /// <summary>평가된 QA 쌍</summary>
    public IReadOnlyList<QAPairWithEvaluation>? EvaluatedQAPairs { get; set; }

    /// <summary>인리치먼트 완료 여부</summary>
    public bool EnrichmentCompleted { get; set; }

    /// <summary>QA 생성 완료 여부</summary>
    public bool QAGenerationCompleted { get; set; }

    /// <summary>평가 완료 여부</summary>
    public bool EvaluationCompleted { get; set; }

    /// <summary>성공 여부</summary>
    public bool Success { get; set; }

    /// <summary>오류 메시지</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 평가가 포함된 QA 쌍
/// </summary>
public sealed class QAPairWithEvaluation
{
    /// <summary>질문</summary>
    public required string Question { get; init; }

    /// <summary>답변</summary>
    public required string Answer { get; init; }

    /// <summary>컨텍스트</summary>
    public required string Context { get; init; }

    /// <summary>RAG 평가 결과</summary>
    public required RAGEvaluationResult Evaluation { get; init; }
}

/// <summary>
/// 배치 파이프라인 결과
/// </summary>
public sealed class BatchPipelineResult
{
    /// <summary>개별 청크 결과</summary>
    public required IReadOnlyList<PipelineResult> Results { get; init; }

    /// <summary>총 청크 수</summary>
    public int TotalChunks { get; init; }

    /// <summary>성공한 청크 수</summary>
    public int SuccessfulChunks { get; init; }

    /// <summary>실패한 청크 수</summary>
    public int FailedChunks { get; init; }

    /// <summary>총 생성된 QA 쌍 수</summary>
    public int TotalQAPairsGenerated { get; init; }

    /// <summary>총 평가된 QA 쌍 수</summary>
    public int TotalQAPairsEvaluated { get; init; }

    /// <summary>성공률</summary>
    public double SuccessRate => TotalChunks > 0 ? (double)SuccessfulChunks / TotalChunks : 0;
}
