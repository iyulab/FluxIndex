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

| Abstract Class | 반드시 구현할 멤버 | 기본 제공 기능 |
|----------------|-----------------|---------------|
| `EmbeddingServiceBase` | `EmbedCoreAsync()`, `GetEmbeddingDimension()`, `GetModelName()`, `GetProviderName()` | 빈 텍스트 처리, 질의 임베딩 기본 경로, 순차 배치 fallback, 토큰 추정, `GetIdentity()` |
| `TextCompletionServiceBase` | `CompleteCoreAsync()` | 빈 프롬프트 처리, `CompleteJsonAsync()` (JSON 지시문 추가 + 응답에서 JSON 추출) |
| `RerankerBase` | `RerankCoreAsync()`, `GetModelInfo()` | RerankResult 변환, 점수 임계값 필터링, content 길이 제한 |

---

## Embedding Service 구현

### 인터페이스

```csharp
namespace FluxIndex.Core.Application.Interfaces;

public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);

    // 검색 질의 역할. 기본 구현은 GenerateEmbeddingAsync — 대칭 모델은 아무것도 하지 않아도 된다.
    Task<float[]> GenerateQueryEmbeddingAsync(string query, CancellationToken cancellationToken = default) =>
        GenerateEmbeddingAsync(query, cancellationToken);

    int GetEmbeddingDimension();
    string GetModelName();
    int GetMaxTokens();
    Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default);

    // Provider + Model + Dimension (+ Revision) — 벡터 공간을 식별한다.
    EmbeddingIdentity GetIdentity();
}
```

### 질의/문서 역할

FluxIndex 는 저장하는 텍스트(문서·청크·요약)를 `GenerateEmbeddingAsync` 로, 저장된 벡터와 비교할 검색 질의를
`GenerateQueryEmbeddingAsync` 로 임베딩한다. 비대칭 모델(E5 `query: `/`passage: `, Qwen3-Embedding·BGE 의 질의 지시문)은
`EmbeddingServiceBase` 에서 `EmbedQueryCoreAsync` 를 override 해 질의 규약을 적용한다 — 문서 쪽 규약(`EmbedCoreAsync`)도
함께 맞춰야 한다(한쪽만 바꾸면 두 규약이 섞인다). 다른 `IEmbeddingService` 를 감싸는 구현은 두 메서드를 모두 전달한다.

### 추상 클래스 사용

`EmbeddingServiceBase`를 상속하면 임베딩 로직(`EmbedCoreAsync()`)과 벡터 공간을 밝히는 세 멤버
(`GetEmbeddingDimension()`, `GetModelName()`, `GetProviderName()`)만 구현하면 됩니다. 빈 입력 처리,
질의 경로, 배치 fallback, 토큰 추정, `GetIdentity()`는 기반 클래스가 제공합니다:

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

    // 핵심 구현: 저장할 텍스트(문서·청크)의 임베딩
    protected override async Task<float[]> EmbedCoreAsync(
        string text,
        CancellationToken cancellationToken)
    {
        // 여기에 실제 임베딩 로직 구현
        // 예: API 호출, 로컬 모델 실행 등
        return await YourEmbeddingProvider.EmbedAsync(text, cancellationToken);
    }

    // 필수 구현 — Provider + Model + Dimension 이 벡터 공간의 Identity 가 된다
    public override int GetEmbeddingDimension() => _dimension;
    public override string GetModelName() => _modelName;
    protected override string GetProviderName() => "MyProvider";

    // 선택적 오버라이드 (기본값 제공됨)
    // protected override Task<float[]> EmbedQueryCoreAsync(string query, CancellationToken cancellationToken) => ...; // 비대칭 모델의 질의 규약
    // public override Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default) => ...; // 네이티브 배치
    // public override int GetMaxTokens() => 8192;                                       // 기본 512
    // public override Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default) => ...; // 기본은 근사치
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

// /v1/rerank 을 구현한 서버(llama.cpp `--rerank`, GPUStack, 호스티드 rerank API 등).
// OpenAI 자체 API 에는 rerank 엔드포인트가 없다.
services.AddOpenAICompatibleReranker(
    endpoint: "http://localhost:8080/v1",
    apiKey: null,
    model: "bge-reranker-v2-m3");
```

`relevance_score` 의 스케일은 wire 형식이 정하지 않는다 — 호스티드 API 는 (0, 1), llama.cpp 서버는 raw logit 을
답한다. 기본값 `ScoreScale.Auto` 는 응답마다 판정해(값 하나라도 [0, 1] 밖이면 그 응답 전체에 sigmoid) 어느 쪽이든
0..1 점수를 돌려준다. 엔드포인트의 스케일을 알고 있으면 `scoreScale: ScoreScale.Logit` / `ScoreScale.Probability` 로
명시한다 — 전부 [0, 1] 안에 드는 logit 응답은 `Auto` 가 가려낼 수 없다.

직접 생성이 필요한 경우:

```csharp
using FluxIndex.Providers.OpenAI.Services;
using FluxIndex.SDK;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.Logging;

var embeddingService = new OpenAICompatibleEmbeddingService(
    endpoint: "https://api.openai.com/v1",
    apiKey: apiKey,
    model: "text-embedding-3-small",
    dimension: 1536,
    logger: loggerFactory.CreateLogger<OpenAICompatibleEmbeddingService>());

var ctx = FluxIndexContext.CreateBuilder()
    .UseLocalStorage("index.db")
    .AddSQLiteStorage()
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

공식 `OpenAI` NuGet 패키지(2.x)를 직접 사용하는 커스텀 구현 예제입니다(`dotnet add package OpenAI`).
Azure OpenAI는 `Azure.AI.OpenAI` 2.x의 `AzureOpenAIClient.GetEmbeddingClient(deployment)`가 같은 `EmbeddingClient`를 돌려주므로 생성 부분만 바꾸면 됩니다.
일반적인 경우 위의 `FluxIndex.Providers.OpenAI` 패키지 사용을 권장합니다.

```csharp
using FluxIndex.Core.Application.Services.Base;
using OpenAI.Embeddings;

public sealed class OpenAIEmbeddingService : EmbeddingServiceBase
{
    private readonly EmbeddingClient _client;
    private readonly string _model;
    private readonly int _dimension;

    public OpenAIEmbeddingService(string apiKey, string model = "text-embedding-3-small")
    {
        _client = new EmbeddingClient(model, apiKey);
        _model = model;
        _dimension = model switch
        {
            "text-embedding-3-small" => 1536,
            "text-embedding-3-large" => 3072,
            "text-embedding-ada-002" => 1536,
            _ => throw new ArgumentException($"Unknown embedding dimension for model '{model}'.", nameof(model))
        };
    }

    protected override async Task<float[]> EmbedCoreAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var response = await _client.GenerateEmbeddingAsync(
            text,
            cancellationToken: cancellationToken);

        return response.Value.ToFloats().ToArray();
    }

    public override int GetEmbeddingDimension() => _dimension;
    public override string GetModelName() => _model;
    protected override string GetProviderName() => "OpenAI";
    public override int GetMaxTokens() => 8191;
}
```

### LMSupply 구현 예제

LMSupply 로컬 모델은 `FluxIndex.Providers.LMSupply`의 `services.AddLMSupplyEmbedding(...)`이 지연 로드·진행률·취소까지
처리하므로 그쪽을 권장합니다(아래 [DI 등록](#di-등록)). 직접 감싸야 한다면 질의/문서 역할을 모델의 규약에 맞춰
나눕니다 — `EmbedPassageAsync`/`EmbedQueryAsync`는 E5처럼 접두어가 있는 모델에 그 규약을 적용하고, 접두어가 없는
모델에서는 원문 그대로 임베딩합니다:

```csharp
using FluxIndex.Core.Application.Services.Base;
using LMSupply.Embedder;

public sealed class LMSupplyEmbedder : EmbeddingServiceBase, IAsyncDisposable
{
    private readonly IEmbeddingModel _model;

    private LMSupplyEmbedder(IEmbeddingModel model) => _model = model;

    public static async Task<LMSupplyEmbedder> CreateAsync(
        string modelId = "default",
        CancellationToken cancellationToken = default)
    {
        var model = await LocalEmbedder.LoadAsync(modelId, cancellationToken: cancellationToken);
        return new LMSupplyEmbedder(model);
    }

    // 저장할 텍스트: 모델의 passage 규약
    protected override async Task<float[]> EmbedCoreAsync(
        string text,
        CancellationToken cancellationToken)
    {
        return await _model.EmbedPassageAsync(text, cancellationToken);
    }

    // 검색 질의: 모델의 query 규약 (비대칭 모델)
    protected override async Task<float[]> EmbedQueryCoreAsync(
        string query,
        CancellationToken cancellationToken)
    {
        return await _model.EmbedQueryAsync(query, cancellationToken);
    }

    // 배치 처리 최적화 (선택적) — LMSupply는 네이티브 배치 지원
    public override async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        return await _model.EmbedPassageAsync(texts.ToList(), cancellationToken);
    }

    public override int GetEmbeddingDimension() => _model.Dimensions;
    public override string GetModelName() => _model.ModelId;
    protected override string GetProviderName() => "LMSupply";

    public ValueTask DisposeAsync() => _model.DisposeAsync();
}
```

---

## Text Completion Service 구현

### 인터페이스

`ITextCompletionService`와 `TextCompletionOptions`는 Flux 생태계 공유 계약 패키지 `Flux.Abstractions`에 있습니다
(`using Flux.Abstractions;`). 필수 멤버는 `CompleteAsync` 하나이고 나머지는 기본 구현(DIM)이 있습니다:

```csharp
namespace Flux.Abstractions;

public interface ITextCompletionService
{
    Task<string> CompleteAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    // 기본 구현: CompleteAsync 에 위임
    Task<string> CompleteJsonAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    // 기본 구현: 순차 실행
    Task<IReadOnlyList<string>> CompleteBatchAsync(
        IEnumerable<string> prompts,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    // 기본 구현: CompleteAsync 결과를 한 번에 yield
    IAsyncEnumerable<string> CompleteStreamAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

`TextCompletionOptions`는 `MaxTokens`(기본 500), `Temperature`(기본 0.7), `TopP`, `FrequencyPenalty`, `PresencePenalty`,
`StopSequences`, `SystemPrompt`, `ResponseFormat`(`"json"`이면 JSON 모드), `ResponseSchema`, `ThrowOnTruncation`,
`EnableThinking`을 나릅니다. provider가 지원하지 않는 항목은 무시해도 됩니다.

### 추상 클래스 사용

`TextCompletionServiceBase`를 상속하면 `CompleteCoreAsync()`만 구현하면 됩니다. 빈 프롬프트는 기반 클래스가 걸러내고,
`CompleteJsonAsync()`는 JSON 지시문을 붙이고 `ResponseFormat = "json"`으로 `CompleteCoreAsync()`를 부른 뒤 응답에서
JSON을 추출합니다:

```csharp
using Flux.Abstractions;
using FluxIndex.Core.Application.Services.Base;

public class MyTextCompletionService : TextCompletionServiceBase
{
    // 핵심 구현
    protected override async Task<string> CompleteCoreAsync(
        string prompt,
        TextCompletionOptions options,
        CancellationToken cancellationToken)
    {
        // 실제 LLM 호출
        return await YourLLMProvider.GenerateAsync(prompt, options.MaxTokens, options.Temperature, cancellationToken);
    }
}
```

### OpenAI 구현 예제

공식 `OpenAI` NuGet 패키지(2.x)의 `ChatClient`를 사용합니다. Azure OpenAI는 `Azure.AI.OpenAI` 2.x의
`AzureOpenAIClient.GetChatClient(deployment)`가 같은 `ChatClient`를 돌려줍니다. `options.ResponseFormat == "json"`을
provider의 JSON 모드로 옮기면 기반 클래스의 `CompleteJsonAsync()`가 그대로 네이티브 JSON 모드를 쓰게 됩니다:

```csharp
using Flux.Abstractions;
using FluxIndex.Core.Application.Services.Base;
using OpenAI.Chat;

public sealed class OpenAICompletionService : TextCompletionServiceBase
{
    private readonly ChatClient _client;

    public OpenAICompletionService(string apiKey, string model = "gpt-4o-mini")
    {
        _client = new ChatClient(model, apiKey);
    }

    protected override async Task<string> CompleteCoreAsync(
        string prompt,
        TextCompletionOptions options,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(options.SystemPrompt))
            messages.Add(ChatMessage.CreateSystemMessage(options.SystemPrompt));
        messages.Add(ChatMessage.CreateUserMessage(prompt));

        var chatOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = options.MaxTokens,
            Temperature = options.Temperature,
            TopP = options.TopP,
            FrequencyPenalty = options.FrequencyPenalty,
            PresencePenalty = options.PresencePenalty,
        };
        foreach (var stop in options.StopSequences ?? [])
            chatOptions.StopSequences.Add(stop);

        // CompleteJsonAsync 는 ResponseFormat = "json" 으로 이 메서드를 부른다
        if (options.ResponseFormat == "json")
            chatOptions.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat();

        var response = await _client.CompleteChatAsync(messages, chatOptions, cancellationToken);
        var completion = response.Value;

        if (options.ThrowOnTruncation && completion.FinishReason == ChatFinishReason.Length)
            throw new TextCompletionTruncatedException(options.MaxTokens);

        return string.Concat(completion.Content.Select(part => part.Text));
    }
}
```

---

## Reranker 구현

### 인터페이스

```csharp
namespace FluxIndex.Core.Application.Interfaces;

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

`RetrievalCandidate`·`RerankResult`·`RerankOptions`(`TopN`, `ScoreThreshold`, `MaxContentLength` 등)·`RerankModelInfo`·
`RerankModel`(`Local`, `Cohere`, `Custom`)은 같은 네임스페이스에 있습니다.

### 추상 클래스 사용

`RerankerBase`를 상속하면 `RerankCoreAsync()`와 `GetModelInfo()`만 구현하면 됩니다. 후보 content 자르기,
`RerankResult` 변환, `ScoreThreshold` 필터링은 기반 클래스가 합니다:

```csharp
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Base;

public class MyReranker : RerankerBase
{
    // 핵심 구현: 관련도 순으로 정렬한 (원래 index, score) 튜플 반환
    protected override async Task<IEnumerable<(int Index, float Score)>> RerankCoreAsync(
        string query,
        IReadOnlyList<string> documents,
        int topN,
        CancellationToken cancellationToken)
    {
        // 실제 reranking 로직
        var scores = await YourRerankerProvider.ScoreAsync(query, documents, cancellationToken);

        return scores
            .Select((score, index) => (Index: index, Score: score))
            .OrderByDescending(x => x.Score)
            .Take(topN);
    }

    public override RerankModelInfo GetModelInfo() => new()
    {
        Name = "my-reranker-v1",
        Type = RerankModel.Custom,
        RequiresApiKey = false
    };
}
```

### Cohere Reranker 예제

```csharp
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluxIndex.Core.Application.Interfaces;
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

        using var response = await _httpClient.PostAsJsonAsync(
            "https://api.cohere.ai/v1/rerank",
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CohereRerankResponse>(
            cancellationToken: cancellationToken);

        return result!.Results.Select(r => (r.Index, (float)r.RelevanceScore));
    }

    public override RerankModelInfo GetModelInfo() => new()
    {
        Name = _model,
        Type = RerankModel.Cohere,
        RequiresApiKey = true
    };

    private sealed record CohereRerankResponse(
        [property: JsonPropertyName("results")] CohereRerankResult[] Results);

    private sealed record CohereRerankResult(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("relevance_score")] double RelevanceScore);
}
```

---

## DI 등록

### 기본 패턴

```csharp
using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
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

    // LMSupply Embedding 등록 — 직접 쓰지 말고 FluxIndex.Providers.LMSupply의
    // services.AddLMSupplyEmbedding(...)을 쓴다(using FluxIndex.Providers.LMSupply.Extensions;).
    // 모델 로드는 컨테이너 해석 시점이 아니라
    // 첫 사용(또는 WarmUpOnStart로 호스트 기동) 시점에 비동기로 일어나며, 진행률·취소·타임아웃이
    // 옵션으로 통과된다. DI 팩토리 안에서 CreateAsync(...).GetAwaiter().GetResult()로 막는 형태는
    // 진행률·취소가 없고 SynchronizationContext가 있는 호스트에서 교착 후보라 채택하지 않는다.
    //
    //   services.AddLMSupplyEmbedding(o =>
    //   {
    //       o.ModelId = "default";
    //       o.Progress = new Progress<DownloadProgress>(p => Console.WriteLine(p));
    //       o.LoadTimeout = TimeSpan.FromMinutes(10);
    //       o.WarmUpOnStart = true;   // Generic Host: 기동 시 로드, 첫 요청은 다운로드를 기다리지 않는다
    //   });

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
using FluxIndex.Providers.LMSupply.Extensions;    // AddLMSupplyEmbedding
using FluxIndex.SDK;
using FluxIndex.Storage.PostgreSQL;
using FluxIndex.Storage.SQLite;

// 테스트 환경: InMemory embedding (명시적으로 선택 — 임베더를 등록하지 않으면 키워드 전용 컨텍스트)
var testContext = FluxIndexContext.CreateBuilder()
    .UseSQLite("test.db")
    .AddSQLiteStorage()
    .UseInMemoryEmbedding()
    .Build();

// 프로덕션 환경: OpenAI
var prodContext = FluxIndexContext.CreateBuilder()
    .UsePostgreSQL(connectionString)
    .AddPostgreSQLStorage()
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
    .AddSQLiteStorage()
    .ConfigureServices(services =>
    {
        services.AddLMSupplyEmbedding("default");
    })
    .Build();
```

---

## 실전 예제

### 완전한 OpenAI 통합 예제

```csharp
// 1. 패키지 참조 (소비 앱의 .csproj)
// <PackageReference Include="OpenAI" Version="2.14.0" />
// + 위의 OpenAIEmbeddingService / OpenAICompletionService 예제 클래스

using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

// 2. 래퍼 클래스 정의
public sealed class OpenAIServices
{
    public OpenAIServices(string apiKey)
    {
        Embedding = new OpenAIEmbeddingService(apiKey, "text-embedding-3-small");
        Completion = new OpenAICompletionService(apiKey, "gpt-4o-mini");
    }

    public IEmbeddingService Embedding { get; }
    public ITextCompletionService Completion { get; }
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
```

```csharp
// 4. 사용 (Program.cs — 최상위 문은 형식 선언과 다른 파일에 둔다)
using FluxIndex.Cache.Redis;
using FluxIndex.SDK;
using FluxIndex.Storage.PostgreSQL;

var connectionString = "Host=localhost;Database=fluxindex;Username=postgres;Password=...";

var context = FluxIndexContext.CreateBuilder()
    .UsePostgreSQL(connectionString)
    .AddPostgreSQLStorage()
    .ConfigureServices(s => s.AddOpenAIServices(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!))
    .UseRedisCache("localhost:6379")
    .AddRedisStorage()
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
using FluxIndex.SDK;
using FluxIndex.Storage.PostgreSQL;

var builder = WebApplication.CreateBuilder(args);

// AI 서비스 설정 로드
var aiConfig = builder.Configuration.GetSection("AI");
var apiKey = aiConfig["OpenAI:ApiKey"]!;

// FluxIndex 설정 — 벡터 스토어/AI Provider 배선은 빌더에 있으므로(옵션 객체가 아니라)
// 컨텍스트를 빌드해 싱글턴으로 등록한다. AI Provider 도 빌더의 ConfigureServices 로 넣어야
// 컨텍스트 자체 컨테이너에 도달한다(앱 서비스에 등록하면 컨텍스트가 보지 못함).
builder.Services.AddSingleton<IFluxIndexContext>(_ =>
    FluxIndexContext.CreateBuilder()
        .UsePostgreSQL(builder.Configuration.GetConnectionString("Default")!)
        .AddPostgreSQLStorage()
        .ConfigureServices(services =>
        {
            services.AddOpenAIEmbedding(apiKey, "text-embedding-3-small");
            services.AddOpenAICompletion(apiKey, "gpt-4o-mini");
        })
        .Build());

var app = builder.Build();
// 주입: 컨트롤러/서비스에서 IFluxIndexContext 를 받아 context.Indexer / context.Retriever 사용
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
A: 테스트 환경에서 `UseInMemoryEmbedding()`으로 명시해 사용합니다. 실제 임베딩을 생성하지 않고 결정적 랜덤 벡터를 반환하므로 검색 품질은 없지만 API 호출 없이 빠르게 테스트할 수 있습니다. 0.65.0부터 기본값이 아닙니다 — 임베더를 등록하지 않은 컨텍스트는 키워드 전용이 됩니다(README «Keyword-only by default»).

### Q: 여러 Embedding 모델을 동시에 사용할 수 있나요?
A: 네, 키 기반 등록으로 가능합니다:
```csharp
services.AddKeyedSingleton<IEmbeddingService>("openai", new OpenAIEmbeddingService(apiKey));
services.AddKeyedSingleton<IEmbeddingService>("local", await LMSupplyEmbedder.CreateAsync());
```

### Q: 비동기 초기화가 필요한 서비스는 어떻게 등록하나요?
A: DI 팩토리 안에서 `GetAwaiter().GetResult()`로 막지 않습니다 — 진행률·취소가 없고 `SynchronizationContext`가 있는
호스트에서 교착 후보입니다([DI 등록](#di-등록)). 두 가지 방법이 있습니다:

- **로드를 첫 사용으로 미룬다** — LMSupply 로컬 모델은 `FluxIndex.Providers.LMSupply`의 `AddLMSupplyEmbedding`이 이렇게
  동작합니다(Generic Host에서는 `WarmUpOnStart`로 기동 시 로드). 직접 구현한 서비스라면 `EmbedCoreAsync()` 안에서
  모델 로드 `Task`를 한 번만 만들어 기다립니다.
- **컨테이너를 만들기 전에 기다린다** — 앱 시작 코드가 이미 비동기라면 먼저 생성해 인스턴스로 등록합니다:

```csharp
var localEmbedder = await LMSupplyEmbedder.CreateAsync("default", cancellationToken);
services.AddSingleton<IEmbeddingService>(localEmbedder);
```

### Q: Anthropic Claude를 Text Completion에 사용하려면?
A: `TextCompletionServiceBase`를 상속하여 Claude API를 호출하는 래퍼를 작성합니다. `CompleteCoreAsync()` 메서드만 구현하면 됩니다.

---

## 관련 문서

- [GUIDE.md](./GUIDE.md) - FluxIndex 기본 사용법
- [REFERENCE.md](./REFERENCE.md) - API 레퍼런스
- [ADVANCED_RAG.md](./ADVANCED_RAG.md) - 고급 RAG 기능
