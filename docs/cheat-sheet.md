# FluxIndex 치트시트

빠른 참조를 위한 핵심 코드 패턴 모음

## 📦 패키지 설치

```bash
# 필수
dotnet add package FluxIndex.SDK

# 저장소 (택1)
dotnet add package FluxIndex.Storage.SQLite      # 개발용
dotnet add package FluxIndex.Storage.PostgreSQL  # 프로덕션

# AI Provider (선택)
dotnet add package FluxIndex.AI.OpenAI

# 확장 기능 (선택)
dotnet add package FluxIndex.AI.LocalReranker     # 신경망 리랭킹
dotnet add package FluxIndex.Extensions.FileFlux  # 문서 파일 처리
dotnet add package FluxIndex.Extensions.WebFlux   # 웹페이지 처리
dotnet add package FluxIndex.Cache.Redis          # Redis 캐싱
```

## ⚡ 빠른 시작

### 1. 기본 설정

```csharp
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddFluxIndex()
    .AddSQLiteVectorStore()     // 인메모리 저장소
    .UseInMemoryCache();        // 기본 캐시

var client = services.BuildServiceProvider()
    .GetRequiredService<FluxIndexClient>();
```

### 2. 문서 인덱싱

```csharp
// 단순 텍스트 인덱싱
var docId = await client.Indexer.IndexDocumentAsync(
    "FluxIndex RAG 라이브러리", "doc-001");

// 메타데이터 포함
await client.Indexer.IndexDocumentAsync(
    content: "상세 내용...",
    documentId: "doc-002",
    metadata: new Dictionary<string, object>
    {
        ["category"] = "tech",
        ["author"] = "개발자"
    });
```

### 3. 검색

```csharp
// 기본 검색
var results = await client.Retriever.SearchAsync("검색어", topK: 5);

// 하이브리드 검색
var hybridResults = await client.Retriever.SearchAsync(
    "검색어",
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.Hybrid,
        VectorWeight = 0.7f,
        KeywordWeight = 0.3f
    });
```

## 🔧 설정 패턴

### OpenAI 설정

```csharp
// appsettings.json
{
  "OpenAI": {
    "ApiKey": "sk-...",
    "ModelName": "text-embedding-3-small"
  }
}

// 코드
services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseOpenAIEmbedding(config.GetSection("OpenAI"));
```

### PostgreSQL 설정

```csharp
services.AddFluxIndex()
    .UsePostgreSQLVectorStore(options => {
        options.ConnectionString = "Host=localhost;Database=vectordb";
        options.EmbeddingDimensions = 1536;
        options.AutoMigrate = true;
    });
```

### Redis 캐싱

```csharp
services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseRedisCache("localhost:6379");
```

## 📄 파일 처리

### PDF, DOCX 처리

```csharp
using FluxIndex.Extensions.FileFlux;

// FluxIndex 기본 설정
services.AddFluxIndex()
    .AddSQLiteVectorStore();

// FileFlux 확장 추가
services.AddFileFlux(options => {
    options.DefaultChunkingStrategy = "Semantic";
    options.DefaultMaxChunkSize = 1024;
    options.DefaultOverlapSize = 128;
});

var serviceProvider = services.BuildServiceProvider();
var fileFlux = serviceProvider.GetRequiredService<FileFluxIntegration>();

// 파일 인덱싱
var documentId = await fileFlux.ProcessAndIndexAsync("document.pdf");
```

### 웹페이지 처리

```csharp
// WebFlux 설정
services.UseWebFlux();

// 웹페이지 인덱싱
await client.Indexer.IndexWebPageAsync(
    "https://example.com",
    "web-001");
```

## 🔍 검색 옵션

### 검색 전략

```csharp
// 벡터 검색만
SearchStrategy.Vector

// 키워드 검색만
SearchStrategy.Keyword

// 하이브리드 (벡터 + 키워드)
SearchStrategy.Hybrid

// 적응형 (자동 선택)
SearchStrategy.Adaptive
```

### 고급 검색 옵션

```csharp
var results = await client.Retriever.SearchAsync(
    query: "질의어",
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.Adaptive,
        MinScore = 0.7f,                    // 최소 점수
        UseCache = true,                    // 캐시 사용
        CacheTTL = TimeSpan.FromHours(1),   // 캐시 만료
        VectorWeight = 0.7f,                // 벡터 가중치
        KeywordWeight = 0.3f                // 키워드 가중치
    });
```

## 📊 배치 처리

### 대량 인덱싱

```csharp
var documents = new[]
{
    new IndexRequest("내용1", "doc-001"),
    new IndexRequest("내용2", "doc-002"),
    new IndexRequest("내용3", "doc-003")
};

await client.Indexer.IndexBatchAsync(
    documents: documents,
    options: new IndexingOptions
    {
        BatchSize = 100,
        MaxParallelism = 4
    });
```

## 🎯 실전 패턴

### 완전한 설정

```csharp
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());

services.AddFluxIndex()
    .UsePostgreSQLVectorStore(config.GetSection("Database"))
    .UseOpenAIEmbedding(config.GetSection("OpenAI"))
    .UseRedisCache(config.GetConnectionString("Redis"));

// FileFlux 확장 추가
services.AddFileFlux(options => {
    options.DefaultChunkingStrategy = "Semantic";
    options.DefaultMaxChunkSize = 1024;
    options.DefaultOverlapSize = 128;
});
```

### 에러 처리

```csharp
try
{
    var results = await client.Retriever.SearchAsync("질의어");
    foreach (var result in results)
    {
        Console.WriteLine($"{result.DocumentId}: {result.Score:F2}");
    }
}
catch (FluxIndexException ex)
{
    logger.LogError(ex, "FluxIndex 오류: {Message}", ex.Message);
}
catch (Exception ex)
{
    logger.LogError(ex, "예상치 못한 오류");
}
```

## 🎯 리랭킹

### LocalReranker 설정

```csharp
using FluxIndex.AI.LocalReranker;

// Resilient (권장 - 자동 폴백)
services.AddResilientLocalReranker(options => {
    options.ModelId = "quality";  // fast, quality, multilingual
    options.WarmupOnStartup = true;
});

// 또는 SDK 빌더
var context = FluxIndexContext.CreateBuilder()
    .UseSQLite("fluxindex.db")
    .UseResilientLocalReranker(options => options.ModelId = "quality")
    .Build();
```

### 모델 선택

| 모델 | 속도 | 품질 | 다국어 |
|------|------|------|--------|
| `fast` | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ❌ |
| `quality` | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ❌ |
| `multilingual` | ⭐⭐ | ⭐⭐⭐⭐ | ✅ |

## 🚀 성능 팁

1. **배치 인덱싱**: 대량 문서는 `IndexBatchAsync` 사용
2. **캐싱 활용**: Redis 캐시로 반복 검색 성능 향상
3. **적응형 검색**: 쿼리 복잡도에 따른 자동 최적화
4. **PostgreSQL**: 프로덕션 환경에서는 PostgreSQL + pgvector 사용
5. **메타데이터**: 검색 필터링을 위한 메타데이터 적극 활용
6. **리랭킹**: `ResilientLocalReranker`로 품질 향상 + 안정성 확보

## 🔗 관련 문서

- [상세 튜토리얼](TUTORIAL.md)
- [아키텍처 가이드](architecture.md)
- [LocalReranker 가이드](LOCAL_RERANKER_GUIDE.md)
- [샘플 코드](../samples/)