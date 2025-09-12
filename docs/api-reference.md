# FluxIndex API 레퍼런스

## 목차
- [FluxIndexClient](#fluxindexclient)
- [FluxIndexClientBuilder](#fluxindexclientbuilder)
- [Indexer](#indexer)
- [Retriever](#retriever)
- [Models](#models)
- [Options](#options)
- [Exceptions](#exceptions)

---

## FluxIndexClient

FluxIndex의 메인 진입점입니다.

### 속성

```csharp
public IIndexer Indexer { get; }
public IRetriever Retriever { get; }
public IMaintenanceService Maintenance { get; }
```

### 메서드

#### IndexAndSearchAsync
단일 메서드로 인덱싱과 검색을 수행합니다.

```csharp
public async Task<SearchResults> IndexAndSearchAsync(
    IEnumerable<Document> documents,
    string query,
    SearchOptions? options = null,
    CancellationToken cancellationToken = default)
```

**매개변수:**
- `documents`: 인덱싱할 문서들
- `query`: 검색 쿼리
- `options`: 검색 옵션 (선택)
- `cancellationToken`: 취소 토큰

**반환값:** `SearchResults` - 검색 결과

---

## FluxIndexClientBuilder

Fluent API를 사용한 클라이언트 빌더입니다.

### 메서드

#### ConfigureVectorStore
벡터 스토어를 설정합니다.

```csharp
public FluxIndexClientBuilder ConfigureVectorStore(
    VectorStoreType type,
    Action<VectorStoreOptions>? configure = null)
```

**사용 예:**
```csharp
builder.ConfigureVectorStore(VectorStoreType.PostgreSQL, options =>
{
    options.ConnectionString = "Host=localhost;Database=fluxindex";
    options.VectorDimension = 1536;
    options.CreateIndexIfNotExists = true;
});
```

#### ConfigureEmbeddingService
임베딩 서비스를 설정합니다.

```csharp
public FluxIndexClientBuilder ConfigureEmbeddingService(
    Action<EmbeddingServiceConfiguration> configure)
```

**사용 예:**
```csharp
// OpenAI
builder.ConfigureEmbeddingService(config => 
    config.UseOpenAI("api-key"));

// Azure OpenAI
builder.ConfigureEmbeddingService(config => 
    config.UseAzureOpenAI(
        endpoint: "https://resource.openai.azure.com/",
        apiKey: "key",
        deploymentName: "ada-002"));

// 로컬 전용
builder.ConfigureEmbeddingService(config => 
    config.UseLocalOnly());
```

#### ConfigureCache
캐시를 설정합니다.

```csharp
public FluxIndexClientBuilder ConfigureCache(
    CacheType type,
    Action<CacheOptions>? configure = null)
```

#### ConfigureSearchOptions
기본 검색 옵션을 설정합니다.

```csharp
public FluxIndexClientBuilder ConfigureSearchOptions(
    Action<SearchOptions> configure)
```

#### UseLocalSearchOnly
로컬 검색만 사용하도록 설정합니다.

```csharp
public FluxIndexClientBuilder UseLocalSearchOnly()
```

#### Build
설정된 옵션으로 클라이언트를 생성합니다.

```csharp
public FluxIndexClient Build()
```

---

## Indexer

문서 인덱싱을 담당합니다.

### 메서드

#### IndexDocumentsAsync
문서들을 인덱싱합니다.

```csharp
public async Task<IndexingResult> IndexDocumentsAsync(
    IEnumerable<Document> documents,
    IndexingOptions? options = null,
    CancellationToken cancellationToken = default)
```

**매개변수:**
- `documents`: 인덱싱할 문서들
- `options`: 인덱싱 옵션
- `cancellationToken`: 취소 토큰

**반환값:** `IndexingResult` - 인덱싱 결과

#### IndexDocumentsStreamAsync
대량 문서를 스트리밍으로 인덱싱합니다.

```csharp
public async IAsyncEnumerable<IndexingBatch> IndexDocumentsStreamAsync(
    IAsyncEnumerable<Document> documents,
    IndexingOptions? options = null,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
```

#### UpdateDocumentAsync
기존 문서를 업데이트합니다.

```csharp
public async Task<bool> UpdateDocumentAsync(
    string documentId,
    string newContent,
    Dictionary<string, object>? newMetadata = null,
    CancellationToken cancellationToken = default)
```

#### DeleteDocumentAsync
문서를 삭제합니다.

```csharp
public async Task<bool> DeleteDocumentAsync(
    string documentId,
    CancellationToken cancellationToken = default)
```

#### DeleteDocumentsByMetadataAsync
메타데이터 필터로 문서들을 삭제합니다.

```csharp
public async Task<int> DeleteDocumentsByMetadataAsync(
    Dictionary<string, object> metadataFilter,
    CancellationToken cancellationToken = default)
```

---

## Retriever

문서 검색을 담당합니다.

### 메서드

#### SearchAsync
쿼리로 문서를 검색합니다.

```csharp
public async Task<SearchResults> SearchAsync(
    string query,
    SearchOptions? options = null,
    CancellationToken cancellationToken = default)
```

**매개변수:**
- `query`: 검색 쿼리
- `options`: 검색 옵션
- `cancellationToken`: 취소 토큰

**반환값:** `SearchResults` - 검색 결과

#### HybridSearchAsync
벡터와 키워드 검색을 결합합니다.

```csharp
public async Task<SearchResults> HybridSearchAsync(
    string query,
    HybridSearchOptions? options = null,
    CancellationToken cancellationToken = default)
```

#### SearchByVectorAsync
벡터로 직접 검색합니다.

```csharp
public async Task<SearchResults> SearchByVectorAsync(
    float[] vector,
    SearchOptions? options = null,
    CancellationToken cancellationToken = default)
```

#### SearchWithFilterAsync
메타데이터 필터와 함께 검색합니다.

```csharp
public async Task<SearchResults> SearchWithFilterAsync(
    string query,
    Dictionary<string, object> metadataFilter,
    SearchOptions? options = null,
    CancellationToken cancellationToken = default)
```

#### MultiQuerySearchAsync
여러 쿼리로 동시에 검색합니다.

```csharp
public async Task<MultiSearchResults> MultiQuerySearchAsync(
    IEnumerable<string> queries,
    SearchOptions? options = null,
    CancellationToken cancellationToken = default)
```

---

## Models

### Document
인덱싱할 문서를 나타냅니다.

```csharp
public class Document
{
    public string Id { get; set; }
    public string Content { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    public DateTime? CreatedAt { get; set; }
    public float[]? Embedding { get; set; }
}
```

### SearchResults
검색 결과를 나타냅니다.

```csharp
public class SearchResults
{
    public List<SearchResult> Documents { get; set; }
    public int TotalCount { get; set; }
    public TimeSpan SearchTime { get; set; }
    public string? NextPageToken { get; set; }
    public Dictionary<string, object>? DebugInfo { get; set; }
}
```

### SearchResult
개별 검색 결과입니다.

```csharp
public class SearchResult
{
    public string Id { get; set; }
    public string Content { get; set; }
    public float Score { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    public string? Highlight { get; set; }
}
```

### IndexingResult
인덱싱 결과입니다.

```csharp
public class IndexingResult
{
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<IndexingError>? Errors { get; set; }
    public TimeSpan Duration { get; set; }
}
```

---

## Options

### SearchOptions
검색 옵션입니다.

```csharp
public class SearchOptions
{
    public int TopK { get; set; } = 10;
    public float MinimumScore { get; set; } = 0.0f;
    public SearchType SearchType { get; set; } = SearchType.Hybrid;
    public bool UseReranking { get; set; } = false;
    public bool UseCache { get; set; } = true;
    public TimeSpan? CacheDuration { get; set; }
    public Dictionary<string, object>? MetadataFilters { get; set; }
    public bool IncludeDebugInfo { get; set; } = false;
}
```

### HybridSearchOptions
하이브리드 검색 옵션입니다.

```csharp
public class HybridSearchOptions : SearchOptions
{
    public float VectorWeight { get; set; } = 0.7f;
    public float KeywordWeight { get; set; } = 0.3f;
    public RankFusionMethod FusionMethod { get; set; } = RankFusionMethod.RRF;
}
```

### IndexingOptions
인덱싱 옵션입니다.

```csharp
public class IndexingOptions
{
    public int BatchSize { get; set; } = 100;
    public int ParallelDegree { get; set; } = 1;
    public bool OverwriteExisting { get; set; } = false;
    public bool GenerateEmbeddings { get; set; } = true;
    public bool ValidateDocuments { get; set; } = true;
}
```

### VectorStoreOptions
벡터 스토어 옵션입니다.

```csharp
public class VectorStoreOptions
{
    public string? ConnectionString { get; set; }
    public int VectorDimension { get; set; } = 1536;
    public bool CreateIndexIfNotExists { get; set; } = true;
    public string? IndexName { get; set; }
    public DistanceMetric DistanceMetric { get; set; } = DistanceMetric.Cosine;
}
```

---

## Enums

### VectorStoreType
```csharp
public enum VectorStoreType
{
    InMemory,
    PostgreSQL,
    SQLite,
    Redis,
    Custom
}
```

### SearchType
```csharp
public enum SearchType
{
    Vector,
    Keyword,
    Hybrid
}
```

### CacheType
```csharp
public enum CacheType
{
    None,
    InMemory,
    Redis,
    Distributed
}
```

### DistanceMetric
```csharp
public enum DistanceMetric
{
    Cosine,
    Euclidean,
    DotProduct
}
```

### RankFusionMethod
```csharp
public enum RankFusionMethod
{
    RRF,  // Reciprocal Rank Fusion
    Linear,
    Weighted
}
```

---

## Exceptions

### FluxIndexException
기본 예외 클래스입니다.

```csharp
public class FluxIndexException : Exception
{
    public string? ErrorCode { get; set; }
    public Dictionary<string, object>? Details { get; set; }
}
```

### IndexingException
인덱싱 중 발생하는 예외입니다.

```csharp
public class IndexingException : FluxIndexException
{
    public List<Document>? FailedDocuments { get; set; }
}
```

### SearchException
검색 중 발생하는 예외입니다.

```csharp
public class SearchException : FluxIndexException
{
    public string Query { get; set; }
    public SearchOptions? Options { get; set; }
}
```

### EmbeddingException
임베딩 생성 중 발생하는 예외입니다.

```csharp
public class EmbeddingException : FluxIndexException
{
    public string? Provider { get; set; }
    public int? StatusCode { get; set; }
}
```

---

## 고급 사용법

### 커스텀 임베딩 서비스
```csharp
public class CustomEmbeddingService : IEmbeddingService
{
    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        // 커스텀 임베딩 로직
        return await YourEmbeddingAPI.GetEmbedding(text);
    }
}

// 등록
services.AddSingleton<IEmbeddingService, CustomEmbeddingService>();
```

### 커스텀 재순위화
```csharp
public class CustomReranker : IReranker
{
    public async Task<IEnumerable<SearchResult>> RerankAsync(
        string query,
        IEnumerable<SearchResult> results,
        CancellationToken cancellationToken = default)
    {
        // 커스텀 재순위화 로직
        return results.OrderByDescending(r => CustomScore(query, r));
    }
}
```

### 메트릭 수집
```csharp
client.Metrics.OnSearchCompleted += (sender, args) =>
{
    telemetry.TrackMetric("search.latency", args.Duration.TotalMilliseconds);
    telemetry.TrackMetric("search.results", args.ResultCount);
};
```

### 벌크 작업
```csharp
// 대량 인덱싱
using var bulkIndexer = client.CreateBulkIndexer(new BulkIndexerOptions
{
    MaxConcurrency = 4,
    FlushInterval = TimeSpan.FromSeconds(10),
    MaxBatchSize = 1000
});

foreach (var doc in documents)
{
    await bulkIndexer.AddAsync(doc);
}

await bulkIndexer.FlushAsync();
```

---

## 성능 팁

1. **배치 크기 최적화**: 네트워크와 메모리 사용량에 따라 배치 크기 조정
2. **캐싱 활용**: 자주 사용되는 쿼리는 캐싱
3. **비동기 처리**: 가능한 모든 작업을 비동기로 처리
4. **연결 풀링**: 데이터베이스 연결 풀 사용
5. **인덱스 최적화**: 주기적으로 벡터 인덱스 최적화

---

## 버전 호환성

| FluxIndex 버전 | .NET 버전 | OpenAI API | PostgreSQL |
|---------------|----------|------------|------------|
| 1.0.x         | 9.0+     | v1         | 14+        |
| 0.1.x         | 8.0+     | v1         | 13+        |