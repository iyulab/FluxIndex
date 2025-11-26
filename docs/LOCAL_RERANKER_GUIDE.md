# LocalReranker Integration Guide

FluxIndex와 LocalReranker 통합 가이드 - 크로스-인코더 기반 신경망 리랭킹

## 개요

LocalReranker는 ONNX 기반 크로스-인코더 모델을 사용하여 검색 결과를 재순위화합니다. FluxIndex는 두 가지 어댑터를 제공합니다:

| 어댑터 | 설명 | 사용 시나리오 |
|--------|------|--------------|
| `LocalRerankerAdapter` | 표준 시맨틱 리랭킹 | 모델이 항상 사용 가능한 환경 |
| `ResilientRerankerAdapter` | 시맨틱 + 알고리즘 폴백 | 프로덕션 환경 (권장) |

## 설치

```bash
dotnet add package FluxIndex.AI.LocalReranker
```

## 빠른 시작

### 옵션 1: Resilient 어댑터 (권장)

```csharp
using FluxIndex.SDK;
using FluxIndex.AI.LocalReranker;

var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI(apiKey, "text-embedding-3-small")
    .UseResilientLocalReranker(options =>
    {
        options.ModelId = "quality";  // "fast", "quality", "multilingual"
    })
    .Build();
```

### 옵션 2: 표준 어댑터

```csharp
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .UseOpenAI(apiKey, "text-embedding-3-small")
    .UseLocalReranker(options =>
    {
        options.ModelId = "quality";
        options.WarmupOnStartup = true;  // 콜드 스타트 방지
    })
    .Build();
```

## DI 컨테이너 등록

```csharp
// Resilient (권장)
services.AddResilientLocalReranker(options =>
{
    options.ModelId = "quality";
});

// 또는 Warmup 포함
services.AddResilientLocalRerankerWithWarmup(options =>
{
    options.ModelId = "quality";
});

// 표준
services.AddLocalReranker(options =>
{
    options.ModelId = "quality";
});
```

## 옵션 설정

```csharp
var options = new LocalRerankerOptions
{
    // 모델 선택 (fast < quality < multilingual)
    ModelId = "quality",

    // 최대 시퀀스 길이 (기본값: 512)
    MaxSequenceLength = 512,

    // GPU 사용 (기본값: false)
    UseGpu = false,

    // 배치 크기 (기본값: 32)
    BatchSize = 32,

    // 스레드 수 (기본값: 자동)
    ThreadCount = null,

    // 모델 캐시 디렉토리
    CacheDirectory = null,

    // 시작 시 웜업 (기본값: false)
    WarmupOnStartup = true
};
```

## 모델 선택

| 모델 ID | 크기 | 다국어 | 속도 | 품질 |
|---------|------|--------|------|------|
| `fast` | ~25MB | ❌ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ |
| `quality` | ~100MB | ❌ | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| `multilingual` | ~280MB | ✅ | ⭐⭐ | ⭐⭐⭐⭐ |

## Resilient 어댑터 동작

```
시작 시:
├─ 모델 로드 성공 → Semantic 모드 (고품질)
└─ 모델 로드 실패 → Algorithmic 모드 (폴백)

런타임:
├─ Semantic 추론 성공 → 결과 반환
└─ Semantic 추론 실패 → Algorithmic 폴백 → 결과 반환
```

### 폴백 트리거 조건

- 모델 다운로드 실패 (네트워크 문제)
- 모델 로드 실패 (디스크/메모리 문제)
- 런타임 추론 실패 (예상치 못한 오류)

### 현재 모드 확인

```csharp
var adapter = serviceProvider.GetRequiredService<ResilientRerankerAdapter>();

Console.WriteLine($"Current method: {adapter.CurrentMethod}");
Console.WriteLine($"Semantic available: {adapter.IsSemanticAvailable}");
```

## 리랭킹 옵션

```csharp
var rerankOptions = new RerankOptions
{
    TopN = 10,                    // 상위 N개 결과
    ScoreThreshold = 0.5f,        // 최소 점수
    MaxContentLength = 512,       // 최대 콘텐츠 길이
    IncludeExplanation = true     // 설명 포함
};

var results = await reranker.RerankAsync(query, candidates, rerankOptions);
```

## 알고리즘 폴백 구성

ResilientRerankerAdapter의 폴백은 TF-IDF + BM25 조합을 사용합니다:

```csharp
// 폴백 가중치 (고정)
TfIdfWeight = 0.4f
Bm25Weight = 0.3f
SemanticWeight = 0.3f  // 임베딩 서비스 있을 경우
```

## 모델 정보 조회

```csharp
var modelInfo = reranker.GetModelInfo();

Console.WriteLine($"Name: {modelInfo.Name}");
Console.WriteLine($"Type: {modelInfo.Type}");
Console.WriteLine($"Max input: {modelInfo.MaxInputLength}");
Console.WriteLine($"Capabilities: {string.Join(", ", modelInfo.Capabilities.Keys)}");
```

## 성능 최적화

### 1. 웜업으로 콜드 스타트 방지

```csharp
// 방법 1: 옵션으로 설정
options.WarmupOnStartup = true;

// 방법 2: HostedService 사용
services.AddLocalRerankerWithWarmup();
```

### 2. 배치 크기 조정

```csharp
options.BatchSize = 64;  // 메모리가 충분하면 증가
```

### 3. GPU 가속

```csharp
options.UseGpu = true;  // CUDA 지원 필요
```

## 에러 처리

```csharp
try
{
    var results = await reranker.RerankAsync(query, candidates);
}
catch (ObjectDisposedException)
{
    // 어댑터가 이미 dispose됨
}
```

## 리소스 정리

```csharp
// IDisposable
using var adapter = new LocalRerankerAdapter(options);

// IAsyncDisposable
await using var adapter = new ResilientRerankerAdapter(options);
```

## 테스트

```csharp
[Fact]
public async Task RerankAsync_ShouldReorderByRelevance()
{
    // Arrange
    var options = Options.Create(new LocalRerankerOptions
    {
        ModelId = "fast",
        WarmupOnStartup = false
    });
    using var adapter = new LocalRerankerAdapter(options);

    var candidates = new List<RetrievalCandidate>
    {
        new() { Id = "1", Content = "Machine learning...", InitialRank = 1 },
        new() { Id = "2", Content = "Weather forecast...", InitialRank = 2 }
    };

    // Act
    var results = await adapter.RerankAsync("What is ML?", candidates);

    // Assert
    results.Should().NotBeEmpty();
    results.First().Content.Should().Contain("Machine learning");
}
```

## 관련 문서

- [Architecture](./architecture.md) - 전체 아키텍처
- [Tutorial](./TUTORIAL.md) - 종합 튜토리얼
- [Cheat Sheet](./cheat-sheet.md) - 빠른 참조
