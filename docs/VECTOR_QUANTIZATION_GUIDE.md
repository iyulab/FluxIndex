# Vector Quantization Guide

FluxIndex의 벡터 양자화 기능을 사용하여 메모리 사용량을 줄이고 검색 성능을 향상시키는 방법을 설명합니다.

## 개요

벡터 양자화는 고차원 부동소수점 벡터를 더 작은 표현으로 압축하는 기술입니다. FluxIndex는 세 가지 양자화 방법을 지원합니다:

| 양자화 방법 | 압축률 | 정확도 | 검색 속도 | 권장 사용 사례 |
|------------|-------|--------|----------|--------------|
| Scalar Int8 | 4x | 높음 (73%+ recall) | 중간 (2x 향상) | 정확도 중시 일반 검색 |
| Binary | 32x | 중간 (54% recall) | 매우 빠름 (25x 향상) | 대규모 후보 필터링 |
| Product Quantization | 16-64x | 높음 | 빠름 | 메모리 제약 환경 |

## 빠른 시작

### 1. 기본 설정 (Scalar Quantization)

```csharp
using FluxIndex.Core.Application.Services;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Scalar Int8 양자화 설정 (기본값)
services.AddScalarQuantization(dimension: 1536);

// Vector Store와 통합
services.AddQuantizedVectorStoreDecorator(autoQuantize: true);
```

### 2. Binary Quantization (최대 압축)

```csharp
// Binary 양자화 - 32배 압축, 최고 속도
services.AddBinaryQuantization(dimension: 1536);
services.AddQuantizedVectorStoreDecorator(autoQuantize: true);
```

### 3. Product Quantization (균형 잡힌 선택)

```csharp
// Product Quantization - 16-64배 압축
services.AddProductQuantization(
    dimension: 1536,
    numSubvectors: 8,    // 서브벡터 수 (dimension의 약수)
    codebookSize: 256);  // 코드북 크기 (일반적으로 256)

services.AddQuantizedVectorStoreDecorator(autoQuantize: true);
```

## 상세 설정

### QuantizationOptions

```csharp
services.AddVectorQuantization(options =>
{
    options.Dimension = 1536;                    // 벡터 차원
    options.Type = QuantizationType.ScalarInt8;  // 양자화 타입
    options.UseSymmetricQuantization = true;     // 대칭 양자화 사용
    options.NumSubvectors = 8;                   // PQ용: 서브벡터 수
    options.CodebookSize = 256;                  // PQ용: 코드북 크기
    options.KMeansIterations = 20;               // PQ용: K-Means 반복 횟수
});
```

### QuantizedVectorStoreOptions

```csharp
services.AddQuantizedVectorStoreDecorator(options =>
{
    options.AutoQuantizeOnStore = true;      // 저장 시 자동 양자화
    options.UseQuantizedSearch = true;       // 양자화 검색 사용
    options.CandidateMultiplier = 3;         // 후보 배수 (Two-Stage 검색)
    options.MinQuantizedScore = 0.0f;        // 최소 점수 임계값
});
```

## Hybrid Search와 통합

### 양자화 검색 활성화

```csharp
var options = new HybridSearchOptions
{
    UseQuantizedSearch = true,              // 양자화 검색 활성화
    QuantizedCandidateMultiplier = 3,       // 후보 배수 (기본값: 3)
    QuantizedMinScore = 0.0f,               // 최소 점수 임계값

    // 기존 옵션들
    MaxResults = 10,
    FusionMethod = FusionMethod.RRF,
    VectorWeight = 0.7,
    SparseWeight = 0.3
};

var results = await hybridSearchService.SearchAsync(query, options);
```

### Two-Stage 검색 동작

양자화 검색이 활성화되면 다음 두 단계로 검색이 수행됩니다:

1. **1단계 (Candidate Retrieval)**: 양자화된 벡터로 빠른 근사 검색
   - `TopK * CandidateMultiplier` 개의 후보 검색
   - 예: TopK=10, Multiplier=3 → 30개 후보 검색

2. **2단계 (Reranking)**: 원본 벡터로 정확한 리랭킹
   - 후보들을 원본 벡터로 재평가
   - 최종 TopK 결과 반환

## 기존 데이터 마이그레이션

### VectorQuantizationMigrationService 사용

```csharp
// DI 등록
services.AddVectorQuantizationMigration();

// 마이그레이션 실행
var migrationService = serviceProvider.GetRequiredService<VectorQuantizationMigrationService>();

// 전체 마이그레이션
var result = await migrationService.MigrateAllAsync(
    new MigrationOptions
    {
        BatchSize = 100,              // 배치 크기
        TrainingSampleSize = 1000,    // PQ 학습용 샘플 크기
        BatchDelayMs = 100,           // 배치 간 지연 (부하 조절)
        ContinueOnError = true        // 오류 시 계속 진행
    },
    progress: new Progress<MigrationProgress>(p =>
    {
        Console.WriteLine($"진행: {p.ProcessedCount}, 성공: {p.SuccessCount}");
    }),
    cancellationToken);

Console.WriteLine($"마이그레이션 완료: {result.SuccessCount}개 성공, " +
                  $"압축률: {result.CompressionRatio:P2}");
```

### 선택적 마이그레이션

```csharp
// 특정 문서만 마이그레이션
var documentIds = new[] { "doc1", "doc2", "doc3" };
var result = await migrationService.MigrateByDocumentIdsAsync(
    documentIds,
    options: new MigrationOptions { BatchSize = 50 });
```

### 양자화 분석 (테스트용)

```csharp
// 실제 저장 없이 양자화 효과 분석
var vectors = new List<float[]>
{
    new float[] { 0.1f, 0.2f, ... },
    new float[] { 0.3f, 0.4f, ... }
};

var analysis = await migrationService.AnalyzeQuantizationAsync(vectors);

Console.WriteLine($"원본 크기: {analysis.OriginalSizeBytes:N0} bytes");
Console.WriteLine($"양자화 크기: {analysis.QuantizedSizeBytes:N0} bytes");
Console.WriteLine($"압축률: {analysis.CompressionRatio:P2}");
Console.WriteLine($"평균 오차: {analysis.AverageQuantizationError:F6}");
```

## 성능 벤치마크

FluxIndex의 양자화 성능 테스트 결과 (1536차원, 1000개 벡터):

### Scalar Int8 Quantization
- **압축률**: 4x (1536 floats → 1536 bytes)
- **Recall@10**: ~73%
- **거리 계산 속도**: ~2x 향상
- **권장**: 정확도가 중요한 일반 검색

### Binary Quantization
- **압축률**: 32x (1536 floats → 192 bits)
- **Recall@10**: ~54%
- **거리 계산 속도**: ~25x 향상 (PopCount 사용)
- **권장**: 대규모 후보 필터링, 초기 검색 단계

### Product Quantization
- **압축률**: 32-64x (설정에 따라 다름)
- **Recall@10**: ~70-80% (학습 데이터에 따라 다름)
- **거리 계산 속도**: ~5-10x 향상
- **권장**: 메모리 제약이 심한 환경

## 사용 패턴

### 패턴 1: 정확도 우선

```csharp
// Scalar Int8 + 높은 후보 배수
services.AddScalarQuantization(dimension: 1536);
services.AddQuantizedVectorStoreDecorator(options =>
{
    options.AutoQuantizeOnStore = true;
    options.CandidateMultiplier = 5;  // 더 많은 후보
});
```

### 패턴 2: 속도 우선

```csharp
// Binary + 낮은 후보 배수
services.AddBinaryQuantization(dimension: 1536);
services.AddQuantizedVectorStoreDecorator(options =>
{
    options.AutoQuantizeOnStore = true;
    options.CandidateMultiplier = 2;  // 적은 후보
});
```

### 패턴 3: 메모리 최적화

```csharp
// Product Quantization
services.AddProductQuantization(
    dimension: 1536,
    numSubvectors: 16,   // 더 많은 서브벡터 = 더 높은 압축
    codebookSize: 256);
```

### 패턴 4: 양자화 없이 기존 동작 유지

```csharp
// 양자화 서비스 등록하지 않음
// 또는 검색 시 UseQuantizedSearch = false
var options = new HybridSearchOptions
{
    UseQuantizedSearch = false  // 기존 검색 사용
};
```

## API 참조

### IVectorQuantizer

```csharp
public interface IVectorQuantizer
{
    QuantizationType QuantizationType { get; }
    int OriginalDimension { get; }
    int QuantizedDimension { get; }
    bool IsTrained { get; }

    Task<QuantizedVector> QuantizeAsync(float[] vector, CancellationToken ct = default);
    Task<float[]> DequantizeAsync(QuantizedVector quantized, CancellationToken ct = default);
    Task<float> ComputeDistanceAsync(QuantizedVector a, QuantizedVector b, CancellationToken ct = default);
    Task TrainAsync(IReadOnlyList<float[]> trainingVectors, CancellationToken ct = default);
}
```

### IQuantizedVectorStore

```csharp
public interface IQuantizedVectorStore : IVectorStore
{
    IVectorQuantizer? Quantizer { get; }
    bool SupportsQuantization { get; }

    Task<string> StoreWithQuantizedAsync(DocumentChunk chunk, QuantizedVector quantized, CancellationToken ct = default);
    Task<IEnumerable<(DocumentChunk, float)>> SearchQuantizedAsync(QuantizedVector query, int topK = 10, float minScore = 0f, CancellationToken ct = default);
    Task<IEnumerable<(DocumentChunk, float)>> SearchWithRerankAsync(float[] queryEmbedding, QuantizedVector queryQuantized, int topK = 10, int candidateMultiplier = 3, float minScore = 0f, CancellationToken ct = default);
}
```

### HybridSearchOptions (양자화 관련)

```csharp
public record HybridSearchOptions
{
    // 양자화 검색 사용 여부 (Two-Stage 검색)
    public bool UseQuantizedSearch { get; set; } = false;

    // 양자화 검색 후보 배수 (리랭킹 시 원본 결과 대비 후보 수 배율)
    public int QuantizedCandidateMultiplier { get; set; } = 3;

    // 양자화 검색 최소 점수 임계값
    public float QuantizedMinScore { get; set; } = 0.0f;

    // ... 기타 옵션
}
```

## 문제 해결

### Q: 양자화 검색이 동작하지 않습니다

**A**: 다음을 확인하세요:
1. `IVectorQuantizer`가 DI에 등록되어 있는지 확인
2. `IVectorStore`가 `IQuantizedVectorStore`를 구현하는지 확인
3. `HybridSearchService.SupportsQuantizedSearch` 속성 확인

```csharp
var hybridService = serviceProvider.GetRequiredService<IHybridSearchService>();
if (hybridService is HybridSearchService hs)
{
    Console.WriteLine($"양자화 지원: {hs.SupportsQuantizedSearch}");
}
```

### Q: Product Quantization 학습이 필요합니다

**A**: PQ는 사용 전 학습이 필요합니다:

```csharp
var quantizer = serviceProvider.GetRequiredService<IVectorQuantizer>();
if (quantizer.QuantizationType == QuantizationType.ProductQuantization && !quantizer.IsTrained)
{
    var trainingVectors = await GetTrainingVectorsAsync(); // 최소 1000개 권장
    await quantizer.TrainAsync(trainingVectors);
}
```

### Q: 정확도가 너무 낮습니다

**A**: 다음 방법을 시도하세요:
1. `CandidateMultiplier` 값 증가 (3 → 5 → 10)
2. Scalar Int8 양자화로 변경 (Binary 대신)
3. `MinQuantizedScore` 값 조정

### Q: 메모리 사용량이 여전히 높습니다

**A**: Product Quantization 또는 Binary Quantization 사용을 고려하세요:
- Binary: 32x 압축
- PQ (16 subvectors): 64x 압축

## 관련 문서

- [FluxIndex Architecture](architecture.md)
- [Hybrid Search Guide](FLUXINDEX_RAG_SYSTEM.md)
- [Getting Started](getting-started.md)
