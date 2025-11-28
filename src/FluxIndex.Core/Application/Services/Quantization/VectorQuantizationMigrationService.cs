using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services.Quantization;

/// <summary>
/// 기존 벡터를 양자화 형식으로 일괄 변환하는 마이그레이션 서비스
/// </summary>
public class VectorQuantizationMigrationService
{
    private readonly IVectorStore _vectorStore;
    private readonly IVectorQuantizer _quantizer;
    private readonly ILogger<VectorQuantizationMigrationService> _logger;

    public VectorQuantizationMigrationService(
        IVectorStore vectorStore,
        IVectorQuantizer quantizer,
        ILogger<VectorQuantizationMigrationService> logger)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _quantizer = quantizer ?? throw new ArgumentNullException(nameof(quantizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 전체 벡터 스토어의 벡터를 양자화하여 마이그레이션합니다
    /// </summary>
    public async Task<MigrationResult> MigrateAllAsync(
        MigrationOptions? options = null,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MigrationOptions();
        var result = new MigrationResult();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation("Starting vector quantization migration with batch size {BatchSize}", options.BatchSize);

        try
        {
            // Product Quantization인 경우 먼저 학습이 필요
            if (_quantizer.QuantizationType == QuantizationType.ProductQuantization)
            {
                _logger.LogInformation("Product Quantization detected - training required");
                var trainingVectors = await CollectTrainingVectorsAsync(options.TrainingSampleSize, cancellationToken);

                if (trainingVectors.Count > 0)
                {
                    _logger.LogInformation("Training with {Count} vectors", trainingVectors.Count);
                    await _quantizer.TrainAsync(trainingVectors, cancellationToken);
                }
            }

            // 배치 단위로 마이그레이션
            var offset = 0;
            var totalProcessed = 0;
            var batchNumber = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                var chunks = await GetChunkBatchAsync(offset, options.BatchSize, cancellationToken);

                if (!chunks.Any())
                    break;

                batchNumber++;
                var batchResult = await ProcessBatchAsync(chunks, options, cancellationToken);

                result.SuccessCount += batchResult.SuccessCount;
                result.FailureCount += batchResult.FailureCount;
                result.SkippedCount += batchResult.SkippedCount;
                result.TotalBytesOriginal += batchResult.TotalBytesOriginal;
                result.TotalBytesQuantized += batchResult.TotalBytesQuantized;

                totalProcessed += chunks.Count();
                offset += options.BatchSize;

                progress?.Report(new MigrationProgress
                {
                    ProcessedCount = totalProcessed,
                    CurrentBatch = batchNumber,
                    SuccessCount = result.SuccessCount,
                    FailureCount = result.FailureCount,
                    SkippedCount = result.SkippedCount
                });

                _logger.LogDebug(
                    "Batch {BatchNumber}: Processed {Count} vectors (Success: {Success}, Failed: {Failed}, Skipped: {Skipped})",
                    batchNumber, chunks.Count(), batchResult.SuccessCount, batchResult.FailureCount, batchResult.SkippedCount);

                // 배치 간 지연
                if (options.BatchDelayMs > 0)
                {
                    await Task.Delay(options.BatchDelayMs, cancellationToken);
                }
            }

            sw.Stop();
            result.ElapsedTime = sw.Elapsed;
            result.IsSuccess = result.FailureCount == 0;

            _logger.LogInformation(
                "Migration completed in {Elapsed}. Total: {Total}, Success: {Success}, Failed: {Failed}, Skipped: {Skipped}",
                sw.Elapsed, totalProcessed, result.SuccessCount, result.FailureCount, result.SkippedCount);

            if (result.TotalBytesOriginal > 0)
            {
                var compressionRatio = (double)result.TotalBytesQuantized / result.TotalBytesOriginal;
                _logger.LogInformation(
                    "Compression: {Original:N0} bytes -> {Quantized:N0} bytes ({Ratio:P2})",
                    result.TotalBytesOriginal, result.TotalBytesQuantized, compressionRatio);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration failed");
            sw.Stop();
            result.ElapsedTime = sw.Elapsed;
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// 특정 문서 ID 목록의 벡터만 마이그레이션합니다
    /// </summary>
    public async Task<MigrationResult> MigrateByDocumentIdsAsync(
        IEnumerable<string> documentIds,
        MigrationOptions? options = null,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MigrationOptions();
        var result = new MigrationResult();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var documentIdList = documentIds.ToList();
        _logger.LogInformation("Starting selective migration for {Count} documents", documentIdList.Count);

        try
        {
            var processedCount = 0;

            foreach (var documentId in documentIdList)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var chunks = await GetChunksByDocumentIdAsync(documentId, cancellationToken);

                if (chunks.Any())
                {
                    var batchResult = await ProcessBatchAsync(chunks, options, cancellationToken);
                    result.SuccessCount += batchResult.SuccessCount;
                    result.FailureCount += batchResult.FailureCount;
                    result.SkippedCount += batchResult.SkippedCount;
                    result.TotalBytesOriginal += batchResult.TotalBytesOriginal;
                    result.TotalBytesQuantized += batchResult.TotalBytesQuantized;
                }

                processedCount++;
                progress?.Report(new MigrationProgress
                {
                    ProcessedCount = processedCount,
                    TotalCount = documentIdList.Count,
                    CurrentBatch = processedCount,
                    SuccessCount = result.SuccessCount,
                    FailureCount = result.FailureCount,
                    SkippedCount = result.SkippedCount
                });
            }

            sw.Stop();
            result.ElapsedTime = sw.Elapsed;
            result.IsSuccess = result.FailureCount == 0;

            _logger.LogInformation(
                "Selective migration completed. Documents: {Total}, Success: {Success}, Failed: {Failed}",
                documentIdList.Count, result.SuccessCount, result.FailureCount);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Selective migration failed");
            sw.Stop();
            result.ElapsedTime = sw.Elapsed;
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// 벡터를 양자화하고 결과를 반환합니다 (저장하지 않음, 테스트/분석용)
    /// </summary>
    public async Task<QuantizationAnalysisResult> AnalyzeQuantizationAsync(
        IEnumerable<float[]> vectors,
        CancellationToken cancellationToken = default)
    {
        var result = new QuantizationAnalysisResult();
        var vectorList = vectors.ToList();

        if (!vectorList.Any())
            return result;

        result.VectorCount = vectorList.Count;
        result.Dimension = vectorList[0].Length;
        result.OriginalSizeBytes = vectorList.Count * vectorList[0].Length * sizeof(float);

        long totalQuantizedBytes = 0;
        var distances = new List<float>();

        foreach (var vector in vectorList)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var quantized = await _quantizer.QuantizeAsync(vector, cancellationToken);
            var dequantized = await _quantizer.DequantizeAsync(quantized, cancellationToken);

            totalQuantizedBytes += quantized.Data.Length;

            // 양자화 오차 계산 (MSE)
            var mse = 0.0f;
            for (int i = 0; i < vector.Length; i++)
            {
                var diff = vector[i] - dequantized[i];
                mse += diff * diff;
            }
            distances.Add(mse / vector.Length);
        }

        result.QuantizedSizeBytes = totalQuantizedBytes;
        result.CompressionRatio = (double)totalQuantizedBytes / result.OriginalSizeBytes;
        result.AverageQuantizationError = distances.Average();
        result.MaxQuantizationError = distances.Max();
        result.MinQuantizationError = distances.Min();
        result.QuantizationType = _quantizer.QuantizationType;

        return result;
    }

    private async Task<List<float[]>> CollectTrainingVectorsAsync(int sampleSize, CancellationToken cancellationToken)
    {
        var trainingVectors = new List<float[]>();
        var chunks = await GetChunkBatchAsync(0, sampleSize, cancellationToken);

        foreach (var chunk in chunks)
        {
            if (chunk.Embedding != null)
            {
                trainingVectors.Add(chunk.Embedding);
            }
        }

        return trainingVectors;
    }

    private async Task<IEnumerable<DocumentChunk>> GetChunkBatchAsync(
        int offset, int limit, CancellationToken cancellationToken)
    {
        // VectorStore가 GetAllAsync 또는 유사 메서드를 지원하는 경우 사용
        // 현재 인터페이스에 없으므로 빈 쿼리로 검색
        // 실제 구현에서는 페이징을 지원하는 메서드가 필요할 수 있음
        try
        {
            // 임의의 벡터로 넓은 범위 검색 (실제 구현에서는 스토어별로 최적화 필요)
            var dummyVector = new float[_quantizer.OriginalDimension];
            Array.Fill(dummyVector, 0.01f);

            return await _vectorStore.SearchAsync(
                dummyVector,
                limit,
                minScore: 0.0f,
                cancellationToken);
        }
        catch
        {
            return Enumerable.Empty<DocumentChunk>();
        }
    }

    private async Task<IEnumerable<DocumentChunk>> GetChunksByDocumentIdAsync(
        string documentId, CancellationToken cancellationToken)
    {
        // DocumentId로 필터링된 검색
        // 실제 구현에서는 메타데이터 필터링을 지원하는 검색 메서드 필요
        var dummyVector = new float[_quantizer.OriginalDimension];
        Array.Fill(dummyVector, 0.01f);

        var allChunks = await _vectorStore.SearchAsync(
            dummyVector,
            1000,
            minScore: 0.0f,
            cancellationToken);

        return allChunks.Where(c => c.DocumentId == documentId);
    }

    private async Task<BatchMigrationResult> ProcessBatchAsync(
        IEnumerable<DocumentChunk> chunks,
        MigrationOptions options,
        CancellationToken cancellationToken)
    {
        var result = new BatchMigrationResult();

        foreach (var chunk in chunks)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (chunk.Embedding == null)
            {
                result.SkippedCount++;
                continue;
            }

            try
            {
                // 원본 크기 기록
                result.TotalBytesOriginal += chunk.Embedding.Length * sizeof(float);

                // 양자화
                var quantized = await _quantizer.QuantizeAsync(chunk.Embedding, cancellationToken);
                result.TotalBytesQuantized += quantized.Data.Length;

                // 양자화된 버전 저장 (IQuantizedVectorStore가 있는 경우)
                if (_vectorStore is IQuantizedVectorStore quantizedStore)
                {
                    await quantizedStore.StoreWithQuantizedAsync(chunk, quantized, cancellationToken);
                }
                else if (options.UpdateOriginalStore)
                {
                    // 원본 스토어에 덮어쓰기 (메타데이터로 양자화 정보 저장)
                    chunk.Metadata ??= new Dictionary<string, object>();
                    chunk.Metadata["quantization_type"] = quantized.Type.ToString();
                    chunk.Metadata["quantized_size"] = quantized.Data.Length;

                    await _vectorStore.StoreAsync(chunk, cancellationToken);
                }

                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to quantize chunk {ChunkId}", chunk.Id);
                result.FailureCount++;

                if (!options.ContinueOnError)
                    throw;
            }
        }

        return result;
    }
}

/// <summary>
/// 마이그레이션 옵션
/// </summary>
public class MigrationOptions
{
    /// <summary>
    /// 배치 크기 (한 번에 처리할 벡터 수)
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Product Quantization 학습용 샘플 크기
    /// </summary>
    public int TrainingSampleSize { get; set; } = 1000;

    /// <summary>
    /// 배치 간 지연 시간 (밀리초)
    /// </summary>
    public int BatchDelayMs { get; set; } = 0;

    /// <summary>
    /// 오류 발생 시 계속 진행할지 여부
    /// </summary>
    public bool ContinueOnError { get; set; } = true;

    /// <summary>
    /// 원본 스토어 업데이트 여부
    /// </summary>
    public bool UpdateOriginalStore { get; set; } = false;
}

/// <summary>
/// 마이그레이션 결과
/// </summary>
public class MigrationResult
{
    public bool IsSuccess { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int SkippedCount { get; set; }
    public long TotalBytesOriginal { get; set; }
    public long TotalBytesQuantized { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public string? ErrorMessage { get; set; }

    public double CompressionRatio => TotalBytesOriginal > 0
        ? (double)TotalBytesQuantized / TotalBytesOriginal
        : 0;
}

/// <summary>
/// 마이그레이션 진행 상황
/// </summary>
public class MigrationProgress
{
    public int ProcessedCount { get; set; }
    public int TotalCount { get; set; }
    public int CurrentBatch { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int SkippedCount { get; set; }

    public double ProgressPercentage => TotalCount > 0
        ? (double)ProcessedCount / TotalCount * 100
        : 0;
}

/// <summary>
/// 양자화 분석 결과
/// </summary>
public class QuantizationAnalysisResult
{
    public int VectorCount { get; set; }
    public int Dimension { get; set; }
    public long OriginalSizeBytes { get; set; }
    public long QuantizedSizeBytes { get; set; }
    public double CompressionRatio { get; set; }
    public float AverageQuantizationError { get; set; }
    public float MaxQuantizationError { get; set; }
    public float MinQuantizationError { get; set; }
    public QuantizationType QuantizationType { get; set; }
}

/// <summary>
/// 배치 마이그레이션 결과 (내부용)
/// </summary>
internal class BatchMigrationResult
{
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int SkippedCount { get; set; }
    public long TotalBytesOriginal { get; set; }
    public long TotalBytesQuantized { get; set; }
}
