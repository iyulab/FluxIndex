# AI Provider Integration Guide

FluxIndex는 AI provider에 대해 완전한 불가지론(agnostic) 아키텍처를 채택합니다. 이 가이드는 OpenAI, Azure OpenAI, LMSupply, 또는 커스텀 AI 서비스를 FluxIndex에 통합하는 방법을 설명합니다.

## 목차

- [아키텍처 개요](#아키텍처-개요)
- [Core 추상 클래스](#core-추상-클래스)
- [Embedding Service 구현](#embedding-service-구현)
- [Text Completion Service 구현](#text-completion-service-구현)
- [Reranker 구현](#reranker-구현)
- [DI 등록](#di-등록)
- [실전 예제](#실전-예제)

---

## 아키텍처 개요

### 설계 철학

FluxIndex Core는 AI 관련 **인터페이스와 추상 클래스**만 제공합니다. 실제 AI provider 구현은 **소비 앱(consumer application)**에서 담당합니다.

```
┌─────────────────────────────────────────────────────────────┐
│                    Consumer Application                      │
│  ┌─────────────────┐  ┌─────────────────┐  ┌──────────────┐ │
│  │ LMSupply Wrapper│  │ OpenAI Wrapper  │  │ Azure Wrapper│ │
│  └────────┬────────┘  └────────┬────────┘  └──────┬───────┘ │
└───────────┼─────────────────────┼─────────────────┼─────────┘
            │                     │                 │
            ▼                     ▼                 ▼
┌─────────────────────────────────────────────────────────────┐
│                      FluxIndex.Core                          │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              Abstract Base Classes                    │   │
│  │  • EmbeddingServiceBase                               │   │
│  │  • TextCompletionServiceBase                          │   │
│  │  • RerankerBase                                       │   │
│  └──────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────┐   │
│  │                    Interfaces                         │   │
│  │  • IEmbeddingService                                  │   │
│  │  • ITextCompletionService                             │   │
│  │  • IReranker                                          │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 이점

1. **패키지 의존성 최소화**: Core는 AI SDK 의존성 없음
2. **자유로운 Provider 선택**: OpenAI, Azure, Anthropic, 로컬 모델 등
3. **테스트 용이성**: InMemory 구현으로 단위 테스트
4. **최신 SDK 버전 사용**: 소비 앱이 직접 의존성 관리

---

## Core 추상 클래스

Core는 세 가지 추상 클래스를 제공합니다. 각 추상 클래스는 핵심 메서드만 구현하면 나머지 기능을 자동으로 제공합니다.

| Abstract Class | 핵심 구현 메서드 | 기본 제공 기능 |
|----------------|-----------------|---------------|
| `EmbeddingServiceBase` | `EmbedCoreAsync()` | null 체크, 배치 처리 fallback, 토큰 추정 |
| `TextCompletionServiceBase` | `GenerateCoreAsync()` | JSON 추출, 토큰 카운트 |
| `RerankerBase` | `RerankCoreAsync()` | RerankResult 변환, 필터링, content 길이 제한 |

---

## Embedding Service 구현

### 인터페이스

```csharp
public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
    Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken ct = default);
    int GetEmbeddingDimension();
    string GetModelName();
    int GetMaxTokens();
    Task<int> CountTokensAsync(string text, CancellationToken ct = default);
}
```

### 추상 클래스 사용

`EmbeddingServiceBase`를 상속하면 `EmbedCoreAsync()`만 구현하면 됩니다:

```csharp
using FluxIndex.Core.Application.Services.Base;

public class MyEmbeddingService : EmbeddingServiceBase
{
    private readonly int _dimension;
    private readonly string _modelName;

    public MyEmbeddingService(int dimension, string modelName)
    {
        _dimension = dimension;
        _modelName = modelName;
    }

    // 핵심 구현: 이 메서드만 구현하면 됩니다
    protected override async Task<float[]> EmbedCoreAsync(
        string text,
        CancellationToken cancellationToken)
    {
        // 여기에 실제 임베딩 로직 구현
        // 예: API 호출, 로컬 모델 실행 등
        return await YourEmbeddingProvider.EmbedAsync(text, cancellationToken);
    }

    // 필수 구현
    public override int GetEmbeddingDimension() => _dimension;
    public override string GetModelName() => _modelName;

    // 선택적 오버라이드 (기본값 제공됨)
    // public override int GetMaxTokens() => 8192;
    // public override Task<int> CountTokensAsync(...) => ...;
}
```

### OpenAI / Azure OpenAI — FluxIndex.Providers.OpenAI 패키지 사용 (권장)

FluxIndex는 OpenAI 및 OpenAI-compatible 엔드포인트를 위한 공식 provider 패키지를 제공합니다.

```bash
dotnet add package FluxIndex.Providers.OpenAI
```

```csharp
using FluxIndex.Providers.OpenAI.Extensions;  // DI 확장

// ASP.NET Core / Generic Host
services.AddOpenAICompatibleEmbedding(
    endpoint: "https://api.openai.com/v1",
    apiKey: apiKey,
    model: "text-embedding-3-small",
    dimension: 1536);

services.AddOpenAICompatibleReranker(
    endpoint: "https://api.openai.com/v1",
    apiKey: apiKey,
    model: "text-embedding-ada-002");
```

직접 생성이 필요한 경우:

```csharp
using FluxIndex.Providers.OpenAI.Services;

var embeddingService = new OpenAICompatibleEmbeddingService(
    endpoint: "https://api.openai.com/v1",
    apiKey: apiKey,
    model: "text-embedding-3-small",
    dimension: 1536,
    logger: loggerFactory.CreateLogger<OpenAICompatibleEmbeddingService>());

var ctx = FluxIndexContext.CreateBuilder()
    .UseLocalStorage("index.db")
    .UseEmbeddingService(embeddingService)
    .Build();
```

**지원 엔드포인트:** OpenAI, Azure OpenAI, GPUStack, Ollama, Fireworks, Groq 등 OpenAI-compatible API.

**Azure OpenAI 엔드포인트 형식:**
```
https://{resource}.openai.azure.com/openai/deployments/{deployment}/v1
```

**공통 모델 dimension:**
| 모델 | Dimension |
|------|-----------|
| `text-embedding-3-small` | 1536 |
| `text-embedding-3-large` | 3072 |
| `text-embedding-ada-002` | 1536 |
| `qwen3-embedding-0.6b` | 1024 |

---

### OpenAI 직접 구현 예제 (커스텀 SDK 필요 시)

Azure.AI.OpenAI SDK를 사용하는 경우의 커스텀 구현 예제입니다.
일반적인 경우 위의 `FluxIndex.Providers.OpenAI` 패키지 사용을 권장합니다.

```csharp
using Azure.AI.OpenAI;
using FluxIndex.Core.Application.Services.Base;

public sealed class OpenAIEmbeddingService : EmbeddingServiceBase, IAsyncDisposable
{
    private readonly OpenAIClient _client;
    private readonly string _model;
    private readonly int _dimension;

    public OpenAIEmbeddingService(string apiKey, string model = "text-embedding-3-small")
    {
        _client = new OpenAIClient(apiKey);
        _model = model;
        _dimension = model switch
        {
            "text-embedding-3-small" => 1536,
            "text-embedding-3-large" => 3072,
            "text-embedding-ada-002" => 1536,
            _ => 1536
        };
    }

    protected override async Task<float[]> EmbedCoreAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var response = await _client.GetEmbeddingsAsync(
            new EmbeddingsOptions(_model, new[] { text }),
            cancellationToken);

        return response.Value.Data[0].Embedding.ToArray();
    }

    public override int GetEmbeddingDimension() => _dimension;
    public override string GetModelName() => _model;
    public override int GetMaxTokens() => 8191;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

### LMSupply 구현 예제

```csharp
using LMSupply.Embedder;
using FluxIndex.Core.Application.Services.Base;

public sealed class LMSupplyEmbedder : EmbeddingServiceBase, IAsyncDisposable
{
    private readonly IEmbeddingModel _model;

    private LMSupplyEmbedder(IEmbeddingModel model) => _model = model;

    public static async Task<LMSupplyEmbedder> CreateAsync(
        string modelId = "all-MiniLM-L6-v2",
        CancellationToken cancellationToken = default)
    {
        var model = await LocalEmbedder.LoadAsync(modelId, cancellationToken: cancellationToken);
        return new LMSupplyEmbedder(model);
    }

    protected override async Task<float[]> EmbedCoreAsync(
        string text,
        CancellationToken cancellationToken)
    {
        return await _model.EmbedAsync(text, cancellationToken);
    }

    // 배치 처리 최적화 (선택적)
    public override async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        // LMSupply는 네이티브 배치 지원
        return await _model.EmbedBatchAsync(texts.ToList(), cancellationToken);
    }

    public override int GetEmbeddingDimension() => _model.Dimensions;
    public override string GetModelName() => _model.ModelId;

    public ValueTask DisposeAsync() => _model.DisposeAsync();
}
```

---

## Text Completion Service 구현

### 인터페이스

```csharp
public interface ITextCompletionService
{
    Task<string> GenerateCompletionAsync(
        string prompt,
        int maxTokens = 500,
        float temperature = 0.7f,
        CancellationToken cancellationToken = default);

    Task<string> GenerateJsonCompletionAsync(
        string prompt,
        int maxTokens = 500,
        CancellationToken cancellationToken = default);

    int CountTokens(string text);
}
```

### 추상 클래스 사용

```csharp
using FluxIndex.Core.Application.Services.Base;

public class MyTextCompletionService : TextCompletionServiceBase
{
    // 핵심 구현
    protected override async Task<string> GenerateCoreAsync(
        string prompt,
        int maxTokens,
        float temperature,
        CancellationToken cancellationToken)
    {
        // 실제 LLM 호출
        return await YourLLMProvider.GenerateAsync(prompt, maxTokens, temperature, cancellationToken);
    }
}
```

### OpenAI 구현 예제

```csharp
using Azure.AI.OpenAI;
using FluxIndex.Core.Application.Services.Base;

public sealed class OpenAICompletionService : TextCompletionServiceBase
{
    private readonly OpenAIClient _client;
    private readonly string _model;

    public OpenAICompletionService(string apiKey, string model = "gpt-4o-mini")
    {
        _client = new OpenAIClient(apiKey);
        _model = model;
    }

    protected override async Task<string> GenerateCoreAsync(
        string prompt,
        int maxTokens,
        float temperature,
        CancellationToken cancellationToken)
    {
        var options = new ChatCompletionsOptions
        {
            DeploymentName = _model,
            Temperature = temperature,
            MaxTokens = maxTokens,
            Messages = { new ChatRequestUserMessage(prompt) }
        };

        var response = await _client.GetChatCompletionsAsync(options, cancellationToken);
        return response.Value.Choices[0].Message.Content;
    }

    // JSON 모드 지원 시 오버라이드
    public override async Task<string> GenerateJsonCompletionAsync(
        string prompt,
        int maxTokens = 500,
        CancellationToken cancellationToken = default)
    {
        var options = new ChatCompletionsOptions
        {
            DeploymentName = _model,
            Temperature = 0.1f,
            MaxTokens = maxTokens,
            ResponseFormat = ChatCompletionsResponseFormat.JsonObject,
            Messages = { new ChatRequestUserMessage(prompt) }
        };

        var response = await _client.GetChatCompletionsAsync(options, cancellationToken);
        return response.Value.Choices[0].Message.Content;
    }
}
```

---

## Reranker 구현

### 인터페이스

```csharp
public interface IReranker
{
    Task<IEnumerable<RerankResult>> RerankAsync(
        string query,
        IEnumerable<RetrievalCandidate> candidates,
        RerankOptions? options = null,
        CancellationToken cancellationToken = default);

    RerankModelInfo GetModelInfo();
}
```

### 추상 클래스 사용

```csharp
using FluxIndex.Core.Application.Services.Base;

public class MyReranker : RerankerBase
{
    // 핵심 구현: (index, score) 튜플 반환
    protected override async Task<IEnumerable<(int Index, float Score)>> RerankCoreAsync(
        string query,
        IReadOnlyList<string> documents,
        int topN,
        CancellationToken cancellationToken)
    {
        // 실제 reranking 로직
        var scores = await YourRerankerProvider.ScoreAsync(query, documents, cancellationToken);

        return scores
            .Select((score, index) => (index, score))
            .OrderByDescending(x => x.score)
            .Take(topN);
    }

    public override RerankModelInfo GetModelInfo() => new()
    {
        Model = RerankModel.Custom,
        ModelName = "my-reranker-v1",
        MaxDocuments = 100
    };
}
```

### Cohere Reranker 예제

```csharp
using FluxIndex.Core.Application.Services.Base;

public sealed class CohereReranker : RerankerBase
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public CohereReranker(string apiKey, string model = "rerank-english-v3.0")
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        _model = model;
    }

    protected override async Task<IEnumerable<(int Index, float Score)>> RerankCoreAsync(
        string query,
        IReadOnlyList<string> documents,
        int topN,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            model = _model,
            query = query,
            documents = documents,
            top_n = topN
        };

        var response = await _httpClient.PostAsJsonAsync(
            "https://api.cohere.ai/v1/rerank",
            request,
            cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<CohereRerankResponse>(
            cancellationToken: cancellationToken);

        return result!.Results.Select(r => (r.Index, (float)r.RelevanceScore));
    }

    public override RerankModelInfo GetModelInfo() => new()
    {
        Model = RerankModel.CohereRerank,
        ModelName = _model,
        MaxDocuments = 1000
    };

    private record CohereRerankResponse(CohereRerankResult[] Results);
    private record CohereRerankResult(int Index, double RelevanceScore);
}
```

---

## DI 등록

### 기본 패턴

```csharp
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    // OpenAI Embedding 등록
    public static IServiceCollection AddOpenAIEmbedding(
        this IServiceCollection services,
        string apiKey,
        string model = "text-embedding-3-small")
    {
        services.AddSingleton<IEmbeddingService>(
            new OpenAIEmbeddingService(apiKey, model));
        return services;
    }

    // LMSupply Embedding 등록 (비동기 초기화)
    public static IServiceCollection AddLMSupplyEmbedding(
        this IServiceCollection services,
        string modelId = "all-MiniLM-L6-v2")
    {
        services.AddSingleton<IEmbeddingService>(sp =>
            LMSupplyEmbedder.CreateAsync(modelId).GetAwaiter().GetResult());
        return services;
    }

    // Text Completion 등록
    public static IServiceCollection AddOpenAICompletion(
        this IServiceCollection services,
        string apiKey,
        string model = "gpt-4o-mini")
    {
        services.AddSingleton<ITextCompletionService>(
            new OpenAICompletionService(apiKey, model));
        return services;
    }

    // Reranker 등록
    public static IServiceCollection AddCohereReranker(
        this IServiceCollection services,
        string apiKey,
        string model = "rerank-english-v3.0")
    {
        services.AddSingleton<IReranker>(
            new CohereReranker(apiKey, model));
        return services;
    }
}
```

### FluxIndexContext에서 사용

```csharp
// 테스트 환경: InMemory embedding (기본값)
var testContext = FluxIndexContext.CreateBuilder()
    .UseSQLite("test.db")
    .Build();

// 프로덕션 환경: OpenAI
var prodContext = FluxIndexContext.CreateBuilder()
    .UsePostgreSQL(connectionString)
    .ConfigureServices(services =>
    {
        services.AddOpenAIEmbedding(apiKey);
        services.AddOpenAICompletion(apiKey);
        services.AddCohereReranker(cohereApiKey);
    })
    .Build();

// 로컬 환경: LMSupply
var localContext = FluxIndexContext.CreateBuilder()
    .UseSQLite("local.db")
    .ConfigureServices(services =>
    {
        services.AddLMSupplyEmbedding("bge-small-en-v1.5");
    })
    .Build();
```

---

## 실전 예제

### 완전한 OpenAI 통합 예제

```csharp
// 1. 패키지 참조 (소비 앱의 .csproj)
// <PackageReference Include="Azure.AI.OpenAI" Version="2.0.0" />

// 2. 래퍼 클래스 정의
public sealed class OpenAIServices : IAsyncDisposable
{
    private readonly OpenAIEmbeddingService _embedding;
    private readonly OpenAICompletionService _completion;

    public OpenAIServices(string apiKey)
    {
        _embedding = new OpenAIEmbeddingService(apiKey, "text-embedding-3-small");
        _completion = new OpenAICompletionService(apiKey, "gpt-4o-mini");
    }

    public IEmbeddingService Embedding => _embedding;
    public ITextCompletionService Completion => _completion;

    public ValueTask DisposeAsync() => _embedding.DisposeAsync();
}

// 3. DI 확장 메서드
public static class OpenAIExtensions
{
    public static IServiceCollection AddOpenAIServices(
        this IServiceCollection services,
        string apiKey)
    {
        var openai = new OpenAIServices(apiKey);
        services.AddSingleton<IEmbeddingService>(openai.Embedding);
        services.AddSingleton<ITextCompletionService>(openai.Completion);
        return services;
    }
}

// 4. 사용
var context = FluxIndexContext.CreateBuilder()
    .UsePostgreSQL(connectionString)
    .ConfigureServices(s => s.AddOpenAIServices(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!))
    .UseRedisCache("localhost:6379")
    .Build();

// 인덱싱
await context.Indexer.IndexDocumentAsync(
    content: "FluxIndex is a RAG library for .NET.",
    documentId: "doc-001");

// 검색
var results = await context.Retriever.SearchAsync("RAG library", maxResults: 5);
```

### ASP.NET Core 통합 예제

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// AI 서비스 설정 로드
var aiConfig = builder.Configuration.GetSection("AI");
var apiKey = aiConfig["OpenAI:ApiKey"]!;

// FluxIndex 설정
builder.Services.AddFluxIndex(options =>
{
    options.UsePostgreSQL(builder.Configuration.GetConnectionString("Default")!);
});

// AI Provider 등록
builder.Services.AddOpenAIEmbedding(apiKey, "text-embedding-3-small");
builder.Services.AddOpenAICompletion(apiKey, "gpt-4o-mini");

var app = builder.Build();
```

```json
// appsettings.json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Database=fluxindex;Username=postgres;Password=..."
  },
  "AI": {
    "OpenAI": {
      "ApiKey": "",  // 환경 변수 또는 Secret Manager 사용 권장
      "EmbeddingModel": "text-embedding-3-small",
      "CompletionModel": "gpt-4o-mini"
    }
  }
}
```

---

## FAQ

### Q: InMemory embedding은 언제 사용하나요?
A: 테스트 환경에서 사용합니다. 실제 임베딩을 생성하지 않고 랜덤 벡터를 반환하므로 검색 품질은 없지만 API 호출 없이 빠르게 테스트할 수 있습니다.

### Q: 여러 Embedding 모델을 동시에 사용할 수 있나요?
A: 네, 키 기반 등록으로 가능합니다:
```csharp
services.AddKeyedSingleton<IEmbeddingService>("openai", new OpenAIEmbeddingService(apiKey));
services.AddKeyedSingleton<IEmbeddingService>("local", await LMSupplyEmbedder.CreateAsync());
```

### Q: 비동기 초기화가 필요한 서비스는 어떻게 등록하나요?
A: `GetAwaiter().GetResult()` 또는 Factory 패턴을 사용합니다:
```csharp
services.AddSingleton<IEmbeddingService>(sp =>
    LMSupplyEmbedder.CreateAsync().GetAwaiter().GetResult());
```

### Q: Anthropic Claude를 Text Completion에 사용하려면?
A: `TextCompletionServiceBase`를 상속하여 Claude API를 호출하는 래퍼를 작성합니다. `GenerateCoreAsync()` 메서드만 구현하면 됩니다.

---

## 관련 문서

- [GUIDE.md](./GUIDE.md) - FluxIndex 기본 사용법
- [REFERENCE.md](./REFERENCE.md) - API 레퍼런스
- [ADVANCED_RAG.md](./ADVANCED_RAG.md) - 고급 RAG 기능
