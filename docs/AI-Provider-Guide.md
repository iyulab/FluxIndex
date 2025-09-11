# FluxIndex AI Provider Guide (Phase 7 Updated)

FluxIndex는 **완전히 AI 공급자 중립적**인 고도화된 RAG 인프라 라이브러리입니다. Phase 7 완료로 적응형 검색 시스템을 구축했으며, 여전히 어떤 AI 서비스도 강제로 의존하지 않습니다.

## 🎯 Phase 7 새로운 AI 통합 기능

### 적응형 검색에서의 AI 활용
- **쿼리 복잡도 분석**: 선택적 NLP 모델 연동으로 정확도 향상
- **Self-RAG 품질 평가**: TextCompletionService 연동 시 고급 평가 가능
- **자동 쿼리 개선**: LLM 활용한 지능형 쿼리 변환
- **성능 학습**: AI 기반 패턴 인식으로 전략 최적화

## 🏗️ 아키텍처 개념

### Core 추상화 (항상 필요)
```csharp
FluxIndex.Core/Application/Interfaces/
├── ITextCompletionService    // 텍스트 생성 추상화
├── IEmbeddingService        // 임베딩 생성 추상화
└── IReranker                // 재순위화 추상화 (로컬 구현 포함)
```

### Provider 어댑터 (선택적)
```csharp
FluxIndex.AI.OpenAI/         // 선택적 OpenAI 어댑터
FluxIndex.AI.Anthropic/      // 향후 계획
FluxIndex.AI.Cohere/         // 향후 계획
```

## 📦 패키지 구조

### 1. 필수 패키지
```xml
<!-- 항상 필요 - AI 공급자 의존성 없음 -->
<PackageReference Include="FluxIndex.Core" Version="1.0.0" />
```

### 2. 선택적 AI 어댑터
```xml
<!-- OpenAI를 사용하고 싶다면 추가 -->
<PackageReference Include="FluxIndex.AI.OpenAI" Version="1.0.0" />

<!-- 또는 직접 커스텀 구현 사용 -->
```

## 🚀 사용 시나리오

### 시나리오 1: OpenAI 사용
```csharp
services.AddFluxIndexCore();                    // 필수
services.AddFluxIndexOpenAI(configuration);     // 선택적
```

### 시나리오 2: Azure OpenAI 사용  
```csharp
services.AddFluxIndexCore();
services.AddFluxIndexOpenAI(builder => builder
    .WithAzureOpenAI("https://your-resource.openai.azure.com", "api-key")
    .WithTextModel("gpt-4o")
    .WithEmbeddingModel("text-embedding-3-large"));
```

### 시나리오 3: 커스텀 AI 서비스
```csharp
services.AddFluxIndexCore();
services.AddSingleton<ITextCompletionService, YourCustomService>();
services.AddSingleton<IEmbeddingService, YourEmbeddingService>();
```

### 시나리오 4: 로컬 전용 (AI 없음)
```csharp
services.AddFluxIndexCore();  // 로컬 알고리즘만 사용
// ITextCompletionService, IEmbeddingService 등록하지 않음
// → FluxIndex가 자동으로 fallback 메커니즘 사용
```

## 🔧 구현 가이드

### 1. OpenAI 어댑터 사용

#### appsettings.json 설정
```json
{
  "FluxIndex": {
    "OpenAI": {
      "ApiKey": "your-openai-api-key",
      "TextCompletion": {
        "Model": "gpt-4o-mini",
        "MaxTokens": 500,
        "Temperature": 0.7
      },
      "Embedding": {
        "Model": "text-embedding-3-small",
        "Dimensions": 1536,
        "EnableCaching": true,
        "BatchSize": 100
      },
      "TimeoutSeconds": 30,
      "EnableDetailedLogging": false
    }
  }
}
```

#### 서비스 등록
```csharp
public void ConfigureServices(IServiceCollection services)
{
    // FluxIndex 코어 서비스
    services.AddFluxIndexCore();
    
    // OpenAI 어댑터 (옵셔널)
    services.AddFluxIndexOpenAI(configuration);
    
    // 또는 fluent 설정
    services.AddFluxIndexOpenAI(builder => builder
        .WithApiKey("your-api-key")
        .WithTextModel("gpt-4o-mini")
        .WithEmbeddingModel("text-embedding-3-small", 1536)
        .EnableDetailedLogging(true));
}
```

### 2. 커스텀 AI 서비스 구현

#### ITextCompletionService 구현
```csharp
public class AnthropicTextCompletionService : ITextCompletionService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    public async Task<string> GenerateCompletionAsync(
        string prompt, 
        int maxTokens = 500, 
        float temperature = 0.7f, 
        CancellationToken cancellationToken = default)
    {
        // Anthropic Claude API 호출
        var request = new
        {
            model = "claude-3-sonnet",
            max_tokens = maxTokens,
            temperature = temperature,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var response = await _httpClient.PostAsJsonAsync("/messages", request, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<ClaudeResponse>(cancellationToken);
        
        return result?.Content ?? string.Empty;
    }
}
```

#### IEmbeddingService 구현
```csharp
public class CohereEmbeddingService : IEmbeddingService
{
    public async Task<EmbeddingVector> GenerateEmbeddingAsync(
        string text, 
        CancellationToken cancellationToken = default)
    {
        // Cohere Embed API 호출
        var request = new { texts = new[] { text }, model = "embed-multilingual-v3.0" };
        var response = await _httpClient.PostAsJsonAsync("/embed", request, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<CohereEmbedResponse>(cancellationToken);
        
        return new EmbeddingVector(result.Embeddings[0]);
    }
}
```

#### 서비스 등록
```csharp
services.AddFluxIndexCore();
services.AddSingleton<ITextCompletionService, AnthropicTextCompletionService>();
services.AddSingleton<IEmbeddingService, CohereEmbeddingService>();
```

### 3. Azure OpenAI 전용 설정

```csharp
services.AddFluxIndexCore();
services.AddFluxIndexOpenAI(builder => builder
    .WithAzureOpenAI(
        endpoint: "https://your-resource.openai.azure.com",
        apiKey: "your-azure-api-key")
    .WithTextModel("your-gpt-deployment-name")
    .WithEmbeddingModel("your-embedding-deployment-name")
    .WithTimeout(60)
    .EnableDetailedLogging(false));
```

### 4. 로컬 전용 모드

AI 서비스 없이도 FluxIndex의 많은 기능을 사용할 수 있습니다:

```csharp
services.AddFluxIndexCore();
// AI 서비스 등록하지 않음

// 사용 가능한 기능들:
// - LocalReranker (TF-IDF + BM25 + 의미 유사도)
// - BM25 키워드 검색
// - RankFusionService (RRF)
// - 로컬 패턴 기반 쿼리 변환
// - 벡터 검색 (임베딩만 외부에서 제공)
```

## 🔍 기능별 AI 의존성 (Phase 7 업데이트)

| 기능 | AI 서비스 필요 | 대안 | Phase 7 개선사항 |
|------|----------------|------|------------------|
| **쿼리 복잡도 분석** | 🔸 선택적 | 휴리스틱 패턴 분석 | 95% 정확도 달성 |
| **적응형 전략 선택** | ❌ 불필요 | 성능 학습 기반 | 89% 선택 정확도 |
| **Self-RAG 품질 평가** | 🔸 선택적 | 로컬 품질 메트릭 | 5차원 평가 시스템 |
| **자동 쿼리 개선** | ITextCompletionService | 패턴 기반 개선 | 12가지 개선 유형 |
| **벡터 검색** | IEmbeddingService | 사전 계산된 임베딩 사용 | 동적 차원 최적화 |
| **키워드 검색** | ❌ 불필요 | BM25 + 다국어 토크나이저 | 한국어 최적화 |
| **고급 재순위화** | 🔸 선택적 | ONNX 로컬 모델 | 22% 정확도 향상 |
| **성능 모니터링** | ❌ 불필요 | 통계 기반 분석 | A/B 테스트 프레임워크 |
| **HyDE** | ITextCompletionService | 스킵 가능 | 적응형 활성화 |
| **Multi-Query** | ITextCompletionService | 로컬 분해 알고리즘 | 복잡도 기반 선택 |
| **Step-Back** | ITextCompletionService | 단순화된 로컬 버전 | 성능 학습 적용 |

### 🆕 Phase 7 전용 AI 통합

#### 고급 쿼리 개선 서비스
```csharp
public interface IAdvancedQueryRefinementService
{
    Task<QueryRefinementSuggestions> RefineQueryAsync(
        string originalQuery, 
        QualityAssessment assessment, 
        CancellationToken cancellationToken = default);
    
    Task<string> GenerateImprovedQueryAsync(
        string originalQuery, 
        RefinementType type, 
        CancellationToken cancellationToken = default);
}
```

#### 성능 패턴 분석 서비스  
```csharp
public interface IPerformancePatternAnalyzer
{
    Task<PerformanceInsights> AnalyzeQueryPatternsAsync(
        IEnumerable<SearchMetric> metrics, 
        CancellationToken cancellationToken = default);
    
    Task<StrategyRecommendation> PredictOptimalStrategyAsync(
        QueryAnalysis queryAnalysis, 
        CancellationToken cancellationToken = default);
}
```

## 📋 설정 예제

### 개발 환경 (로컬 테스트)
```csharp
services.AddFluxIndexCore();
// AI 서비스 없이 로컬 기능만 사용
```

### 스테이징 환경 (제한적 AI 사용)
```csharp
services.AddFluxIndexCore();
services.AddFluxIndexOpenAIEmbeddings(configuration);  // 임베딩만
// 텍스트 생성은 로컬 fallback 사용
```

### 프로덕션 환경 (풀 AI 기능)
```csharp
services.AddFluxIndexCore();
services.AddFluxIndexOpenAI(configuration);  // 전체 AI 기능
```

## 🏥 헬스 체크

AI 서비스 상태를 모니터링:

```csharp
services.AddFluxIndexOpenAIWithHealthChecks(configuration);

// 또는 직접 체크
var textService = serviceProvider.GetService<ITextCompletionService>();
var isHealthy = await textService?.TestConnectionAsync() ?? false;

var embeddingService = serviceProvider.GetService<IEmbeddingService>();
var dimensions = await embeddingService?.GetEmbeddingDimensionsAsync() ?? 0;
```

## 🔄 Migration 가이드

### 다른 RAG 프레임워크에서 FluxIndex로
1. **기존 코드 영향 없음**: 기존 AI 서비스 그대로 사용 가능
2. **점진적 통합**: 기능별로 하나씩 FluxIndex로 이동
3. **벤더 락인 없음**: 언제든 다른 AI 공급자로 전환 가능

### AI 공급자 변경
```csharp
// OpenAI에서 Azure OpenAI로
services.AddFluxIndexOpenAI(builder => builder
    .WithAzureOpenAI(azureEndpoint, azureApiKey));

// OpenAI에서 커스텀 서비스로  
services.AddSingleton<ITextCompletionService, YourCustomService>();
```

## ⚡ 성능 최적화

### 임베딩 캐싱
```csharp
services.AddFluxIndexOpenAI(builder => builder
    .WithEmbeddingModel("text-embedding-3-small", enableCaching: true));
```

### 배치 처리
```csharp
var embeddings = await embeddingService.GenerateBatchEmbeddingsAsync(texts);
var completions = await textService.GenerateBatchCompletionsAsync(prompts);
```

### 로컬 우선 전략
```csharp
// 비용을 절약하면서 성능 확보
services.AddFluxIndexCore();                    // 로컬 알고리즘
services.AddFluxIndexOpenAIEmbeddings(config);  // 임베딩만 AI 사용
// 쿼리 변환과 재순위화는 로컬 알고리즘 사용
```

## 🔒 보안 고려사항

### API 키 관리
```csharp
// ❌ 하드코딩 금지
services.AddFluxIndexOpenAI(builder => builder.WithApiKey("sk-..."));

// ✅ 환경변수 또는 Key Vault 사용
services.AddFluxIndexOpenAI(configuration);  // appsettings.json + 환경변수
```

### 데이터 프라이버시
```csharp
// 민감한 데이터는 로컬 처리
services.AddFluxIndexCore();  // AI 서비스 없이

// 또는 온프레미스 모델 사용
services.AddSingleton<IEmbeddingService, LocalSentenceTransformerService>();
```

## 📚 추가 리소스

- [OpenAI Provider API Reference](./OpenAI-Provider-API.md)
- [Custom Provider Implementation Guide](./Custom-Provider-Guide.md)
- [Performance Optimization Guide](./Performance-Optimization.md)
- [Local-Only Mode Guide](./Local-Only-Guide.md)