using System.Runtime.CompilerServices;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Core.Application.Services.Quantization;

/// <summary>
/// Product Quantization (PQ) 구현
/// 벡터를 서브벡터로 분할하고 각각을 코드북으로 양자화
/// 높은 압축률과 빠른 거리 계산 제공
/// </summary>
public partial class ProductQuantizer : IVectorQuantizer
{
    private readonly QuantizationOptions _options;
    private readonly ILogger<ProductQuantizer> _logger;

    // 코드북: [서브벡터 인덱스][코드워드 인덱스][차원 값들]
    private float[][][] _codebooks;
    private int _subvectorDimension;
    private bool _isTrained;

    // 사전 계산된 거리 테이블은 BuildDistanceTable() 메서드로 생성

    public ProductQuantizer(
        IOptions<QuantizationOptions> options,
        ILogger<ProductQuantizer> logger)
    {
        _options = options.Value;
        _logger = logger;

        ValidateOptions();

        _subvectorDimension = _options.Dimension / _options.NumSubvectors;
        _codebooks = new float[_options.NumSubvectors][][];
        _isTrained = false;
    }

    private void ValidateOptions()
    {
        if (_options.Dimension % _options.NumSubvectors != 0)
        {
            throw new ArgumentException(
                $"Dimension ({_options.Dimension}) must be divisible by NumSubvectors ({_options.NumSubvectors})");
        }

        if (_options.CodebookSize > 256)
        {
            throw new ArgumentException(
                $"CodebookSize ({_options.CodebookSize}) must be <= 256 to fit in a byte");
        }
    }

    public QuantizationType QuantizationType => QuantizationType.ProductQuantization;

    public float CompressionRatio
    {
        get
        {
            // 원본: Dimension * 4 bytes (float32)
            // PQ: NumSubvectors bytes (각 서브벡터당 1바이트 코드)
            var originalSize = _options.Dimension * sizeof(float);
            var compressedSize = _options.NumSubvectors;
            return (float)compressedSize / originalSize;
        }
    }

    public int OriginalDimension => _options.Dimension;

    public int QuantizedSizeBytes => _options.NumSubvectors;

    public bool IsTrained => _isTrained;

    public async Task<QuantizedVector> QuantizeAsync(float[] vector, CancellationToken cancellationToken = default)
    {
        if (!_isTrained)
            throw new InvalidOperationException("Product quantizer must be trained before use");

        if (vector == null || vector.Length != _options.Dimension)
            throw new ArgumentException($"Vector must have dimension {_options.Dimension}");

        var codes = new byte[_options.NumSubvectors];

        // 각 서브벡터에 대해 가장 가까운 코드워드 찾기
        for (int m = 0; m < _options.NumSubvectors; m++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var subvector = ExtractSubvector(vector, m);
            codes[m] = (byte)FindNearestCodeword(subvector, m);
        }

        return new QuantizedVector
        {
            Data = codes,
            Type = QuantizationType.ProductQuantization,
            OriginalDimension = _options.Dimension,
            Metadata = new QuantizationMetadata
            {
                NumSubvectors = _options.NumSubvectors,
                CodebookSize = _options.CodebookSize
            }
        };
    }

    public async Task<IEnumerable<QuantizedVector>> QuantizeBatchAsync(
        IEnumerable<float[]> vectors,
        CancellationToken cancellationToken = default)
    {
        var results = new List<QuantizedVector>();

        foreach (var vector in vectors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await QuantizeAsync(vector, cancellationToken));
        }

        return results;
    }

    public Task<float[]> DequantizeAsync(QuantizedVector quantizedVector, CancellationToken cancellationToken = default)
    {
        if (!_isTrained)
            throw new InvalidOperationException("Product quantizer must be trained before use");

        ArgumentNullException.ThrowIfNull(quantizedVector);

        var result = new float[_options.Dimension];

        for (int m = 0; m < _options.NumSubvectors; m++)
        {
            var codeIndex = quantizedVector.Data[m];
            var codeword = _codebooks[m][codeIndex];

            var offset = m * _subvectorDimension;
            Array.Copy(codeword, 0, result, offset, _subvectorDimension);
        }

        return Task.FromResult(result);
    }

    public float ComputeDistance(QuantizedVector a, QuantizedVector b)
    {
        if (!_isTrained)
            throw new InvalidOperationException("Product quantizer must be trained before use");

        if (a.Data.Length != b.Data.Length)
            throw new ArgumentException("Quantized vectors must have the same size");

        // Asymmetric Distance Computation (ADC) 사용
        float sumSq = 0;

        for (int m = 0; m < _options.NumSubvectors; m++)
        {
            var codeA = a.Data[m];
            var codeB = b.Data[m];

            // 두 코드워드 간의 거리
            var codewordA = _codebooks[m][codeA];
            var codewordB = _codebooks[m][codeB];

            sumSq += ComputeSubvectorDistance(codewordA, codewordB);
        }

        return (float)Math.Sqrt(sumSq);
    }

    public float ComputeDistanceToVector(QuantizedVector quantized, float[] vector)
    {
        if (!_isTrained)
            throw new InvalidOperationException("Product quantizer must be trained before use");

        // 거리 테이블 생성 (쿼리 벡터에 대해)
        var distanceTable = BuildDistanceTable(vector);

        // 테이블 룩업으로 거리 계산
        float sumSq = 0;
        for (int m = 0; m < _options.NumSubvectors; m++)
        {
            var code = quantized.Data[m];
            sumSq += distanceTable[m][code];
        }

        return (float)Math.Sqrt(sumSq);
    }

    /// <summary>
    /// 쿼리 벡터에 대한 거리 테이블 사전 계산
    /// 대량 검색 시 재사용하여 성능 향상
    /// </summary>
    public float[][] BuildDistanceTable(float[] queryVector)
    {
        if (!_isTrained)
            throw new InvalidOperationException("Product quantizer must be trained before use");

        var table = new float[_options.NumSubvectors][];

        for (int m = 0; m < _options.NumSubvectors; m++)
        {
            table[m] = new float[_options.CodebookSize];
            var querySubvector = ExtractSubvector(queryVector, m);

            for (int k = 0; k < _options.CodebookSize; k++)
            {
                table[m][k] = ComputeSubvectorDistance(querySubvector, _codebooks[m][k]);
            }
        }

        return table;
    }

    /// <summary>
    /// 사전 계산된 거리 테이블을 사용한 빠른 거리 계산
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ComputeDistanceWithTable(float[][] distanceTable, byte[] codes)
    {
        float sumSq = 0;
        for (int m = 0; m < codes.Length; m++)
        {
            sumSq += distanceTable[m][codes[m]];
        }
        return (float)Math.Sqrt(sumSq);
    }

    public async Task TrainAsync(IEnumerable<float[]> trainingVectors, CancellationToken cancellationToken = default)
    {
        var vectors = trainingVectors.ToList();

        if (vectors.Count < _options.CodebookSize)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                LogProductQuantizer4(_logger, vectors.Count, _options.CodebookSize);
        }

        if (_logger.IsEnabled(LogLevel.Warning))
            LogProductQuantizer3(_logger, vectors.Count, _options.NumSubvectors, _options.CodebookSize);

        // 각 서브벡터 공간에 대해 K-Means 클러스터링
        for (int m = 0; m < _options.NumSubvectors; m++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_logger.IsEnabled(LogLevel.Warning))
                LogProductQuantizer2(_logger, m + 1, _options.NumSubvectors);

            // 서브벡터 추출
            var subvectors = vectors
                .Select(v => ExtractSubvector(v, m))
                .ToList();

            // K-Means로 코드북 학습
            _codebooks[m] = await TrainCodebookAsync(subvectors, cancellationToken);
        }

        _isTrained = true;

        LogProductQuantizer1(_logger);
    }

    #region K-Means Training

    private async Task<float[][]> TrainCodebookAsync(
        List<float[]> subvectors,
        CancellationToken cancellationToken)
    {
        var k = _options.CodebookSize;
        var dim = _subvectorDimension;
        var maxIterations = _options.KMeansIterations;

        // 코드북 초기화 (K-Means++ 방식)
        var codebook = InitializeCodebookKMeansPlusPlus(subvectors, k);

        // K-Means 반복
        for (int iter = 0; iter < maxIterations; iter++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 할당 단계: 각 서브벡터를 가장 가까운 코드워드에 할당
            var assignments = new int[subvectors.Count];
            var clusterCounts = new int[k];

            for (int i = 0; i < subvectors.Count; i++)
            {
                var nearestIdx = FindNearestCodewordInCodebook(subvectors[i], codebook);
                assignments[i] = nearestIdx;
                clusterCounts[nearestIdx]++;
            }

            // 업데이트 단계: 클러스터 중심 재계산
            var newCodebook = new float[k][];
            for (int c = 0; c < k; c++)
            {
                newCodebook[c] = new float[dim];
            }

            for (int i = 0; i < subvectors.Count; i++)
            {
                var cluster = assignments[i];
                for (int d = 0; d < dim; d++)
                {
                    newCodebook[cluster][d] += subvectors[i][d];
                }
            }

            // 평균 계산
            for (int c = 0; c < k; c++)
            {
                if (clusterCounts[c] > 0)
                {
                    for (int d = 0; d < dim; d++)
                    {
                        newCodebook[c][d] /= clusterCounts[c];
                    }
                }
                else
                {
                    // 빈 클러스터는 랜덤 벡터로 재초기화
                    var randomIdx = Random.Shared.Next(subvectors.Count);
                    Array.Copy(subvectors[randomIdx], newCodebook[c], dim);
                }
            }

            codebook = newCodebook;
        }

        return codebook;
    }

    private float[][] InitializeCodebookKMeansPlusPlus(List<float[]> vectors, int k)
    {
        var dim = _subvectorDimension;
        var codebook = new float[k][];

        if (vectors.Count == 0)
        {
            // 빈 벡터 세트면 제로 벡터로 초기화
            for (int i = 0; i < k; i++)
            {
                codebook[i] = new float[dim];
            }
            return codebook;
        }

        // 첫 번째 중심은 랜덤 선택
        var firstIdx = Random.Shared.Next(vectors.Count);
        codebook[0] = vectors[firstIdx].ToArray();

        var distances = new float[vectors.Count];

        // 나머지 중심은 거리 기반 확률로 선택
        for (int c = 1; c < k; c++)
        {
            float totalDist = 0;

            // 각 벡터에서 가장 가까운 중심까지의 거리 계산
            for (int i = 0; i < vectors.Count; i++)
            {
                var minDist = float.MaxValue;
                for (int j = 0; j < c; j++)
                {
                    var dist = ComputeSubvectorDistance(vectors[i], codebook[j]);
                    if (dist < minDist) minDist = dist;
                }
                distances[i] = minDist * minDist; // 거리의 제곱
                totalDist += distances[i];
            }

            // 확률적 선택
            var target = (float)Random.Shared.NextDouble() * totalDist;
            float cumulative = 0;
            int selectedIdx = 0;

            for (int i = 0; i < vectors.Count; i++)
            {
                cumulative += distances[i];
                if (cumulative >= target)
                {
                    selectedIdx = i;
                    break;
                }
            }

            codebook[c] = vectors[selectedIdx].ToArray();
        }

        return codebook;
    }

    #endregion

    #region Helper Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float[] ExtractSubvector(float[] vector, int subvectorIndex)
    {
        var result = new float[_subvectorDimension];
        var offset = subvectorIndex * _subvectorDimension;
        Array.Copy(vector, offset, result, 0, _subvectorDimension);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindNearestCodeword(float[] subvector, int subvectorIndex)
    {
        return FindNearestCodewordInCodebook(subvector, _codebooks[subvectorIndex]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindNearestCodewordInCodebook(float[] subvector, float[][] codebook)
    {
        int nearestIdx = 0;
        float minDist = float.MaxValue;

        for (int k = 0; k < codebook.Length; k++)
        {
            var dist = ComputeSubvectorDistance(subvector, codebook[k]);
            if (dist < minDist)
            {
                minDist = dist;
                nearestIdx = k;
            }
        }

        return nearestIdx;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ComputeSubvectorDistance(float[] a, float[] b)
    {
        float sumSq = 0;
        for (int i = 0; i < a.Length; i++)
        {
            var diff = a[i] - b[i];
            sumSq += diff * diff;
        }
        return sumSq; // 제곱 거리 반환 (sqrt는 나중에)
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Warning, Message = "Training set size ({Count}) is smaller than codebook size ({CodebookSize}). Using simple initialization.")]
    private static partial void LogProductQuantizer4(ILogger logger, int count, int codebookSize);
    [LoggerMessage(Level = LogLevel.Information, Message = "Training Product Quantizer: {Count} vectors, {NumSubvectors} subvectors, {CodebookSize} codes")]
    private static partial void LogProductQuantizer3(ILogger logger, int count, int numSubvectors, int codebookSize);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Training codebook for subvector {Index}/{Total}")]
    private static partial void LogProductQuantizer2(ILogger logger, int index, int total);
    [LoggerMessage(Level = LogLevel.Information, Message = "Product Quantizer training completed")]
    private static partial void LogProductQuantizer1(ILogger logger);

    #endregion
}
