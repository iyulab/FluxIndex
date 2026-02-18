using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 벡터 양자화 서비스 인터페이스
/// 대규모 벡터 저장 시 메모리/저장 공간 최적화를 위한 양자화 기능 제공
/// </summary>
public interface IVectorQuantizer
{
    /// <summary>
    /// 벡터를 양자화하여 압축된 형태로 변환
    /// </summary>
    /// <param name="vector">원본 벡터</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>양자화된 벡터</returns>
    Task<QuantizedVector> QuantizeAsync(float[] vector, CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 벡터를 배치로 양자화
    /// </summary>
    /// <param name="vectors">원본 벡터들</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>양자화된 벡터들</returns>
    Task<IEnumerable<QuantizedVector>> QuantizeBatchAsync(
        IEnumerable<float[]> vectors,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 양자화된 벡터를 원본 형태로 복원 (근사값)
    /// </summary>
    /// <param name="quantizedVector">양자화된 벡터</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>복원된 벡터 (근사값)</returns>
    Task<float[]> DequantizeAsync(QuantizedVector quantizedVector, CancellationToken cancellationToken = default);

    /// <summary>
    /// 양자화된 두 벡터 간의 거리 계산 (역양자화 없이 직접 계산)
    /// </summary>
    /// <param name="a">첫 번째 양자화 벡터</param>
    /// <param name="b">두 번째 양자화 벡터</param>
    /// <returns>거리 값 (낮을수록 유사)</returns>
    float ComputeDistance(QuantizedVector a, QuantizedVector b);

    /// <summary>
    /// 양자화된 벡터와 원본 벡터 간의 거리 계산
    /// </summary>
    /// <param name="quantized">양자화된 벡터</param>
    /// <param name="vector">원본 벡터</param>
    /// <returns>거리 값</returns>
    float ComputeDistanceToVector(QuantizedVector quantized, float[] vector);

    /// <summary>
    /// 코드북 학습 (Product Quantization 등에서 사용)
    /// </summary>
    /// <param name="trainingVectors">학습용 벡터들</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task TrainAsync(IEnumerable<float[]> trainingVectors, CancellationToken cancellationToken = default);

    /// <summary>
    /// 양자화 방식 정보
    /// </summary>
    QuantizationType QuantizationType { get; }

    /// <summary>
    /// 압축률 (원본 대비 압축된 크기 비율, 0~1)
    /// </summary>
    float CompressionRatio { get; }

    /// <summary>
    /// 원본 벡터 차원
    /// </summary>
    int OriginalDimension { get; }

    /// <summary>
    /// 양자화된 벡터의 바이트 크기
    /// </summary>
    int QuantizedSizeBytes { get; }

    /// <summary>
    /// 학습 완료 여부
    /// </summary>
    bool IsTrained { get; }
}

/// <summary>
/// 양자화된 벡터 표현
/// </summary>
public class QuantizedVector
{
    /// <summary>
    /// 양자화된 데이터 (바이트 배열)
    /// </summary>
    public byte[] Data { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// 양자화 타입
    /// </summary>
    public QuantizationType Type { get; init; }

    /// <summary>
    /// 원본 벡터 차원
    /// </summary>
    public int OriginalDimension { get; init; }

    /// <summary>
    /// 양자화 메타데이터 (스케일, 오프셋 등)
    /// </summary>
    public QuantizationMetadata? Metadata { get; init; }

    /// <summary>
    /// 생성 시각
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 바이트 크기
    /// </summary>
    public int SizeBytes => Data.Length + (Metadata is not null ? QuantizationMetadata.SizeBytes : 0);
}

/// <summary>
/// 양자화 메타데이터
/// </summary>
public class QuantizationMetadata
{
    /// <summary>
    /// 스칼라 양자화용 스케일 값
    /// </summary>
    public float Scale { get; init; }

    /// <summary>
    /// 스칼라 양자화용 오프셋 값
    /// </summary>
    public float Offset { get; init; }

    /// <summary>
    /// 최소값 (정규화용)
    /// </summary>
    public float MinValue { get; init; }

    /// <summary>
    /// 최대값 (정규화용)
    /// </summary>
    public float MaxValue { get; init; }

    /// <summary>
    /// Product Quantization 서브벡터 수
    /// </summary>
    public int NumSubvectors { get; init; }

    /// <summary>
    /// Product Quantization 코드북 크기
    /// </summary>
    public int CodebookSize { get; init; }

    /// <summary>
    /// 메타데이터 바이트 크기
    /// </summary>
    public static int SizeBytes => sizeof(float) * 4 + sizeof(int) * 2; // 24 bytes
}

/// <summary>
/// 양자화 방식
/// </summary>
public enum QuantizationType
{
    /// <summary>
    /// 양자화 없음 (원본 float32)
    /// </summary>
    None = 0,

    /// <summary>
    /// 스칼라 양자화 (int8) - 4배 압축
    /// </summary>
    ScalarInt8 = 1,

    /// <summary>
    /// 스칼라 양자화 (uint8) - 4배 압축
    /// </summary>
    ScalarUInt8 = 2,

    /// <summary>
    /// 스칼라 양자화 (int4) - 8배 압축
    /// </summary>
    ScalarInt4 = 3,

    /// <summary>
    /// 이진 양자화 (1비트) - 32배 압축
    /// </summary>
    Binary = 4,

    /// <summary>
    /// Product Quantization - 가변 압축률
    /// </summary>
    ProductQuantization = 5,

    /// <summary>
    /// Optimized Product Quantization - 회전 최적화 적용
    /// </summary>
    OptimizedProductQuantization = 6
}

/// <summary>
/// 양자화 옵션
/// </summary>
public class QuantizationOptions
{
    /// <summary>
    /// 양자화 방식
    /// </summary>
    public QuantizationType Type { get; set; } = QuantizationType.ScalarInt8;

    /// <summary>
    /// 벡터 차원
    /// </summary>
    public int Dimension { get; set; } = 1536;

    /// <summary>
    /// Product Quantization 서브벡터 수 (PQ 사용 시)
    /// </summary>
    public int NumSubvectors { get; set; } = 8;

    /// <summary>
    /// Product Quantization 코드북 크기 (PQ 사용 시)
    /// </summary>
    public int CodebookSize { get; set; } = 256;

    /// <summary>
    /// 학습용 샘플 수
    /// </summary>
    public int TrainingSamples { get; set; } = 10000;

    /// <summary>
    /// K-Means 반복 횟수 (PQ 학습 시)
    /// </summary>
    public int KMeansIterations { get; set; } = 25;

    /// <summary>
    /// 정규화 적용 여부
    /// </summary>
    public bool NormalizeVectors { get; set; } = true;

    /// <summary>
    /// 대칭 양자화 사용 여부 (스칼라 양자화 시)
    /// </summary>
    public bool UseSymmetricQuantization { get; set; } = true;
}
