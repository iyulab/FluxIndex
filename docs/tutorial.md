# FluxIndex 튜토리얼

소비 앱에서 FluxIndex RAG 라이브러리를 활용하는 간결한 단계별 가이드

## 목차

1. [기본 설정](#1-기본-설정)
2. [간단한 인덱싱과 검색](#2-간단한-인덱싱과-검색)
3. [AI Provider 연동](#3-ai-provider-연동)
4. [하이브리드 검색](#4-하이브리드-검색)
5. [문서 파일 처리](#5-문서-파일-처리)
6. [성능 최적화](#6-성능-최적화)

---

## 1. 기본 설정

### 패키지 설치

```bash
# 필수 패키지
dotnet add package FluxIndex.SDK

# 저장소 (하나 선택)
dotnet add package FluxIndex.Storage.SQLite      # 개발용
dotnet add package FluxIndex.Storage.PostgreSQL  # 프로덕션용

# AI Provider (선택적)
dotnet add package FluxIndex.AI.OpenAI
```

### 최소 설정

```csharp
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// FluxIndex 기본 설정
services.AddFluxIndex()
    .AddSQLiteVectorStore()                    // 인메모리 저장소
    .UseInMemoryCache();                       // 기본 캐싱

var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<FluxIndexClient>();
```

---

## 2. 간단한 인덱싱과 검색

### 문서 인덱싱

```csharp
// 단일 문서 인덱싱
var docId = await client.Indexer.IndexDocumentAsync(
    content: "FluxIndex는 .NET RAG 라이브러리입니다.",
    documentId: "doc-001"
);

// 메타데이터와 함께 인덱싱
var docWithMeta = await client.Indexer.IndexDocumentAsync(
    content: "AI와 머신러닝에 대한 상세한 설명...",
    documentId: "doc-002",
    metadata: new Dictionary<string, object>
    {
        ["category"] = "AI",
        ["author"] = "홍길동",
        ["created"] = DateTime.Now
    }
);
```

### 기본 검색

```csharp
// 간단한 검색
var results = await client.Retriever.SearchAsync(
    query: "RAG 라이브러리",
    topK: 5
);

foreach (var result in results)
{
    Console.WriteLine($"문서: {result.DocumentId}");
    Console.WriteLine($"내용: {result.Content}");
    Console.WriteLine($"점수: {result.Score:F2}");
    Console.WriteLine("---");
}
```

---

## 3. AI Provider 연동

### OpenAI 설정

```csharp
// appsettings.json
{
  "OpenAI": {
    "ApiKey": "your-api-key",
    "ModelName": "text-embedding-3-small",
    "Dimensions": 1536
  }
}

// 서비스 등록
services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseOpenAIEmbedding(configuration.GetSection("OpenAI"));
```

### Azure OpenAI 사용

```csharp
services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseAzureOpenAIEmbedding(options =>
    {
        options.Endpoint = "https://your-resource.openai.azure.com/";
        options.ApiKey = "your-api-key";
        options.DeploymentName = "text-embedding-ada-002";
    });
```

### 임베딩 벡터로 검색

```csharp
// AI 임베딩 기반 검색 (의미적 유사도)
var semanticResults = await client.Retriever.SearchAsync(
    query: "인공지능과 자연어처리의 연관성",
    topK: 10,
    options: new SearchOptions
    {
        UseEmbedding = true,        // 벡터 검색 활성화
        MinScore = 0.7f            // 최소 유사도 설정
    }
);
```

---

## 4. 하이브리드 검색

### 키워드 + 의미 검색 결합

```csharp
// 하이브리드 검색 설정
var hybridResults = await client.Retriever.SearchAsync(
    query: "머신러닝 알고리즘",
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.Hybrid,
        VectorWeight = 0.7f,       // 의미 검색 가중치
        KeywordWeight = 0.3f       // 키워드 검색 가중치
    }
);
```

### 적응형 검색 (권장)

```csharp
// 쿼리 복잡도에 따라 자동으로 검색 전략 선택
var adaptiveResults = await client.Retriever.SearchAsync(
    query: "딥러닝과 신경망의 차이점을 설명하고 실제 응용 사례를 제시해주세요",
    topK: 10,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.Adaptive  // 자동 전략 선택
    }
);
```

---

## 5. 문서 파일 처리

### FileFlux Extension 사용

```bash
# 문서 처리 패키지 추가
dotnet add package FluxIndex.Extensions.FileFlux
```

```csharp
using FluxIndex.Extensions.FileFlux;

// FluxIndex 기본 설정
services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseOpenAIEmbedding(config.GetSection("OpenAI"));

// FileFlux 확장 추가 (주의: AddFileFlux로 변경됨)
services.AddFileFlux(options =>
{
    options.DefaultChunkingStrategy = "Semantic";
    options.DefaultMaxChunkSize = 1024;
    options.DefaultOverlapSize = 128;
});

var serviceProvider = services.BuildServiceProvider();
var client = serviceProvider.GetRequiredService<FluxIndexClient>();
var fileFlux = serviceProvider.GetRequiredService<FileFluxIntegration>();

// PDF, DOCX, TXT 파일 인덱싱
var documentId = await fileFlux.ProcessAndIndexAsync(
    filePath: "documents/manual.pdf",
    options: new ProcessingOptions
    {
        ChunkingStrategy = "Semantic",
        MaxChunkSize = 1024,
        OverlapSize = 128
    }
);

Console.WriteLine($"인덱싱된 문서 ID: {documentId}");
```

### 웹 페이지 처리

```bash
dotnet add package FluxIndex.Extensions.WebFlux
```

```csharp
// 웹 페이지 크롤링 및 인덱싱
var webResults = await client.Indexer.IndexWebPageAsync(
    url: "https://example.com/article",
    documentId: "web-001",
    options: new WebCrawlOptions
    {
        MaxDepth = 1,
        FollowExternalLinks = false
    }
);
```

---

## 6. 성능 최적화

### 캐싱 설정

```bash
dotnet add package FluxIndex.Cache.Redis
```

```csharp
// Redis 캐싱으로 성능 향상
services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseOpenAIEmbedding(config.GetSection("OpenAI"))
    .UseRedisCache("localhost:6379");

// 캐시 활용한 검색
var cachedResults = await client.Retriever.SearchAsync(
    query: "자주 검색되는 내용",
    topK: 5,
    options: new SearchOptions
    {
        UseCache = true,           // 캐시 활용
        CacheTTL = TimeSpan.FromHours(1)
    }
);
```

### 배치 인덱싱

```csharp
// 대량 문서 효율적 처리
var documents = new[]
{
    new IndexRequest("문서 내용 1", "doc-001"),
    new IndexRequest("문서 내용 2", "doc-002"),
    new IndexRequest("문서 내용 3", "doc-003")
};

var batchResults = await client.Indexer.IndexBatchAsync(
    documents: documents,
    options: new IndexingOptions
    {
        BatchSize = 100,           // 배치 크기
        MaxParallelism = 4         // 병렬 처리 수
    }
);
```

### PostgreSQL 프로덕션 설정

```csharp
// 프로덕션 환경 설정
services.AddFluxIndex()
    .UsePostgreSQLVectorStore(options =>
    {
        options.ConnectionString = "Host=localhost;Database=vectordb;Username=user;Password=pass";
        options.EmbeddingDimensions = 1536;
        options.AutoMigrate = true;
    })
    .UseOpenAIEmbedding(config.GetSection("OpenAI"))
    .UseRedisCache(config.GetConnectionString("Redis"));
```

---

## 실전 예제: 완전한 RAG 시스템

```csharp
using FluxIndex.SDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// 설정 로드
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

// 서비스 등록
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());

services.AddFluxIndex()
    .UsePostgreSQLVectorStore(config.GetSection("Database"))
    .UseOpenAIEmbedding(config.GetSection("OpenAI"))
    .UseRedisCache(config.GetConnectionString("Redis"))
    .UseFileFlux();

var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<FluxIndexClient>();
var logger = provider.GetRequiredService<ILogger<Program>>();

// 1. 문서 인덱싱
logger.LogInformation("문서 인덱싱 시작...");
await client.Indexer.IndexFileAsync("docs/manual.pdf", "manual");

// 2. 사용자 질의 처리
var userQuery = "제품 설치 방법을 알려주세요";
logger.LogInformation("사용자 질의: {Query}", userQuery);

// 3. 적응형 검색으로 관련 문서 검색
var searchResults = await client.Retriever.SearchAsync(
    query: userQuery,
    topK: 5,
    options: new SearchOptions
    {
        SearchStrategy = SearchStrategy.Adaptive,
        UseCache = true
    }
);

// 4. 결과 표시
logger.LogInformation("검색된 문서 {Count}개", searchResults.Count());
foreach (var result in searchResults)
{
    logger.LogInformation("문서: {DocumentId}, 점수: {Score:F2}",
        result.DocumentId, result.Score);
}
```

---

## 다음 단계

1. **고급 기능**: [Architecture Guide](architecture.md)에서 내부 구조 학습
2. **성능 튜닝**: 벤치마크와 최적화 전략
3. **실제 예제**: `samples/` 디렉토리의 다양한 사용 사례 참고
4. **API 문서**: 각 패키지별 상세 API 참조

FluxIndex를 사용한 RAG 시스템 구축을 시작해보세요! 🚀