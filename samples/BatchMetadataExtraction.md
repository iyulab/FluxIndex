# 배치 메타데이터 추출 샘플

FluxIndex의 배치 메타데이터 추출 기능을 사용하면 여러 문서의 메타데이터를 병렬로 효율적으로 추출할 수 있습니다.

## 기본 배치 추출

```csharp
using FluxIndex.SDK;
using FluxIndex.Core.Models;

var context = new FluxIndexContextBuilder()
    .UseSQLite("batch-index.db")
    .UseOpenAI("your-api-key")
    .WithOpenAIMetadataExtractor(
        apiKey: "your-api-key",
        schema: MetadataSchema.General,
        strategy: MetadataExtractionStrategy.Smart)
    .Build();

// 추출할 문서 목록 준비
var documents = new[]
{
    ("doc1", "AI and Machine Learning in Healthcare..."),
    ("doc2", "Climate Change and Environmental Policy..."),
    ("doc3", "Quantum Computing: Future of Technology...")
};

// 배치 메타데이터 추출 (진행 상황 보고 포함)
var progress = new Progress<BatchMetadataExtractionProgress>(p =>
{
    Console.WriteLine($"[{p.ProgressPercentage:F1}%] {p.Message}");
    Console.WriteLine($"  성공: {p.SuccessfulItems}, 실패: {p.FailedItems}");

    if (p.EstimatedTimeRemaining.HasValue)
    {
        Console.WriteLine($"  예상 남은 시간: {p.EstimatedTimeRemaining.Value:hh\\:mm\\:ss}");
    }
});

var result = await context.Indexer.ExtractMetadataBatchAsync(
    documents: documents,
    schema: MetadataSchema.General,
    strategy: MetadataExtractionStrategy.Smart,
    maxConcurrency: 4,
    progressCallback: progress);

// 결과 출력
Console.WriteLine($"\n배치 추출 완료:");
Console.WriteLine($"  총 문서: {result.TotalItems}");
Console.WriteLine($"  성공: {result.SuccessfulItems}");
Console.WriteLine($"  실패: {result.FailedItems}");
Console.WriteLine($"  처리 시간: {result.ProcessingTime.TotalSeconds:F2}초");
Console.WriteLine($"  평균 신뢰도: {result.Statistics.AverageConfidence:F2}");
```

## 배치 인덱싱 (메타데이터 자동 추출 포함)

```csharp
// 여러 문서를 인덱싱하면서 자동으로 메타데이터 추출
var documents = new[]
{
    ("article1", "Content of article 1...", new Dictionary<string, object>
    {
        { "author", "John Doe" },
        { "publishedDate", "2024-01-15" }
    }),
    ("article2", "Content of article 2...", new Dictionary<string, object>
    {
        { "author", "Jane Smith" },
        { "publishedDate", "2024-01-20" }
    }),
    ("article3", "Content of article 3...", null)
};

var progress = new Progress<BatchProgress>(p =>
{
    Console.WriteLine($"[{p.ProgressPercentage:F1}%] {p.Message}");
});

var result = await context.Indexer.IndexDocumentsBatchAsync(
    documents: documents,
    options: new IndexingOptions(),
    progressCallback: progress);

Console.WriteLine($"\n배치 인덱싱 완료:");
Console.WriteLine($"  총 문서: {result.TotalDocuments}");
Console.WriteLine($"  성공: {result.SuccessfulDocuments}");
Console.WriteLine($"  실패: {result.FailedDocuments}");
Console.WriteLine($"  처리 시간: {result.TotalProcessingTime.TotalSeconds:F2}초");
```

## 대용량 문서 배치 처리

```csharp
// 1000개의 문서를 배치로 처리
var largeDocumentSet = Enumerable.Range(1, 1000)
    .Select(i => ($"doc{i}", $"Content of document {i}..."))
    .ToList();

// 진행 상황 모니터링
var progress = new Progress<BatchMetadataExtractionProgress>(p =>
{
    if (p.CurrentItemIndex % 10 == 0) // 10개마다 로그
    {
        Console.WriteLine(
            $"[{p.ProgressPercentage:F1}%] 처리 중: {p.CurrentItemIndex}/{p.TotalItems}, " +
            $"성공: {p.SuccessfulItems}, 실패: {p.FailedItems}");
    }
});

var result = await context.Indexer.ExtractMetadataBatchAsync(
    documents: largeDocumentSet,
    schema: MetadataSchema.General,
    strategy: MetadataExtractionStrategy.Fast, // 비용 절감을 위해 Fast 전략 사용
    maxConcurrency: 8, // 높은 병렬 처리
    progressCallback: progress);

Console.WriteLine($"\n대용량 배치 처리 완료:");
Console.WriteLine($"  처리 시간: {result.ProcessingTime.TotalMinutes:F2}분");
Console.WriteLine($"  평균 처리 시간: {result.Statistics.AverageProcessingTime.TotalSeconds:F2}초/문서");
```

## 배치 통계 활용

```csharp
var result = await context.Indexer.ExtractMetadataBatchAsync(
    documents: documents,
    schema: MetadataSchema.Article);

// 가장 많이 나타난 주제 확인
Console.WriteLine("\n상위 주제:");
foreach (var (topic, count) in result.Statistics.TopTopics.Take(5))
{
    Console.WriteLine($"  {topic}: {count}회");
}

// 가장 많이 나타난 키워드 확인
Console.WriteLine("\n상위 키워드:");
foreach (var (keyword, count) in result.Statistics.TopKeywords.Take(10))
{
    Console.WriteLine($"  {keyword}: {count}회");
}

// 문서 타입 분포
Console.WriteLine("\n문서 타입 분포:");
foreach (var (docType, count) in result.Statistics.DocumentTypeDistribution)
{
    Console.WriteLine($"  {docType}: {count}개");
}

// 언어 분포
Console.WriteLine("\n언어 분포:");
foreach (var (language, count) in result.Statistics.LanguageDistribution)
{
    Console.WriteLine($"  {language}: {count}개");
}
```

## 스키마별 배치 처리

### 제품 매뉴얼 배치 추출

```csharp
var manuals = new[]
{
    ("manual1", "iPhone 15 Pro User Manual..."),
    ("manual2", "Samsung Galaxy S24 User Guide..."),
    ("manual3", "MacBook Pro M3 Setup Guide...")
};

var result = await context.Indexer.ExtractMetadataBatchAsync(
    documents: manuals,
    schema: MetadataSchema.ProductManual,
    strategy: MetadataExtractionStrategy.Deep);

foreach (var itemResult in result.ItemResults.Where(r => r.Success))
{
    var metadata = itemResult.Metadata!;
    var productName = metadata.SchemaSpecificData["productName"]?.ToString();
    var manufacturer = metadata.SchemaSpecificData["manufacturer"]?.ToString();

    Console.WriteLine($"{itemResult.DocumentId}:");
    Console.WriteLine($"  제품: {productName}");
    Console.WriteLine($"  제조사: {manufacturer}");
    Console.WriteLine($"  신뢰도: {metadata.OverallConfidence:F2}");
}
```

### 기술 문서 배치 추출

```csharp
var techDocs = new[]
{
    ("api-v1", "REST API Documentation v1.0..."),
    ("api-v2", "REST API Documentation v2.0..."),
    ("sdk-guide", "SDK Integration Guide...")
};

var result = await context.Indexer.ExtractMetadataBatchAsync(
    documents: techDocs,
    schema: MetadataSchema.TechnicalDoc,
    strategy: MetadataExtractionStrategy.Smart);

foreach (var itemResult in result.ItemResults.Where(r => r.Success))
{
    var metadata = itemResult.Metadata!;
    var apiVersion = metadata.SchemaSpecificData["apiVersion"]?.ToString();
    var framework = metadata.SchemaSpecificData["framework"]?.ToString();

    Console.WriteLine($"{itemResult.DocumentId}:");
    Console.WriteLine($"  API 버전: {apiVersion}");
    Console.WriteLine($"  프레임워크: {framework}");
}
```

## 오류 처리 및 재시도

```csharp
var result = await context.Indexer.ExtractMetadataBatchAsync(
    documents: documents,
    maxConcurrency: 4,
    progressCallback: progress);

// 실패한 문서 재처리
var failedDocs = result.ItemResults
    .Where(r => !r.Success)
    .Select(r => (r.DocumentId, documents.First(d => d.Item1 == r.DocumentId).Item2))
    .ToList();

if (failedDocs.Any())
{
    Console.WriteLine($"\n{failedDocs.Count}개 문서 재시도 중...");

    var retryResult = await context.Indexer.ExtractMetadataBatchAsync(
        documents: failedDocs,
        strategy: MetadataExtractionStrategy.Deep, // 더 높은 품질로 재시도
        maxConcurrency: 2); // 낮은 동시성으로 안정성 향상

    Console.WriteLine($"재시도 결과: {retryResult.SuccessfulItems}/{retryResult.TotalItems} 성공");
}
```

## 비용 최적화 전략

```csharp
// 전략 1: Fast 전략으로 빠르게 처리
var fastResult = await context.Indexer.ExtractMetadataBatchAsync(
    documents: documents,
    strategy: MetadataExtractionStrategy.Fast,
    maxConcurrency: 10); // 높은 병렬 처리로 시간 단축

// 전략 2: 낮은 신뢰도 문서만 Deep 전략으로 재처리
var lowConfidenceDocs = fastResult.ItemResults
    .Where(r => r.Success && r.Metadata!.OverallConfidence < 0.7f)
    .Select(r => (r.DocumentId, documents.First(d => d.Item1 == r.DocumentId).Item2))
    .ToList();

if (lowConfidenceDocs.Any())
{
    var deepResult = await context.Indexer.ExtractMetadataBatchAsync(
        documents: lowConfidenceDocs,
        strategy: MetadataExtractionStrategy.Deep);

    Console.WriteLine($"재처리로 {deepResult.SuccessfulItems}개 문서 품질 향상");
}
```

## 실시간 진행 상황 UI

```csharp
var progressBar = new ProgressBar(); // 가상의 진행 바 UI

var progress = new Progress<BatchMetadataExtractionProgress>(p =>
{
    progressBar.Update(p.ProgressPercentage / 100.0);
    progressBar.SetMessage($"처리 중: {p.CurrentDocumentId}");
    progressBar.SetStatus($"{p.SuccessfulItems}개 성공, {p.FailedItems}개 실패");

    if (p.EstimatedTimeRemaining.HasValue)
    {
        progressBar.SetTimeRemaining(p.EstimatedTimeRemaining.Value);
    }
});

var result = await context.Indexer.ExtractMetadataBatchAsync(
    documents: documents,
    progressCallback: progress);

progressBar.Complete();
```

## 병렬 처리 수준 최적화

```csharp
// 시스템 리소스에 따른 동적 병렬 처리
var cpuCores = Environment.ProcessorCount;
var optimalConcurrency = Math.Max(2, cpuCores / 2);

Console.WriteLine($"최적 병렬 처리 수준: {optimalConcurrency}");

var result = await context.Indexer.ExtractMetadataBatchAsync(
    documents: largeDocumentSet,
    maxConcurrency: optimalConcurrency);
```

## 요약

배치 메타데이터 추출 기능의 핵심 장점:

1. **병렬 처리**: 여러 문서를 동시에 처리하여 시간 단축
2. **진행 상황 보고**: 실시간으로 처리 진행 상황 모니터링
3. **오류 복원력**: 일부 문서 실패 시에도 계속 진행
4. **통계 제공**: 배치 처리 결과에 대한 종합 통계
5. **비용 최적화**: 전략별 처리 및 캐싱으로 API 비용 절감
6. **유연한 구성**: 동시성, 전략, 스키마 등 세밀한 제어

대용량 문서 처리 시 배치 API를 사용하면 효율성과 비용 효과를 크게 향상시킬 수 있습니다.
