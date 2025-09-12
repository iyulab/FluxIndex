# FluxIndex 아키텍처 가이드

## 개요

FluxIndex는 Clean Architecture 원칙을 따르는 고성능 RAG(Retrieval-Augmented Generation) 인프라입니다. 의존성 역전 원칙(DIP)을 철저히 준수하여 외부 의존성으로부터 핵심 비즈니스 로직을 보호합니다.

## 아키텍처 레이어

```
┌──────────────────────────────────────────────────────┐
│                    Presentation                       │
│                   (FluxIndex.SDK)                     │
├──────────────────────────────────────────────────────┤
│                   Infrastructure                      │
│ (Storage.PostgreSQL, AI.OpenAI, Cache.Redis, etc.)   │
├──────────────────────────────────────────────────────┤
│                    Application                        │
│              (Services, Use Cases)                    │
├──────────────────────────────────────────────────────┤
│                      Domain                          │
│            (Entities, Value Objects)                  │
└──────────────────────────────────────────────────────┘
```

### 1. Domain Layer (FluxIndex.Core/Domain)

**핵심 비즈니스 로직과 도메인 모델**

```csharp
// 엔티티
public class Document
{
    public string Id { get; private set; }
    public string Content { get; private set; }
    public EmbeddingVector? Embedding { get; private set; }
    public DocumentMetadata Metadata { get; private set; }
    
    // Factory Method Pattern
    public static Document Create(string content, Dictionary<string, object>? metadata = null)
    {
        // 도메인 규칙 검증
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        
        return new Document
        {
            Id = Guid.NewGuid().ToString(),
            Content = content,
            Metadata = new DocumentMetadata(metadata ?? new())
        };
    }
}

// Value Object
public record EmbeddingVector(float[] Values)
{
    public int Dimensions => Values.Length;
    
    // 코사인 유사도 계산
    public float CosineSimilarity(EmbeddingVector other)
    {
        // 도메인 로직
    }
}
```

**특징:**
- 외부 의존성 없음 (FileFlux 제외)
- 불변성 보장
- 풍부한 도메인 모델
- Factory Method 패턴 사용

### 2. Application Layer (FluxIndex.Core/Application)

**비즈니스 유스케이스와 오케스트레이션**

```csharp
// 서비스 인터페이스
public interface IIndexingService
{
    Task<IndexingResult> IndexDocumentsAsync(
        IEnumerable<Document> documents,
        IndexingOptions? options = null,
        CancellationToken cancellationToken = default);
}

// 구현
public class IndexingService : IIndexingService
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IDocumentRepository _repository;
    
    public async Task<IndexingResult> IndexDocumentsAsync(...)
    {
        // 1. 임베딩 생성
        var embeddings = await _embeddingService.GenerateEmbeddingsAsync(documents);
        
        // 2. 벡터 스토어에 저장
        await _vectorStore.StoreAsync(embeddings);
        
        // 3. 메타데이터 저장
        await _repository.SaveAsync(documents);
        
        return new IndexingResult { ... };
    }
}
```

**책임:**
- 유스케이스 구현
- 도메인과 인프라 조정
- 트랜잭션 관리
- 비즈니스 규칙 적용

### 3. Infrastructure Layer

**외부 시스템과의 통합**

#### Storage Providers
```csharp
// FluxIndex.Storage.PostgreSQL
public class PostgreSQLVectorStore : IVectorStore
{
    private readonly FluxIndexDbContext _context;
    
    public async Task<IEnumerable<SearchResult>> SearchAsync(
        EmbeddingVector query,
        int topK)
    {
        // pgvector를 사용한 벡터 검색
        return await _context.Documents
            .OrderBy(d => d.Embedding.CosineDistance(query))
            .Take(topK)
            .Select(d => new SearchResult { ... })
            .ToListAsync();
    }
}
```

#### AI Providers
```csharp
// FluxIndex.AI.OpenAI
public class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly OpenAIClient _client;
    
    public async Task<EmbeddingVector> GenerateEmbeddingAsync(string text)
    {
        var response = await _client.Embeddings.CreateAsync(
            new EmbeddingCreateRequest
            {
                Model = "text-embedding-ada-002",
                Input = text
            });
            
        return new EmbeddingVector(response.Data[0].Embedding);
    }
}
```

### 4. SDK Layer (FluxIndex.SDK)

**통합 API와 Builder 패턴**

```csharp
public class FluxIndexClientBuilder
{
    private readonly IServiceCollection _services = new ServiceCollection();
    
    public FluxIndexClientBuilder ConfigureVectorStore(VectorStoreType type)
    {
        switch (type)
        {
            case VectorStoreType.PostgreSQL:
                _services.AddPostgreSQLVectorStore();
                break;
            case VectorStoreType.InMemory:
                _services.AddInMemoryVectorStore();
                break;
        }
        return this;
    }
    
    public FluxIndexClientBuilder ConfigureEmbeddingService(
        Action<EmbeddingServiceConfiguration> configure)
    {
        var config = new EmbeddingServiceConfiguration();
        configure(config);
        config.Apply(_services);
        return this;
    }
    
    public FluxIndexClient Build()
    {
        var provider = _services.BuildServiceProvider();
        return new FluxIndexClient(
            provider.GetRequiredService<IRetriever>(),
            provider.GetRequiredService<IIndexer>()
        );
    }
}
```

## 의존성 흐름

```
SDK → Infrastructure → Application → Domain
         ↓                ↓
    External APIs    Core Interfaces
```

**원칙:**
1. 내부 레이어는 외부 레이어를 모름
2. 인터페이스는 Core에 정의
3. 구현은 Infrastructure에 위치
4. DI를 통한 의존성 주입

## 패키지 구조

```
FluxIndex/
├── src/
│   ├── FluxIndex.Core/           # 핵심 비즈니스 로직
│   │   ├── Domain/               # 엔티티, Value Objects
│   │   ├── Application/          # 서비스, 유스케이스
│   │   └── Interfaces/           # 추상화
│   │
│   ├── FluxIndex.SDK/            # 통합 API
│   │   ├── FluxIndexClient.cs
│   │   ├── FluxIndexClientBuilder.cs
│   │   ├── Retriever.cs
│   │   └── Indexer.cs
│   │
│   ├── FluxIndex.AI.OpenAI/      # OpenAI 통합
│   ├── FluxIndex.Storage.PostgreSQL/  # PostgreSQL + pgvector
│   ├── FluxIndex.Storage.SQLite/      # SQLite 스토리지
│   └── FluxIndex.Cache.Redis/         # Redis 캐싱
│
└── tests/
    ├── FluxIndex.Core.Tests/
    ├── FluxIndex.SDK.Tests/
    └── FluxIndex.Integration.Tests/
```

## 주요 디자인 패턴

### 1. Repository Pattern
```csharp
public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(string id);
    Task<IEnumerable<Document>> GetByIdsAsync(IEnumerable<string> ids);
    Task SaveAsync(Document document);
    Task DeleteAsync(string id);
}
```

### 2. Factory Pattern
```csharp
public static class DocumentFactory
{
    public static Document CreateFromChunk(FileFluxChunk chunk)
    {
        return Document.Create(
            content: chunk.Content,
            metadata: chunk.Metadata
        );
    }
}
```

### 3. Strategy Pattern (검색 전략)
```csharp
public interface ISearchStrategy
{
    Task<SearchResults> ExecuteAsync(SearchQuery query);
}

public class HybridSearchStrategy : ISearchStrategy
{
    private readonly IVectorSearch _vectorSearch;
    private readonly IKeywordSearch _keywordSearch;
    
    public async Task<SearchResults> ExecuteAsync(SearchQuery query)
    {
        // 벡터 검색과 키워드 검색 결합
        var vectorResults = await _vectorSearch.SearchAsync(query);
        var keywordResults = await _keywordSearch.SearchAsync(query);
        
        return RankFusion.Combine(vectorResults, keywordResults);
    }
}
```

### 4. Builder Pattern (SDK)
```csharp
var client = new FluxIndexClientBuilder()
    .ConfigureVectorStore(VectorStoreType.PostgreSQL)
    .ConfigureEmbeddingService(config => config.UseOpenAI(apiKey))
    .ConfigureSearchOptions(options => 
    {
        options.TopK = 10;
        options.MinimumScore = 0.7f;
    })
    .Build();
```

## 확장 포인트

### 새로운 Storage Provider 추가
1. `FluxIndex.Storage.{Provider}` 프로젝트 생성
2. `IVectorStore` 인터페이스 구현
3. DI 확장 메서드 제공
4. SDK Builder에 통합

### 새로운 AI Provider 추가
1. `FluxIndex.AI.{Provider}` 프로젝트 생성
2. `IEmbeddingService` 인터페이스 구현
3. Provider별 설정 클래스 생성
4. SDK Builder에 통합

### 커스텀 검색 전략 추가
1. `ISearchStrategy` 인터페이스 구현
2. 전략 선택 로직에 추가
3. 성능 메트릭 수집

## 성능 고려사항

### 1. 배치 처리
```csharp
// 대량 문서 인덱싱
await indexer.IndexDocumentsAsync(documents, new IndexingOptions
{
    BatchSize = 100,
    ParallelDegree = 4
});
```

### 2. 캐싱 전략
```csharp
// 임베딩 캐싱
services.AddMemoryCache();
services.Decorate<IEmbeddingService, CachedEmbeddingService>();
```

### 3. 연결 풀링
```csharp
// PostgreSQL 연결 풀 설정
services.AddDbContext<FluxIndexDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.UseVector();
    });
}, ServiceLifetime.Scoped);
```

## 보안 고려사항

1. **API 키 관리**: Azure Key Vault, AWS Secrets Manager 통합
2. **데이터 암호화**: 저장 시 암호화, 전송 시 TLS
3. **접근 제어**: 문서별 권한 관리
4. **감사 로깅**: 모든 검색/인덱싱 작업 기록

## 모니터링과 관찰성

```csharp
// OpenTelemetry 통합
services.AddOpenTelemetry()
    .WithMetrics(builder => builder
        .AddMeter("FluxIndex.Metrics")
        .AddPrometheusExporter())
    .WithTracing(builder => builder
        .AddSource("FluxIndex.Tracing")
        .AddJaegerExporter());
```

## 마이그레이션 가이드

### v0.1.x → v1.0.0
1. 패키지 참조 업데이트
2. Builder API 마이그레이션
3. 설정 옵션 변경 적용

## 결론

FluxIndex의 Clean Architecture는 다음을 제공합니다:
- **유지보수성**: 명확한 레이어 분리
- **테스트 가능성**: 의존성 주입과 모킹
- **확장성**: 새로운 Provider 쉽게 추가
- **성능**: 최적화된 검색 알고리즘
- **유연성**: AI Provider 독립성