# FluxIndex.AI.OpenAI - AI 메타데이터 추출 서비스

FluxIndex의 OpenAI/Azure OpenAI 기반 메타데이터 추출 기능을 제공합니다.

## 주요 기능

### 🔍 **구조화된 메타데이터 추출**
- **제목 및 요약**: 문서 청크의 핵심 내용을 AI가 자동 생성
- **키워드 추출**: 검색 최적화를 위한 관련 키워드 자동 추출
- **엔터티 인식**: 인물, 기관, 장소, 개념 등 주요 엔터티 식별
- **질문 생성**: 내용 기반 자동 질문 생성으로 검색 확장성 향상

### ⚡ **배치 처리 및 성능 최적화**
- **대량 처리**: 여러 청크를 효율적으로 일괄 처리
- **재시도 로직**: 실패 시 자동 재시도 및 오류 복구
- **품질 검증**: 추출된 메타데이터의 품질 자동 평가
- **비용 최적화**: 배치 요청으로 API 호출 비용 절감

### 🧠 **지능형 프롬프트 엔지니어링**
- **동적 프롬프트**: 컨텍스트에 따른 프롬프트 자동 생성
- **도메인 특화**: 특정 도메인에 맞춤화된 메타데이터 추출
- **JSON 스키마 강제**: 일관된 구조화된 출력 보장
- **버전 관리**: 프롬프트 템플릿 버전 관리 및 A/B 테스팅

### 🔧 **SDK 통합 및 호환성**
- **기존 모델 호환**: 기존 DocumentChunk 모델과 완벽 호환
- **유연한 설정**: OpenAI와 Azure OpenAI 모두 지원
- **확장 메서드**: FluxIndexClient에 메타데이터 추출 기능 추가
- **의존성 주입**: .NET DI 컨테이너와 완전 통합

## 빠른 시작

### 1. 기본 설정

```csharp
// appsettings.json
{
  "OpenAI": {
    "ApiKey": "your-openai-api-key",
    "Model": "gpt-5-nano",
    "MaxTokens": 1000,
    "Temperature": 0.3
  },
  "MetadataExtraction": {
    "MaxKeywords": 10,
    "MaxEntities": 15,
    "MaxQuestions": 5,
    "BatchSize": 5,
    "EnableQualityScoring": true
  }
}

// Program.cs
services.AddOpenAIMetadataExtraction(configuration);
```

### 2. FluxIndex 클라이언트와 통합

```csharp
var client = new FluxIndexClientBuilder()
    .WithVectorStore(store => /* vector store 설정 */)
    .WithAIMetadataExtraction(options =>
    {
        options.ApiKey = "your-openai-api-key";
        options.Model = "gpt-5-nano";
        options.Temperature = 0.3f;
    })
    .Build();

// AI 메타데이터와 함께 문서 인덱싱
var chunks = new List<DocumentChunk>
{
    DocumentChunk.Create("doc1", "인공지능과 머신러닝의 발전...", 0, 1),
    DocumentChunk.Create("doc1", "딥러닝 모델의 성능 향상...", 1, 1)
};

await client.IndexWithAIMetadataAsync(chunks);
```

### 3. 단일 청크 메타데이터 추출

```csharp
var chunk = DocumentChunk.Create("doc1", "블록체인 기술의 미래 전망", 0, 1);

// AI 메타데이터 추출 및 적용
await client.EnrichChunkMetadataAsync(chunk, "기술 동향 분석 문서");

// 추출된 메타데이터 확인
Console.WriteLine($"AI 생성 제목: {chunk.Properties["ai_generated_title"]}");
Console.WriteLine($"AI 생성 요약: {chunk.Properties["ai_generated_summary"]}");
Console.WriteLine($"품질 점수: {chunk.Properties["ai_quality_score"]}");
```

### 4. 배치 처리

```csharp
var batchOptions = new BatchProcessingOptions
{
    Size = 3,
    DelayBetweenBatches = TimeSpan.FromMilliseconds(500),
    ContinueOnFailure = true,
    ProgressCallback = (processed, total) =>
        Console.WriteLine($"진행률: {processed}/{total}")
};

await client.IndexWithAIMetadataAsync(chunks, batchOptions);
```

## Azure OpenAI 설정

```csharp
// Azure OpenAI 사용
var client = new FluxIndexClientBuilder()
    .WithVectorStore(store => /* vector store 설정 */)
    .WithAzureAIMetadataExtraction(
        apiKey: "your-azure-openai-key",
        resourceUrl: "https://your-resource.openai.azure.com",
        deploymentName: "gpt-5-nano"
    )
    .Build();
```

## 고급 사용법

### 1. 사용자 정의 스키마

```csharp
var customSchema = new
{
    Domain = "Software Engineering",
    RequiredFields = new[] { "complexity", "framework", "language" }
};

var metadata = await enrichmentService.ExtractWithSchemaAsync(
    content: "React 컴포넌트 개발 가이드",
    schema: customSchema
);
```

### 2. 상태 모니터링

```csharp
// 서비스 상태 확인
var isHealthy = await client.IsAIMetadataServiceHealthyAsync();

// 사용 통계 조회
var stats = await client.GetAIMetadataStatisticsAsync();
Console.WriteLine($"총 처리: {stats.TotalProcessedChunks}");
Console.WriteLine($"성공률: {stats.SuccessRate:P}");
Console.WriteLine($"평균 품질: {stats.AverageQualityScore:F2}");
```

### 3. 검색 결과 향상

```csharp
// AI 메타데이터가 포함된 검색
var results = await client.SearchWithAIMetadataAsync("머신러닝 알고리즘");

foreach (var result in results)
{
    Console.WriteLine($"제목: {result.ExplanationMetadata["ai_title"]}");
    Console.WriteLine($"요약: {result.ExplanationMetadata["ai_summary"]}");
    Console.WriteLine($"키워드: {string.Join(", ", (List<string>)result.ExplanationMetadata["ai_keywords"])}");
    Console.WriteLine($"하이라이트: {result.HighlightedContent}");
}
```

## 설정 옵션

### OpenAI 옵션

```csharp
services.Configure<OpenAIOptions>(options =>
{
    options.ApiKey = "your-api-key";
    options.BaseUrl = "https://api.openai.com"; // 또는 Azure URL
    options.Model = "gpt-5-nano";
    options.MaxTokens = 1500;
    options.Temperature = 0.3f;
    options.TopP = 1.0f;
    options.IsAzure = false; // Azure 사용 시 true
    options.DeploymentName = ""; // Azure 배포명
});
```

### 메타데이터 추출 옵션

```csharp
services.Configure<MetadataExtractionOptions>(options =>
{
    options.MaxKeywords = 15;           // 최대 키워드 수
    options.MaxEntities = 20;           // 최대 엔터티 수
    options.MaxQuestions = 8;           // 최대 질문 수
    options.BatchSize = 10;             // 배치 처리 크기
    options.Timeout = TimeSpan.FromSeconds(45);
    options.MaxRetries = 3;             // 재시도 횟수
    options.MaxConcurrency = 5;         // 동시 처리 제한
    options.MinQualityThreshold = 0.3f; // 최소 품질 임계값
    options.EnableDebugLogging = false; // 디버그 로깅
    options.EnableCostTracking = true;  // 비용 추적
});
```

## 테스트

### 단위 테스트 예제

```csharp
[Fact]
public async Task ExtractMetadata_ValidContent_ReturnsQualityMetadata()
{
    // Arrange
    var mockClient = new Mock<IOpenAIClient>();
    mockClient.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(ValidJsonResponse);

    var service = OpenAIMetadataEnrichmentService.CreateForTesting(
        mockClient.Object,
        MetadataExtractionOptions.CreateForTesting()
    );

    // Act
    var result = await service.ExtractMetadataAsync("AI 기술의 발전과 응용");

    // Assert
    Assert.NotEmpty(result.Title);
    Assert.NotEmpty(result.Summary);
    Assert.True(result.QualityScore > 0.5f);
    Assert.True(result.IsValid);
}
```

### 통합 테스트

```csharp
[Fact]
public async Task EndToEnd_MetadataExtraction_WorksCorrectly()
{
    var services = new ServiceCollection();
    services.AddOpenAIMetadataExtraction(/* 설정 */);
    services.AddSingleton<DocumentChunkMetadataIntegration>();

    var provider = services.BuildServiceProvider();
    var integration = provider.GetRequiredService<DocumentChunkMetadataIntegration>();

    var chunk = DocumentChunk.Create("test", "테스트 문서 내용", 0, 1);
    await integration.EnrichDocumentChunkAsync(chunk);

    Assert.NotNull(chunk.Properties["ai_generated_title"]);
    Assert.True((float)chunk.Properties["ai_quality_score"] > 0);
}
```

## 트러블슈팅

### 일반적인 문제들

1. **API 키 인증 실패**
   ```
   해결: appsettings.json의 API 키 확인 또는 환경 변수 설정
   ```

2. **JSON 파싱 오류**
   ```
   해결: Temperature 값을 낮춰서 더 일관된 JSON 출력 생성
   options.Temperature = 0.1f;
   ```

3. **품질 점수 낮음**
   ```
   해결: MinQualityThreshold 조정 또는 프롬프트 개선
   options.MinQualityThreshold = 0.2f;
   ```

4. **배치 처리 실패**
   ```
   해결: 배치 크기 줄이기 또는 재시도 횟수 증가
   options.BatchSize = 3;
   options.MaxRetries = 5;
   ```

### 성능 최적화

- **배치 크기 조정**: 배치 크기를 늘려 API 호출 횟수 감소
- **동시성 제어**: MaxConcurrency로 동시 요청 수 조절
- **캐싱 활용**: 동일한 내용에 대한 중복 요청 방지
- **프롬프트 최적화**: 더 간결한 프롬프트로 토큰 사용량 감소

## 라이선스

이 프로젝트는 MIT 라이선스 하에 배포됩니다.