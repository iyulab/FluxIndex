using System.Numerics;
using System.Runtime.CompilerServices;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Core.Application.Services.Quantization;

/// <summary>
/// 이진 양자화 구현
/// 각 차원을 1비트로 표현하여 32배 압축
/// 해밍 거리로 빠른 유사도 계산 가능
/// </summary>
public partial class BinaryQuantizer : IVectorQuantizer
{
    private readonly QuantizationOptions _options;
    private readonly ILogger<BinaryQuantizer> _logger;

    // 학습된 임계값 (각 차원별)
    private float[]? _thresholds;
    private bool _isTrained;

    public BinaryQuantizer(
        IOptions<QuantizationOptions> options,
        ILogger<BinaryQuantizer> logger)
    {
        _options = options.Value;
        _logger = logger;
        _isTrained = false;
    }

    public QuantizationType QuantizationType => QuantizationType.Binary;

    public float CompressionRatio => 1.0f / 32.0f; // float32 → 1bit = 32배 압축

    public int OriginalDimension => _options.Dimension;

    public int QuantizedSizeBytes => (_options.Dimension + 7) / 8; // 비트를 바이트로

    public bool IsTrained => _isTrained;

    public Task<QuantizedVector> QuantizeAsync(float[] vector, CancellationToken cancellationToken = default)
    {
        if (vector == null || vector.Length != _options.Dimension)
            throw new ArgumentException($"Vector must have dimension {_options.Dimension}");

        var numBytes = QuantizedSizeBytes;
        var binaryData = new byte[numBytes];

        // 각 차원을 이진화
        for (int i = 0; i < vector.Length; i++)
        {
            var threshold = _thresholds?[i] ?? 0.0f;
            if (vector[i] > threshold)
            {
                var byteIndex = i / 8;
                var bitIndex = i % 8;
                binaryData[byteIndex] |= (byte)(1 << bitIndex);
            }
        }

        return Task.FromResult(new QuantizedVector
        {
            Data = binaryData,
            Type = QuantizationType.Binary,
            OriginalDimension = vector.Length,
            Metadata = new QuantizationMetadata()
        });
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
        ArgumentNullException.ThrowIfNull(quantizedVector);

        var result = new float[quantizedVector.OriginalDimension];

        // 이진 데이터를 float로 변환 (+1 또는 -1)
        for (int i = 0; i < quantizedVector.OriginalDimension; i++)
        {
            var byteIndex = i / 8;
            var bitIndex = i % 8;

            if (byteIndex < quantizedVector.Data.Length)
            {
                var bit = (quantizedVector.Data[byteIndex] >> bitIndex) & 1;
                result[i] = bit == 1 ? 1.0f : -1.0f;
            }
        }

        return Task.FromResult(result);
    }

    public float ComputeDistance(QuantizedVector a, QuantizedVector b)
    {
        if (a.Data.Length != b.Data.Length)
            throw new ArgumentException("Binary vectors must have the same size");

        // 해밍 거리 계산 (XOR 후 1비트 수 카운트)
        var hammingDistance = ComputeHammingDistance(a.Data, b.Data);

        // 정규화된 거리 반환 (0~1 범위)
        return (float)hammingDistance / a.OriginalDimension;
    }

    public float ComputeDistanceToVector(QuantizedVector quantized, float[] vector)
    {
        // 벡터를 먼저 이진화한 후 해밍 거리 계산
        var binaryVector = QuantizeAsync(vector).GetAwaiter().GetResult();
        return ComputeDistance(quantized, binaryVector);
    }

    public Task TrainAsync(IEnumerable<float[]> trainingVectors, CancellationToken cancellationToken = default)
    {
        var vectors = trainingVectors.ToList();

        if (vectors.Count == 0)
        {
            LogBinaryQuantizer3(_logger);
            _thresholds = new float[_options.Dimension];
            _isTrained = true;
            return Task.CompletedTask;
        }

        LogBinaryQuantizer2(_logger, vectors.Count);

        // 각 차원의 중간값(median)을 임계값으로 사용
        _thresholds = new float[_options.Dimension];

        for (int d = 0; d < _options.Dimension; d++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var values = vectors.Select(v => v[d]).OrderBy(x => x).ToList();
            _thresholds[d] = values[values.Count / 2]; // 중간값
        }

        _isTrained = true;

        LogBinaryQuantizer1(_logger);

        return Task.CompletedTask;
    }

    #region Hamming Distance Computation

    /// <summary>
    /// 해밍 거리 계산 (하드웨어 POPCNT 명령어 활용)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeHammingDistance(byte[] a, byte[] b)
    {
        int distance = 0;

        // 64비트 단위로 처리 (성능 최적화)
        int i = 0;
        int length64 = (a.Length / 8) * 8;

        while (i < length64)
        {
            var xorResult = BitConverter.ToUInt64(a, i) ^ BitConverter.ToUInt64(b, i);
            distance += BitOperations.PopCount(xorResult);
            i += 8;
        }

        // 나머지 바이트 처리
        while (i < a.Length)
        {
            var xorResult = (byte)(a[i] ^ b[i]);
            distance += BitOperations.PopCount(xorResult);
            i++;
        }

        return distance;
    }

    /// <summary>
    /// SIMD를 활용한 배치 해밍 거리 계산
    /// </summary>
    public static int[] ComputeHammingDistancesBatch(byte[] query, IEnumerable<byte[]> candidates)
    {
        return candidates
            .Select(c => ComputeHammingDistance(query, c))
            .ToArray();
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// 코사인 유사도를 근사하는 해밍 유사도 계산
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ComputeHammingSimilarity(QuantizedVector a, QuantizedVector b)
    {
        var hammingDist = ComputeHammingDistance(a.Data, b.Data);
        // 해밍 유사도: 1 - (hamming_distance / dimension)
        return 1.0f - ((float)hammingDist / a.OriginalDimension);
    }

    /// <summary>
    /// 이진 벡터를 문자열로 변환 (디버깅용)
    /// </summary>
    public static string ToBinaryString(byte[] data, int dimension)
    {
        var chars = new char[dimension];
        for (int i = 0; i < dimension; i++)
        {
            var byteIndex = i / 8;
            var bitIndex = i % 8;
            if (byteIndex < data.Length)
            {
                chars[i] = ((data[byteIndex] >> bitIndex) & 1) == 1 ? '1' : '0';
            }
            else
            {
                chars[i] = '0';
            }
        }
        return new string(chars);
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Warning, Message = "No training vectors provided, using zero threshold")]
    private static partial void LogBinaryQuantizer3(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Training binary quantizer with {Count} vectors")]
    private static partial void LogBinaryQuantizer2(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Information, Message = "Binary quantizer training completed")]
    private static partial void LogBinaryQuantizer1(ILogger logger);

    #endregion
}
