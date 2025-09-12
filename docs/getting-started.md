# FluxIndex 빠른 시작 가이드

5분 안에 FluxIndex를 시작하고 첫 번째 RAG 시스템을 구축해보세요!

## 📋 전제 조건

- .NET 9.0 SDK 이상
- (선택) OpenAI API 키 또는 Azure OpenAI 엔드포인트
- (선택) PostgreSQL 14+ (pgvector 확장 포함)

## 🚀 1단계: 패키지 설치

### 최소 설치 (로컬 검색만)
```bash
dotnet new console -n MyRAGApp
cd MyRAGApp
dotnet add package FluxIndex.SDK
```

### 전체 설치 (AI 임베딩 포함)
```bash
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.AI.OpenAI
dotnet add package FluxIndex.Storage.PostgreSQL  # 또는 SQLite
```

## 🔧 2단계: 기본 설정

### appsettings.json
```json
{
  "FluxIndex": {
    "OpenAI": {
      "ApiKey": "your-openai-api-key",
      "Model": "text-embedding-ada-002"
    },
    "Storage": {
      "Type": "InMemory",  // 또는 "PostgreSQL", "SQLite"
      "ConnectionString": "Host=localhost;Database=fluxindex;Username=user;Password=pass"
    }
  }
}
```

## 💻 3단계: 첫 번째 RAG 애플리케이션

### Program.cs
```csharp
using FluxIndex.SDK;
using FluxIndex.Core.Models;

// 1. FluxIndex 클라이언트 생성
var client = new FluxIndexClientBuilder()
    .ConfigureVectorStore(VectorStoreType.InMemory)
    .ConfigureEmbeddingService(config => 
    {
        // OpenAI 사용 (선택적)
        config.UseOpenAI("your-api-key");
        
        // 또는 로컬 전용 모드
        // config.UseLocalOnly();
    })
    .Build();

// 2. 문서 준비 및 인덱싱
var documents = new[]
{
    new Document
    {
        Id = "doc1",
        Content = "FluxIndex는 고성능 RAG 인프라입니다. 벡터 검색과 키워드 검색을 지원합니다.",
        Metadata = new Dictionary<string, object>
        {
            ["category"] = "introduction",
            ["source"] = "documentation"
        }
    },
    new Document
    {
        Id = "doc2", 
        Content = "Clean Architecture를 따르며 AI Provider에 중립적입니다.",
        Metadata = new Dictionary<string, object>
        {
            ["category"] = "architecture",
            ["source"] = "documentation"
        }
    },
    new Document
    {
        Id = "doc3",
        Content = "PostgreSQL, SQLite, Redis 등 다양한 스토리지를 지원합니다.",
        Metadata = new Dictionary<string, object>
        {
            ["category"] = "features",
            ["source"] = "documentation"
        }
    }
};

Console.WriteLine("📚 문서 인덱싱 중...");
await client.Indexer.IndexDocumentsAsync(documents);
Console.WriteLine($"✅ {documents.Length}개 문서 인덱싱 완료!\n");

// 3. 검색 수행
while (true)
{
    Console.Write("검색어를 입력하세요 (종료: exit): ");
    var query = Console.ReadLine();
    
    if (query?.ToLower() == "exit")
        break;
        
    if (string.IsNullOrWhiteSpace(query))
        continue;
    
    // 검색 실행
    var results = await client.Retriever.SearchAsync(
        query,
        new SearchOptions 
        { 
            TopK = 3,
            MinimumScore = 0.5f
        }
    );
    
    // 결과 출력
    Console.WriteLine($"\n🔍 '{query}'에 대한 검색 결과:\n");
    
    if (!results.Documents.Any())
    {
        Console.WriteLine("관련 문서를 찾을 수 없습니다.\n");
        continue;
    }
    
    foreach (var doc in results.Documents)
    {
        Console.WriteLine($"📄 [{doc.Score:F2}] {doc.Content}");
        Console.WriteLine($"   카테고리: {doc.Metadata["category"]}, 소스: {doc.Metadata["source"]}\n");
    }
}

Console.WriteLine("👋 프로그램을 종료합니다.");
```

## 🎯 4단계: 실행 및 테스트

```bash
dotnet run
```

### 예상 출력:
```
📚 문서 인덱싱 중...
✅ 3개 문서 인덱싱 완료!

검색어를 입력하세요 (종료: exit): 아키텍처

🔍 '아키텍처'에 대한 검색 결과:

📄 [0.92] Clean Architecture를 따르며 AI Provider에 중립적입니다.
   카테고리: architecture, 소스: documentation

📄 [0.68] FluxIndex는 고성능 RAG 인프라입니다. 벡터 검색과 키워드 검색을 지원합니다.
   카테고리: introduction, 소스: documentation

검색어를 입력하세요 (종료: exit): 
```

## 🔄 5단계: FileFlux와 통합 (선택적)

실제 문서 파일을 처리하려면 FileFlux와 통합하세요:

```bash
dotnet add package FileFlux
```

```csharp
using FileFlux;
using FluxIndex.SDK;

// FileFlux로 문서 처리
var fileFlux = new FileFluxClient();
var processedDocs = await fileFlux.ProcessDirectoryAsync("./documents");

// FluxIndex로 인덱싱
var fluxIndex = new FluxIndexClientBuilder()
    .ConfigureVectorStore(VectorStoreType.InMemory)
    .ConfigureEmbeddingService(config => config.UseOpenAI(apiKey))
    .Build();

// FileFlux 청킹 결과를 FluxIndex Document로 변환
var documents = processedDocs.Chunks.Select(chunk => new Document
{
    Id = chunk.Id,
    Content = chunk.Content,
    Metadata = chunk.Metadata
});

await fluxIndex.Indexer.IndexDocumentsAsync(documents);
```

## 🎨 고급 설정

### PostgreSQL + pgvector 사용
```csharp
var client = new FluxIndexClientBuilder()
    .ConfigureVectorStore(VectorStoreType.PostgreSQL, options =>
    {
        options.ConnectionString = "Host=localhost;Database=fluxindex;Username=user;Password=pass";
        options.VectorDimension = 1536;  // OpenAI ada-002
        options.CreateIndexIfNotExists = true;
    })
    .ConfigureEmbeddingService(config => config.UseOpenAI(apiKey))
    .Build();
```

### Azure OpenAI 사용
```csharp
var client = new FluxIndexClientBuilder()
    .ConfigureEmbeddingService(config => config.UseAzureOpenAI(
        endpoint: "https://your-resource.openai.azure.com/",
        apiKey: "your-azure-api-key",
        deploymentName: "text-embedding-ada-002"
    ))
    .Build();
```

### Redis 캐싱 추가
```csharp
var client = new FluxIndexClientBuilder()
    .ConfigureCache(CacheType.Redis, options =>
    {
        options.ConnectionString = "localhost:6379";
        options.ExpirationMinutes = 60;
    })
    .Build();
```

### 하이브리드 검색 설정
```csharp
var results = await client.Retriever.SearchAsync(query, new SearchOptions
{
    SearchType = SearchType.Hybrid,  // 벡터 + 키워드 결합
    TopK = 10,
    MinimumScore = 0.7f,
    UseReranking = true,  // 재순위화 활성화
    MetadataFilters = new Dictionary<string, object>
    {
        ["category"] = "technical"  // 메타데이터 필터링
    }
});
```

## 📊 성능 최적화 팁

### 1. 배치 인덱싱
```csharp
// 대량 문서는 배치로 처리
await client.Indexer.IndexDocumentsAsync(documents, new IndexingOptions
{
    BatchSize = 100,
    ParallelDegree = 4
});
```

### 2. 비동기 처리
```csharp
// 여러 검색을 병렬로 실행
var tasks = queries.Select(q => client.Retriever.SearchAsync(q));
var results = await Task.WhenAll(tasks);
```

### 3. 캐싱 활용
```csharp
// 자주 사용되는 쿼리는 캐싱
var cachedResults = await client.Retriever.SearchAsync(query, new SearchOptions
{
    UseCache = true,
    CacheDuration = TimeSpan.FromMinutes(30)
});
```

## 🐛 문제 해결

### OpenAI API 키 오류
```csharp
try
{
    await client.Indexer.IndexDocumentsAsync(documents);
}
catch (OpenAIException ex)
{
    Console.WriteLine($"OpenAI 오류: {ex.Message}");
    // 로컬 모드로 폴백
    client = new FluxIndexClientBuilder()
        .UseLocalSearchOnly()
        .Build();
}
```

### 메모리 부족
```csharp
// 스트리밍 모드 사용
await foreach (var batch in client.Indexer.IndexDocumentsStreamAsync(largeDocumentSet))
{
    Console.WriteLine($"처리된 배치: {batch.ProcessedCount}");
}
```

### 느린 검색 성능
```csharp
// 인덱스 최적화
await client.Maintenance.OptimizeIndexAsync();

// 검색 범위 제한
var results = await client.Retriever.SearchAsync(query, new SearchOptions
{
    TopK = 5,  // 결과 수 줄이기
    SearchScope = SearchScope.Recent  // 최근 문서만 검색
});
```

## 📚 다음 단계

1. **[설치 가이드](./installation.md)**: 상세한 설치 및 설정
2. **[API 레퍼런스](./api-reference.md)**: 전체 API 문서
3. **[아키텍처 가이드](./architecture.md)**: 시스템 설계 이해
4. **[AI Provider 가이드](./AI-Provider-Guide.md)**: 다양한 AI 서비스 통합

## 💡 예제 프로젝트

- [기본 콘솔 앱](https://github.com/iyulab/FluxIndex/tree/main/examples/ConsoleApp)
- [ASP.NET Core Web API](https://github.com/iyulab/FluxIndex/tree/main/examples/WebApi)
- [Blazor 검색 UI](https://github.com/iyulab/FluxIndex/tree/main/examples/BlazorSearch)
- [FileFlux 통합](https://github.com/iyulab/FluxIndex/tree/main/examples/FileFluxIntegration)

## 🆘 도움말

문제가 있으신가요? 
- [GitHub Issues](https://github.com/iyulab/FluxIndex/issues)
- [Discord 커뮤니티](https://discord.gg/fluxindex)
- [Stack Overflow](https://stackoverflow.com/questions/tagged/fluxindex)

축하합니다! 🎉 이제 FluxIndex를 사용한 첫 번째 RAG 시스템을 구축했습니다!