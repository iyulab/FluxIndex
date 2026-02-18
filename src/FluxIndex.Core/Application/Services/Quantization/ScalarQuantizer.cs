using System.Runtime.CompilerServices;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Core.Application.Services.Quantization;

/// <summary>
/// 스칼라 양자화 구현
/// float32 벡터를 int8/uint8로 변환하여 4배 압축
/// </summary>
public partial class ScalarQuantizer : IVectorQuantizer
{
    private readonly QuantizationOptions _options;
    private readonly ILogger<ScalarQuantizer> _logger;

    // 학습된 통계 (전역 스케일/오프셋)
    private float _globalMin;
    private float _globalMax;
    private float _globalScale;
    private float _globalOffset;
    private bool _isTrained;

    public ScalarQuantizer(
        IOptions<QuantizationOptions> options,
        ILogger<ScalarQuantizer> logger)
    {
        _options = options.Value;
        _logger = logger;

        // 기본값 초기화 (학습 전)
        _globalMin = float.MaxValue;
        _globalMax = float.MinValue;
        _globalScale = 1.0f;
        _globalOffset = 0.0f;
        _isTrained = false;
    }

    public QuantizationType QuantizationType => _options.Type switch
    {
        QuantizationType.ScalarInt8 => QuantizationType.ScalarInt8,
        QuantizationType.ScalarUInt8 => QuantizationType.ScalarUInt8,
        QuantizationType.ScalarInt4 => QuantizationType.ScalarInt4,
        _ => QuantizationType.ScalarInt8
    };

    public float CompressionRatio => QuantizationType switch
    {
        QuantizationType.ScalarInt8 => 0.25f,   // 4바이트 → 1바이트
        QuantizationType.ScalarUInt8 => 0.25f,
        QuantizationType.ScalarInt4 => 0.125f,  // 4바이트 → 0.5바이트
        _ => 0.25f
    };

    public int OriginalDimension => _options.Dimension;

    public int QuantizedSizeBytes => QuantizationType switch
    {
        QuantizationType.ScalarInt4 => (_options.Dimension + 1) / 2, // 2개의 값을 1바이트에
        _ => _options.Dimension
    };

    public bool IsTrained => _isTrained;

    public Task<QuantizedVector> QuantizeAsync(float[] vector, CancellationToken cancellationToken = default)
    {
        if (vector == null || vector.Length == 0)
            throw new ArgumentException("Vector cannot be null or empty", nameof(vector));

        if (vector.Length != _options.Dimension)
            throw new ArgumentException($"Vector dimension mismatch: expected {_options.Dimension}, got {vector.Length}");

        var result = QuantizationType switch
        {
            QuantizationType.ScalarInt8 => QuantizeInt8(vector),
            QuantizationType.ScalarUInt8 => QuantizeUInt8(vector),
            QuantizationType.ScalarInt4 => QuantizeInt4(vector),
            _ => QuantizeInt8(vector)
        };

        return Task.FromResult(result);
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

        var result = quantizedVector.Type switch
        {
            QuantizationType.ScalarInt8 => DequantizeInt8(quantizedVector),
            QuantizationType.ScalarUInt8 => DequantizeUInt8(quantizedVector),
            QuantizationType.ScalarInt4 => DequantizeInt4(quantizedVector),
            _ => DequantizeInt8(quantizedVector)
        };

        return Task.FromResult(result);
    }

    public float ComputeDistance(QuantizedVector a, QuantizedVector b)
    {
        if (a.Type != b.Type)
            throw new ArgumentException("Quantization types must match");

        if (a.Data.Length != b.Data.Length)
            throw new ArgumentException("Quantized vector sizes must match");

        // 양자화된 상태에서 직접 거리 계산 (L2 거리의 근사)
        return a.Type switch
        {
            QuantizationType.ScalarInt8 => ComputeDistanceInt8(a.Data, b.Data, a.Metadata, b.Metadata),
            QuantizationType.ScalarUInt8 => ComputeDistanceUInt8(a.Data, b.Data, a.Metadata, b.Metadata),
            QuantizationType.ScalarInt4 => ComputeDistanceInt4(a.Data, b.Data, a.Metadata, b.Metadata),
            _ => ComputeDistanceInt8(a.Data, b.Data, a.Metadata, b.Metadata)
        };
    }

    public float ComputeDistanceToVector(QuantizedVector quantized, float[] vector)
    {
        ArgumentNullException.ThrowIfNull(quantized);
        if (vector == null || vector.Length == 0)
            throw new ArgumentException("Vector cannot be null or empty", nameof(vector));

        // 역양자화 후 거리 계산 (정확도 우선)
        var dequantized = DequantizeAsync(quantized).GetAwaiter().GetResult();
        return ComputeL2Distance(dequantized, vector);
    }

    public Task TrainAsync(IEnumerable<float[]> trainingVectors, CancellationToken cancellationToken = default)
    {
        var vectors = trainingVectors.ToList();

        if (vectors.Count == 0)
        {
            LogScalarQuantizer3(_logger);
            _isTrained = true;
            return Task.CompletedTask;
        }

        LogScalarQuantizer2(_logger, vectors.Count);

        // 전역 최소/최대값 계산
        _globalMin = float.MaxValue;
        _globalMax = float.MinValue;

        foreach (var vector in vectors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var value in vector)
            {
                if (value < _globalMin) _globalMin = value;
                if (value > _globalMax) _globalMax = value;
            }
        }

        // 스케일 및 오프셋 계산
        if (_options.UseSymmetricQuantization)
        {
            // 대칭 양자화: 0을 중심으로 대칭
            var absMax = Math.Max(Math.Abs(_globalMin), Math.Abs(_globalMax));
            _globalScale = 127.0f / absMax;
            _globalOffset = 0.0f;
        }
        else
        {
            // 비대칭 양자화: 전체 범위 사용
            var range = _globalMax - _globalMin;
            _globalScale = range > 0 ? 255.0f / range : 1.0f;
            _globalOffset = _globalMin;
        }

        _isTrained = true;

        if (_logger.IsEnabled(LogLevel.Warning))
            LogScalarQuantizer1(_logger, _globalMin, _globalMax, _globalScale, _globalOffset);

        return Task.CompletedTask;
    }

    #region Int8 Quantization

    private QuantizedVector QuantizeInt8(float[] vector)
    {
        var quantized = new byte[vector.Length];
        float min = float.MaxValue, max = float.MinValue;

        // 로컬 min/max 계산
        foreach (var v in vector)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }

        float scale, offset;
        if (_isTrained && _options.UseSymmetricQuantization)
        {
            scale = _globalScale;
            offset = _globalOffset;
        }
        else
        {
            // 로컬 스케일 사용
            var absMax = Math.Max(Math.Abs(min), Math.Abs(max));
            scale = absMax > 0 ? 127.0f / absMax : 1.0f;
            offset = 0.0f;
        }

        // 양자화
        for (int i = 0; i < vector.Length; i++)
        {
            var scaled = (vector[i] - offset) * scale;
            var clamped = Math.Clamp(scaled, -128.0f, 127.0f);
            quantized[i] = (byte)(sbyte)Math.Round(clamped);
        }

        return new QuantizedVector
        {
            Data = quantized,
            Type = QuantizationType.ScalarInt8,
            OriginalDimension = vector.Length,
            Metadata = new QuantizationMetadata
            {
                Scale = scale,
                Offset = offset,
                MinValue = min,
                MaxValue = max
            }
        };
    }

    private float[] DequantizeInt8(QuantizedVector quantized)
    {
        var result = new float[quantized.OriginalDimension];
        var scale = quantized.Metadata?.Scale ?? _globalScale;
        var offset = quantized.Metadata?.Offset ?? _globalOffset;

        for (int i = 0; i < quantized.Data.Length && i < result.Length; i++)
        {
            var value = (sbyte)quantized.Data[i];
            result[i] = (value / scale) + offset;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ComputeDistanceInt8(byte[] a, byte[] b, QuantizationMetadata? metaA, QuantizationMetadata? metaB)
    {
        // 같은 스케일이면 양자화된 상태에서 직접 계산 가능
        if (metaA?.Scale == metaB?.Scale)
        {
            long sumSq = 0;
            for (int i = 0; i < a.Length; i++)
            {
                var diff = (sbyte)a[i] - (sbyte)b[i];
                sumSq += diff * diff;
            }
            var scale = metaA?.Scale ?? 1.0f;
            return (float)Math.Sqrt(sumSq) / scale;
        }

        // 다른 스케일이면 역양자화 후 계산
        var scaleA = metaA?.Scale ?? 1.0f;
        var scaleB = metaB?.Scale ?? 1.0f;
        var offsetA = metaA?.Offset ?? 0.0f;
        var offsetB = metaB?.Offset ?? 0.0f;

        float sumSqFloat = 0;
        for (int i = 0; i < a.Length; i++)
        {
            var valA = ((sbyte)a[i] / scaleA) + offsetA;
            var valB = ((sbyte)b[i] / scaleB) + offsetB;
            var diff = valA - valB;
            sumSqFloat += diff * diff;
        }
        return (float)Math.Sqrt(sumSqFloat);
    }

    #endregion

    #region UInt8 Quantization

    private static QuantizedVector QuantizeUInt8(float[] vector)
    {
        var quantized = new byte[vector.Length];
        float min = float.MaxValue, max = float.MinValue;

        foreach (var v in vector)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }

        var range = max - min;
        var scale = range > 0 ? 255.0f / range : 1.0f;

        for (int i = 0; i < vector.Length; i++)
        {
            var normalized = (vector[i] - min) * scale;
            quantized[i] = (byte)Math.Clamp(Math.Round(normalized), 0, 255);
        }

        return new QuantizedVector
        {
            Data = quantized,
            Type = QuantizationType.ScalarUInt8,
            OriginalDimension = vector.Length,
            Metadata = new QuantizationMetadata
            {
                Scale = scale,
                Offset = min,
                MinValue = min,
                MaxValue = max
            }
        };
    }

    private static float[] DequantizeUInt8(QuantizedVector quantized)
    {
        var result = new float[quantized.OriginalDimension];
        var scale = quantized.Metadata?.Scale ?? 1.0f;
        var offset = quantized.Metadata?.Offset ?? 0.0f;

        for (int i = 0; i < quantized.Data.Length && i < result.Length; i++)
        {
            result[i] = (quantized.Data[i] / scale) + offset;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ComputeDistanceUInt8(byte[] a, byte[] b, QuantizationMetadata? metaA, QuantizationMetadata? metaB)
    {
        var scaleA = metaA?.Scale ?? 1.0f;
        var scaleB = metaB?.Scale ?? 1.0f;
        var offsetA = metaA?.Offset ?? 0.0f;
        var offsetB = metaB?.Offset ?? 0.0f;

        float sumSq = 0;
        for (int i = 0; i < a.Length; i++)
        {
            var valA = (a[i] / scaleA) + offsetA;
            var valB = (b[i] / scaleB) + offsetB;
            var diff = valA - valB;
            sumSq += diff * diff;
        }
        return (float)Math.Sqrt(sumSq);
    }

    #endregion

    #region Int4 Quantization

    private static QuantizedVector QuantizeInt4(float[] vector)
    {
        // 2개의 4비트 값을 1바이트에 저장
        var quantizedSize = (vector.Length + 1) / 2;
        var quantized = new byte[quantizedSize];

        float min = float.MaxValue, max = float.MinValue;
        foreach (var v in vector)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }

        var absMax = Math.Max(Math.Abs(min), Math.Abs(max));
        var scale = absMax > 0 ? 7.0f / absMax : 1.0f;

        for (int i = 0; i < vector.Length; i += 2)
        {
            var val1 = (int)Math.Round(Math.Clamp(vector[i] * scale, -8, 7));
            var val2 = i + 1 < vector.Length
                ? (int)Math.Round(Math.Clamp(vector[i + 1] * scale, -8, 7))
                : 0;

            // 두 4비트 값을 하나의 바이트에 패킹
            quantized[i / 2] = (byte)(((val1 & 0x0F) << 4) | (val2 & 0x0F));
        }

        return new QuantizedVector
        {
            Data = quantized,
            Type = QuantizationType.ScalarInt4,
            OriginalDimension = vector.Length,
            Metadata = new QuantizationMetadata
            {
                Scale = scale,
                Offset = 0,
                MinValue = min,
                MaxValue = max
            }
        };
    }

    private static float[] DequantizeInt4(QuantizedVector quantized)
    {
        var result = new float[quantized.OriginalDimension];
        var scale = quantized.Metadata?.Scale ?? 1.0f;

        for (int i = 0; i < quantized.Data.Length; i++)
        {
            var packed = quantized.Data[i];

            // 상위 4비트 (부호 확장)
            var val1 = (sbyte)((packed >> 4) | ((packed & 0x80) != 0 ? 0xF0 : 0));
            if ((packed & 0x80) != 0) val1 = (sbyte)(val1 | 0xF0);

            var idx1 = i * 2;
            if (idx1 < result.Length)
                result[idx1] = val1 / scale;

            // 하위 4비트 (부호 확장)
            var val2Raw = packed & 0x0F;
            var val2 = (val2Raw & 0x08) != 0 ? (sbyte)(val2Raw | 0xF0) : (sbyte)val2Raw;

            var idx2 = i * 2 + 1;
            if (idx2 < result.Length)
                result[idx2] = val2 / scale;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ComputeDistanceInt4(byte[] a, byte[] b, QuantizationMetadata? metaA, QuantizationMetadata? metaB)
    {
        var scaleA = metaA?.Scale ?? 1.0f;
        var scaleB = metaB?.Scale ?? 1.0f;

        float sumSq = 0;
        for (int i = 0; i < a.Length; i++)
        {
            // 상위 4비트
            var a1 = (sbyte)((a[i] >> 4) | ((a[i] & 0x80) != 0 ? 0xF0 : 0));
            var b1 = (sbyte)((b[i] >> 4) | ((b[i] & 0x80) != 0 ? 0xF0 : 0));
            var diff1 = (a1 / scaleA) - (b1 / scaleB);
            sumSq += diff1 * diff1;

            // 하위 4비트
            var a2Raw = a[i] & 0x0F;
            var b2Raw = b[i] & 0x0F;
            var a2 = (a2Raw & 0x08) != 0 ? (sbyte)(a2Raw | 0xF0) : (sbyte)a2Raw;
            var b2 = (b2Raw & 0x08) != 0 ? (sbyte)(b2Raw | 0xF0) : (sbyte)b2Raw;
            var diff2 = (a2 / scaleA) - (b2 / scaleB);
            sumSq += diff2 * diff2;
        }
        return (float)Math.Sqrt(sumSq);
    }

    #endregion

    #region Helper Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ComputeL2Distance(float[] a, float[] b)
    {
        float sumSq = 0;
        var length = Math.Min(a.Length, b.Length);

        for (int i = 0; i < length; i++)
        {
            var diff = a[i] - b[i];
            sumSq += diff * diff;
        }

        return (float)Math.Sqrt(sumSq);
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Warning, Message = "No training vectors provided, using default scale")]
    private static partial void LogScalarQuantizer3(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Training scalar quantizer with {Count} vectors")]
    private static partial void LogScalarQuantizer2(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Information, Message = "Scalar quantizer trained: min={Min}, max={Max}, scale={Scale}, offset={Offset}")]
    private static partial void LogScalarQuantizer1(ILogger logger, float min, float max, float scale, float offset);

    #endregion
}
