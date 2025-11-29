using FluxIndex.Extensions.FluxImprover.Services;
using FluxIndex.SDK;
using FluxImprover.Options;
using FluxImprover.Evaluation;
using FluxImprover.QAGeneration;
using Microsoft.Extensions.DependencyInjection;
using FluxIndexChunk = FluxIndex.Core.Application.Interfaces.IEnrichedChunk;
using FluxImproverEnrichedChunk = FluxImprover.Models.IEnrichedChunk;
using FilteringOptions = FluxImprover.Options.ChunkFilteringOptions;

namespace FluxIndex.Extensions.FluxImprover;

/// <summary>
/// FluxIndexContext용 FluxImprover 확장 메서드
/// SDK 사용자가 enrichment, evaluation, QA 생성 기능을 편리하게 사용할 수 있도록 지원
/// </summary>
public static class FluxIndexContextExtensions
{
    /// <summary>
    /// FluxIndexContext에서 ChunkEnrichmentServiceWrapper를 가져옵니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <returns>청크 인리치먼트 서비스 래퍼 (등록되지 않은 경우 null)</returns>
    public static ChunkEnrichmentServiceWrapper? GetEnrichmentService(this FluxIndexContext context)
    {
        return context.ServiceProvider.GetService<ChunkEnrichmentServiceWrapper>();
    }

    /// <summary>
    /// FluxIndexContext에서 RAGEvaluationService를 가져옵니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <returns>RAG 평가 서비스 (등록되지 않은 경우 null)</returns>
    public static RAGEvaluationService? GetRAGEvaluationService(this FluxIndexContext context)
    {
        return context.ServiceProvider.GetService<RAGEvaluationService>();
    }

    /// <summary>
    /// FluxIndexContext에서 QAGenerationService를 가져옵니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <returns>QA 생성 서비스 (등록되지 않은 경우 null)</returns>
    public static QAGenerationService? GetQAGenerationService(this FluxIndexContext context)
    {
        return context.ServiceProvider.GetService<QAGenerationService>();
    }

    /// <summary>
    /// 청크에 LLM 기반 요약 및 키워드를 추가합니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <param name="chunk">인리치먼트할 청크</param>
    /// <param name="options">인리치먼트 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>인리치먼트된 청크</returns>
    /// <exception cref="InvalidOperationException">인리치먼트 서비스가 등록되지 않은 경우</exception>
    public static async Task<FluxImproverEnrichedChunk> EnrichChunkAsync(
        this FluxIndexContext context,
        FluxIndexChunk chunk,
        EnrichmentOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var enrichmentService = context.GetEnrichmentService()
            ?? throw new InvalidOperationException(
                "ChunkEnrichmentServiceWrapper is not registered. " +
                "Please call AddChunkEnrichmentWrapper() during service configuration.");

        return await enrichmentService.EnrichAsync(chunk, options, cancellationToken);
    }

    /// <summary>
    /// 여러 청크에 일괄 인리치먼트를 수행합니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <param name="chunks">인리치먼트할 청크들</param>
    /// <param name="options">인리치먼트 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>인리치먼트된 청크들</returns>
    public static async Task<IReadOnlyList<FluxImproverEnrichedChunk>> EnrichChunksAsync(
        this FluxIndexContext context,
        IEnumerable<FluxIndexChunk> chunks,
        EnrichmentOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var enrichmentService = context.GetEnrichmentService()
            ?? throw new InvalidOperationException(
                "ChunkEnrichmentServiceWrapper is not registered. " +
                "Please call AddChunkEnrichmentWrapper() during service configuration.");

        return await enrichmentService.EnrichBatchAsync(chunks, options, cancellationToken);
    }

    /// <summary>
    /// RAG 응답의 품질을 종합 평가합니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <param name="retrievedContext">검색된 컨텍스트</param>
    /// <param name="question">사용자 질문</param>
    /// <param name="answer">생성된 답변</param>
    /// <param name="options">평가 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>RAG 평가 결과 (Answerability, Faithfulness, Relevancy)</returns>
    public static async Task<RAGEvaluationResult> EvaluateRAGAsync(
        this FluxIndexContext context,
        string retrievedContext,
        string question,
        string answer,
        EvaluationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var evaluationService = context.GetRAGEvaluationService()
            ?? throw new InvalidOperationException(
                "RAGEvaluationService is not registered. " +
                "Please call AddRAGEvaluation() during service configuration.");

        return await evaluationService.EvaluateAsync(retrievedContext, question, answer, options, cancellationToken);
    }

    /// <summary>
    /// 답변 가능성을 평가합니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <param name="retrievedContext">검색된 컨텍스트</param>
    /// <param name="question">사용자 질문</param>
    /// <param name="options">평가 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>답변 가능성 평가 결과</returns>
    public static async Task<MetricResult> EvaluateAnswerabilityAsync(
        this FluxIndexContext context,
        string retrievedContext,
        string question,
        EvaluationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var evaluationService = context.GetRAGEvaluationService()
            ?? throw new InvalidOperationException(
                "RAGEvaluationService is not registered. " +
                "Please call AddRAGEvaluation() during service configuration.");

        return await evaluationService.EvaluateAnswerabilityAsync(retrievedContext, question, options, cancellationToken);
    }

    /// <summary>
    /// 답변의 충실도를 평가합니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <param name="retrievedContext">검색된 컨텍스트</param>
    /// <param name="answer">생성된 답변</param>
    /// <param name="options">평가 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>충실도 평가 결과</returns>
    public static async Task<MetricResult> EvaluateFaithfulnessAsync(
        this FluxIndexContext context,
        string retrievedContext,
        string answer,
        EvaluationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var evaluationService = context.GetRAGEvaluationService()
            ?? throw new InvalidOperationException(
                "RAGEvaluationService is not registered. " +
                "Please call AddRAGEvaluation() during service configuration.");

        return await evaluationService.EvaluateFaithfulnessAsync(retrievedContext, answer, options, cancellationToken);
    }

    /// <summary>
    /// 답변의 관련성을 평가합니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <param name="question">사용자 질문</param>
    /// <param name="answer">생성된 답변</param>
    /// <param name="options">평가 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>관련성 평가 결과</returns>
    public static async Task<MetricResult> EvaluateRelevancyAsync(
        this FluxIndexContext context,
        string question,
        string answer,
        EvaluationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var evaluationService = context.GetRAGEvaluationService()
            ?? throw new InvalidOperationException(
                "RAGEvaluationService is not registered. " +
                "Please call AddRAGEvaluation() during service configuration.");

        return await evaluationService.EvaluateRelevancyAsync(question, answer, options, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 청크에서 QA 쌍을 생성합니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <param name="chunk">QA 생성할 청크</param>
    /// <param name="options">생성 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>생성된 QA 쌍 목록</returns>
    public static async Task<IReadOnlyList<GeneratedQAPair>> GenerateQAFromChunkAsync(
        this FluxIndexContext context,
        FluxIndexChunk chunk,
        QAGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var qaService = context.GetQAGenerationService()
            ?? throw new InvalidOperationException(
                "QAGenerationService is not registered. " +
                "Please call AddQAGeneration() during service configuration.");

        return await qaService.GenerateFromChunkAsync(chunk, options, cancellationToken);
    }

    /// <summary>
    /// 여러 청크에서 QA 데이터셋을 생성합니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <param name="chunks">QA 생성할 청크들</param>
    /// <param name="options">파이프라인 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>문서 전체 QA 데이터셋</returns>
    public static async Task<DocumentQADataset> GenerateQADatasetAsync(
        this FluxIndexContext context,
        IEnumerable<FluxIndexChunk> chunks,
        QAPipelineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var qaService = context.GetQAGenerationService()
            ?? throw new InvalidOperationException(
                "QAGenerationService is not registered. " +
                "Please call AddQAGeneration() during service configuration.");

        return await qaService.GenerateDatasetAsync(chunks, options, cancellationToken);
    }

    /// <summary>
    /// FluxImprover 서비스들이 등록되어 있는지 확인합니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <returns>FluxImprover 서비스 가용성 정보</returns>
    public static FluxImproverAvailability GetFluxImproverAvailability(this FluxIndexContext context)
    {
        return new FluxImproverAvailability
        {
            IsEnrichmentAvailable = context.GetEnrichmentService() != null,
            IsRAGEvaluationAvailable = context.GetRAGEvaluationService() != null,
            IsQAGenerationAvailable = context.GetQAGenerationService() != null,
            IsChunkFilteringAvailable = context.GetChunkFilteringService() != null
        };
    }

    /// <summary>
    /// FluxIndexContext에서 ChunkFilteringServiceWrapper를 가져옵니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <returns>청크 필터링 서비스 래퍼 (등록되지 않은 경우 null)</returns>
    public static ChunkFilteringServiceWrapper? GetChunkFilteringService(this FluxIndexContext context)
    {
        return context.ServiceProvider.GetService<ChunkFilteringServiceWrapper>();
    }

    /// <summary>
    /// LLM 기반 3단계 평가를 통해 청크를 필터링합니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <param name="chunks">필터링할 청크들</param>
    /// <param name="query">관련성 평가를 위한 쿼리 (null일 경우 품질 기반 필터링만 수행)</param>
    /// <param name="options">필터링 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>필터링된 청크들과 점수 및 평가 상세 정보</returns>
    public static async Task<IReadOnlyList<FilteredFluxIndexChunk>> FilterChunksAsync(
        this FluxIndexContext context,
        IEnumerable<FluxIndexChunk> chunks,
        string? query,
        FilteringOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var filteringService = context.GetChunkFilteringService()
            ?? throw new InvalidOperationException(
                "ChunkFilteringServiceWrapper is not registered. " +
                "Please call AddChunkFilteringWrapper() during service configuration.");

        return await filteringService.FilterAsync(chunks, query, options, cancellationToken);
    }

    /// <summary>
    /// 최소 점수 기준으로 청크를 필터링하고 점수 내림차순으로 정렬합니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <param name="chunks">필터링할 청크들</param>
    /// <param name="query">관련성 평가를 위한 쿼리</param>
    /// <param name="minScore">최소 점수 임계값 (0.0 ~ 1.0, 기본값: 0.7)</param>
    /// <param name="maxResults">반환할 최대 청크 수 (null이면 제한 없음)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>필터링된 FluxIndex 청크들</returns>
    public static async Task<IReadOnlyList<FluxIndexChunk>> FilterChunksByScoreAsync(
        this FluxIndexContext context,
        IEnumerable<FluxIndexChunk> chunks,
        string? query,
        double minScore = 0.7,
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        var filteringService = context.GetChunkFilteringService()
            ?? throw new InvalidOperationException(
                "ChunkFilteringServiceWrapper is not registered. " +
                "Please call AddChunkFilteringWrapper() during service configuration.");

        return await filteringService.FilterByScoreAsync(
            chunks, query, minScore, maxResults, cancellationToken);
    }

    /// <summary>
    /// 단일 청크에 대해 3단계 평가를 수행합니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <param name="chunk">평가할 청크</param>
    /// <param name="query">관련성 평가를 위한 쿼리</param>
    /// <param name="options">평가 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>상세 평가 결과</returns>
    public static async Task<ChunkAssessmentResult> AssessChunkAsync(
        this FluxIndexContext context,
        FluxIndexChunk chunk,
        string? query,
        FilteringOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var filteringService = context.GetChunkFilteringService()
            ?? throw new InvalidOperationException(
                "ChunkFilteringServiceWrapper is not registered. " +
                "Please call AddChunkFilteringWrapper() during service configuration.");

        return await filteringService.AssessAsync(chunk, query, options, cancellationToken);
    }

    /// <summary>
    /// FluxIndexContext에서 FluxImproverPipeline을 가져옵니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <returns>FluxImprover 파이프라인 (등록되지 않은 경우 null)</returns>
    public static FluxImproverPipeline? GetPipeline(this FluxIndexContext context)
    {
        return context.ServiceProvider.GetService<FluxImproverPipeline>();
    }

    /// <summary>
    /// 단일 청크에 대한 전체 FluxImprover 파이프라인을 실행합니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <param name="chunk">처리할 청크</param>
    /// <param name="options">파이프라인 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>파이프라인 실행 결과</returns>
    public static async Task<PipelineResult> RunPipelineAsync(
        this FluxIndexContext context,
        FluxIndexChunk chunk,
        PipelineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var pipeline = context.GetPipeline()
            ?? throw new InvalidOperationException(
                "FluxImproverPipeline is not registered. " +
                "Please call AddFluxImproverPipeline() during service configuration.");

        return await pipeline.ProcessChunkAsync(chunk, options, cancellationToken);
    }

    /// <summary>
    /// 여러 청크에 대한 배치 FluxImprover 파이프라인을 실행합니다.
    /// </summary>
    /// <param name="context">FluxIndex 컨텍스트</param>
    /// <param name="chunks">처리할 청크들</param>
    /// <param name="options">파이프라인 옵션</param>
    /// <param name="progressCallback">진행 상황 콜백</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>배치 파이프라인 결과</returns>
    public static async Task<BatchPipelineResult> RunBatchPipelineAsync(
        this FluxIndexContext context,
        IEnumerable<FluxIndexChunk> chunks,
        PipelineOptions? options = null,
        Action<int, int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var pipeline = context.GetPipeline()
            ?? throw new InvalidOperationException(
                "FluxImproverPipeline is not registered. " +
                "Please call AddFluxImproverPipeline() during service configuration.");

        return await pipeline.ProcessBatchAsync(chunks, options, progressCallback, cancellationToken);
    }
}

/// <summary>
/// FluxImprover 서비스 가용성 정보
/// </summary>
public sealed class FluxImproverAvailability
{
    /// <summary>
    /// 청크 인리치먼트 서비스 가용 여부
    /// </summary>
    public bool IsEnrichmentAvailable { get; init; }

    /// <summary>
    /// RAG 평가 서비스 가용 여부
    /// </summary>
    public bool IsRAGEvaluationAvailable { get; init; }

    /// <summary>
    /// QA 생성 서비스 가용 여부
    /// </summary>
    public bool IsQAGenerationAvailable { get; init; }

    /// <summary>
    /// 청크 필터링 서비스 가용 여부
    /// </summary>
    public bool IsChunkFilteringAvailable { get; init; }

    /// <summary>
    /// 모든 FluxImprover 서비스가 가용한지 여부
    /// </summary>
    public bool IsFullyAvailable => IsEnrichmentAvailable && IsRAGEvaluationAvailable
                                   && IsQAGenerationAvailable && IsChunkFilteringAvailable;

    /// <summary>
    /// 최소 하나 이상의 서비스가 가용한지 여부
    /// </summary>
    public bool IsPartiallyAvailable => IsEnrichmentAvailable || IsRAGEvaluationAvailable
                                       || IsQAGenerationAvailable || IsChunkFilteringAvailable;
}
