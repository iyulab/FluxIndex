# Phase 3.1: 메타데이터 추출 서비스 구현 태스크

## 📋 전체 개요
**목표**: LLM 기반 메타데이터 자동 추출로 하이브리드 검색 기반 마련
**기간**: 2주 (10 영업일)
**성과 지표**: 메타데이터 추출 성공률 95%+, 배치 처리 효율 5x 향상

---

## 🏗️ Task 1: Core 인터페이스 및 모델 설계 (3일)

### 1.1 Domain Model 확장 (1일)
```csharp
// FluxIndex.Core/Domain/ValueObjects/ChunkMetadata.cs
public class ChunkMetadata
{
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public List<string> Keywords { get; init; } = new();
    public List<string> Entities { get; init; } = new();
    public List<string> GeneratedQuestions { get; init; } = new();
    public Dictionary<string, object> CustomFields { get; init; } = new();
    public float QualityScore { get; init; }
    public DateTime ExtractedAt { get; init; }
}
```

### 1.2 서비스 인터페이스 정의 (1일)
```csharp
// FluxIndex.Core/Application/Interfaces/IMetadataEnrichmentService.cs
public interface IMetadataEnrichmentService
{
    Task<ChunkMetadata> ExtractMetadataAsync(
        string content,
        string? context = null,
        CancellationToken cancellationToken = default);

    Task<List<ChunkMetadata>> ExtractBatchAsync(
        List<string> contents,
        BatchOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<ChunkMetadata> ExtractWithSchemaAsync<T>(
        string content,
        T schema,
        CancellationToken cancellationToken = default) where T : class;
}
```

### 1.3 설정 모델 및 옵션 (1일)
```csharp
// FluxIndex.Core/Application/Options/MetadataExtractionOptions.cs
public class MetadataExtractionOptions
{
    public int MaxKeywords { get; set; } = 10;
    public int MaxEntities { get; set; } = 15;
    public int MaxQuestions { get; set; } = 5;
    public int BatchSize { get; set; } = 5;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool EnableQualityScoring { get; set; } = true;
    public string PromptTemplate { get; set; } = DefaultPrompts.MetadataExtraction;
}
```

---

## 🔧 Task 2: OpenAI Provider 구현 (4일)

### 2.1 OpenAI 메타데이터 추출 서비스 (2일)
```csharp
// FluxIndex.AI.OpenAI/Services/OpenAIMetadataEnrichmentService.cs
public class OpenAIMetadataEnrichmentService : IMetadataEnrichmentService
{
    private readonly OpenAIClient _client;
    private readonly ILogger<OpenAIMetadataEnrichmentService> _logger;
    private readonly MetadataExtractionOptions _options;

    public async Task<ChunkMetadata> ExtractMetadataAsync(
        string content,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        // 구조화된 출력을 위한 JSON Schema 기반 프롬프트
        // Pydantic-style 스키마 강제
        // 재시도 로직 및 오류 처리
    }
}
```

### 2.2 프롬프트 엔지니어링 및 템플릿 (1일)
```csharp
// FluxIndex.AI.OpenAI/Prompts/MetadataPrompts.cs
public static class MetadataPrompts
{
    public const string ExtractionPrompt = @"
Extract structured metadata from the following text chunk.
Return a JSON object with the following schema:

{
  ""title"": ""Clear, descriptive title (max 100 chars)"",
  ""summary"": ""Concise summary (max 200 chars)"",
  ""keywords"": [""key1"", ""key2"", ""key3""],
  ""entities"": [""Person"", ""Organization"", ""Location""],
  ""generated_questions"": [""What does this explain?"", ""How is this used?""],
  ""quality_score"": 0.85
}

Text to analyze:
{content}

Context (if available):
{context}
";
}
```

### 2.3 배치 처리 최적화 (1일)
- 5개 단위 배치 처리로 API 효율성 극대화
- 동시 요청 제한 및 백프레셔 처리
- 비용 모니터링 및 토큰 사용량 추적

---

## 🧪 Task 3: 테스트 및 검증 (2일)

### 3.1 단위 테스트 (1일)
```csharp
// Tests/FluxIndex.AI.OpenAI.Tests/OpenAIMetadataEnrichmentServiceTests.cs
[Test]
public async Task ExtractMetadataAsync_ValidContent_ReturnsStructuredMetadata()
{
    // Given
    var content = "FluxIndex is a RAG optimization library...";

    // When
    var result = await _service.ExtractMetadataAsync(content);

    // Then
    result.Title.Should().NotBeEmpty();
    result.Keywords.Should().NotBeEmpty();
    result.QualityScore.Should().BeGreaterThan(0);
}
```

### 3.2 통합 테스트 및 품질 검증 (1일)
- 실제 문서 청크를 활용한 품질 평가
- 추출된 메타데이터의 정확성 검증
- 성능 벤치마크 (처리 시간, 비용)

---

## ⚙️ Task 4: SDK 통합 및 설정 (1일)

### 4.1 FluxIndexClientBuilder 확장
```csharp
// FluxIndex.SDK/FluxIndexClientBuilder.cs
public FluxIndexClientBuilder WithMetadataEnrichment(
    Action<MetadataExtractionOptions>? configure = null)
{
    var options = new MetadataExtractionOptions();
    configure?.Invoke(options);

    _services.Configure<MetadataExtractionOptions>(opts =>
    {
        opts.MaxKeywords = options.MaxKeywords;
        opts.BatchSize = options.BatchSize;
        // ... 기타 옵션 설정
    });

    _services.AddScoped<IMetadataEnrichmentService, OpenAIMetadataEnrichmentService>();
    return this;
}
```

### 4.2 DocumentChunk 모델 확장
```csharp
// FluxIndex.Core/Domain/Entities/DocumentChunk.cs 확장
public class DocumentChunk
{
    // 기존 속성들...
    public ChunkMetadata? Metadata { get; private set; }

    public void EnrichMetadata(ChunkMetadata metadata)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        AddProperty("HasMetadata", true);
        AddProperty("MetadataExtractedAt", metadata.ExtractedAt);
    }
}
```

---

## 📊 성공 기준 및 검증

### 정량적 지표
- ✅ **추출 성공률**: 95% 이상
- ✅ **처리 속도**: 청크당 평균 2초 이내
- ✅ **배치 효율성**: 단일 요청 대비 5배 향상
- ✅ **비용 효율성**: 토큰당 비용 30% 절감

### 정성적 지표
- ✅ **키워드 품질**: 수동 검증 시 80% 이상 관련성
- ✅ **질문 품질**: 생성된 질문의 답변 가능성 85% 이상
- ✅ **요약 품질**: 원본 내용 대비 정보 손실 최소화

---

## 🚀 다음 단계 연결점

**Phase 4.1 하이브리드 검색 준비**:
- 추출된 키워드 → BM25 희소 인덱스 구축
- 생성된 질문 → QuOTE 임베딩 전략
- 엔터티 정보 → 구조화된 필터링

**즉시 혜택**:
- 하이브리드 검색 없이도 메타데이터 기반 필터링 가능
- 검색 결과 설명 가능성 향상
- 사용자 대시보드에서 콘텐츠 미리보기 품질 개선