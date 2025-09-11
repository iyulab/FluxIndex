# Modern RAG 가이드

FluxIndex의 고도화된 RAG 기능 활용 방법을 설명합니다.

## 🚀 핵심 기능

### 1. 풍부한 메타데이터 자동 추출
- **텍스트 분석**: 토큰 수, 가독성 점수, 언어 감지
- **의미적 메타데이터**: 키워드, 엔터티, 토픽 자동 추출
- **구조적 메타데이터**: 섹션 레벨, 제목, 맥락 요약
- **검색 최적화**: 중요도 점수, 검색 용어 가중치

### 2. 청크 간 관계 그래프
- **8가지 관계 유형**: 순차, 의미적, 참조, 인과, 계층, 유사성, 모순, 보충
- **관계 강도**: 0.0-1.0 정량화된 관계 점수
- **자동 관계 분석**: 임베딩 기반 의미적 유사성 분석

### 3. 다차원 품질 평가
- **콘텐츠 품질**: 완성도, 정보 밀도, 응집성, 고유성
- **검색 품질**: 쿼리 관련성, 맥락적 관련성, 권위도, 최신성
- **사용자 피드백**: 평점, 클릭률, 검색 빈도 추적

### 4. 고급 재순위화 전략
- **6가지 전략**: Semantic, Quality, Contextual, Hybrid, LLM, Adaptive
- **자동 전략 선택**: 쿼리 분석 기반 최적 전략 자동 선택
- **설명 가능한 AI**: 점수 구성 요소 및 선택 근거 제공

## 💻 사용 예제

### 1. 기본 풍부화 인덱싱

```csharp
using FluxIndex.SDK;
using FluxIndex.Core.Domain.Entities;

// 1. 클라이언트 설정 (메타데이터 풍부화 활성화)
var client = FluxIndexClient.CreateBuilder()
    .ConfigureVectorStore(store => store.UsePostgreSQL(connectionString))
    .ConfigureEmbedding(embed => embed.UseOpenAI(apiKey))
    .EnableMetadataEnrichment() // 고급 메타데이터 활성화
    .EnableAdvancedReranking() // 재순위화 활성화
    .Build();

// 2. 청크 생성 (FileFlux 결과)
var chunks = new[]
{
    new DocumentChunk("스마트폰 배터리 최적화 방법을 알아보겠습니다.", 0),
    new DocumentChunk("첫 번째로 배터리 설정을 확인하세요.", 1),
    new DocumentChunk("절전 모드를 활용하면 배터리 수명이 30% 연장됩니다.", 2)
};

// 3. 풍부한 메타데이터와 함께 인덱싱
await client.IndexChunksAsync(chunks, 
    documentId: "battery_guide",
    metadata: new Dictionary<string, object>
    {
        ["topic"] = "기술",
        ["difficulty"] = "초급",
        ["author"] = "기술팀"
    });
```

### 2. 고급 검색 및 재순위화

```csharp
// 1. 적응형 재순위화 (자동 최적 전략 선택)
var results = await client.AdvancedSearchAsync(
    query: "배터리 수명을 늘리는 방법",
    topK: 10,
    rerankingStrategy: RerankingStrategy.Adaptive);

foreach (var result in results)
{
    Console.WriteLine($"점수: {result.RerankedScore:F3}");
    Console.WriteLine($"내용: {result.HighlightedContent}");
    Console.WriteLine($"설명: {result.ExplanationMetadata["adaptive_strategy"]}");
    
    // 관련 청크 표시
    foreach (var related in result.RelatedChunks)
    {
        Console.WriteLine($"  → 관련: {related.Type} (강도: {related.Strength:F2})");
    }
}
```

### 3. 맥락적 검색 (관련 청크 확장)

```csharp
// 맥락 확장 검색 - 관련 청크 자동 포함
var contextResults = await client.ContextualSearchAsync(
    query: "배터리 최적화",
    topK: 5,
    includeRelatedChunks: true);

foreach (var result in contextResults)
{
    var contextType = result.ExplanationMetadata.GetValueOrDefault("context_type", "primary");
    
    Console.WriteLine($"[{contextType}] {result.Chunk.Content}");
    
    if (contextType.ToString().StartsWith("sequential"))
    {
        Console.WriteLine("  ↳ 순차적 맥락 청크");
    }
    else if (contextType.ToString().StartsWith("related"))
    {
        Console.WriteLine("  ↳ 의미적 관련 청크");
    }
}
```

### 4. 품질 기반 재순위화

```csharp
// 품질 중심 검색 - 고품질 콘텐츠 우선
var qualityResults = await client.AdvancedSearchAsync(
    query: "전문적인 배터리 관리 방법",
    topK: 10,
    rerankingStrategy: RerankingStrategy.Quality);

foreach (var result in qualityResults)
{
    var quality = result.Chunk.Quality;
    
    Console.WriteLine($"콘텐츠: {result.Chunk.Content}");
    Console.WriteLine($"품질 점수:");
    Console.WriteLine($"  완성도: {quality.ContentCompleteness:F2}");
    Console.WriteLine($"  정보밀도: {quality.InformationDensity:F2}");
    Console.WriteLine($"  응집성: {quality.Coherence:F2}");
    Console.WriteLine($"  권위도: {quality.AuthorityScore:F2}");
    
    if (quality.UserRating > 0)
    {
        Console.WriteLine($"  사용자 평점: {quality.UserRating:F1}/5.0");
    }
}
```

### 5. LLM 기반 재순위화

```csharp
// LLM을 활용한 관련성 평가
var llmResults = await client.AdvancedSearchAsync(
    query: "배터리가 빨리 닳는 이유와 해결책을 자세히 설명해줘",
    topK: 10,
    rerankingStrategy: RerankingStrategy.LLM);

foreach (var result in llmResults)
{
    var llmEval = result.ExplanationMetadata["llm_evaluation"];
    
    Console.WriteLine($"LLM 평가 점수: {result.RerankedScore:F3}");
    Console.WriteLine($"내용: {result.Chunk.Content}");
    Console.WriteLine($"LLM 근거: {llmEval}");
}
```

### 6. 메타데이터 기반 필터링

```csharp
// 특정 조건으로 필터링된 검색
var filteredResults = await client.AdvancedSearchAsync(
    query: "배터리 최적화",
    topK: 10,
    filters: new Dictionary<string, object>
    {
        ["topic"] = "기술",
        ["difficulty"] = "초급",
        ["author"] = "기술팀"
    });

foreach (var result in filteredResults)
{
    var metadata = result.Chunk.Metadata;
    
    Console.WriteLine($"내용: {result.Chunk.Content}");
    Console.WriteLine($"키워드: {string.Join(", ", metadata.Keywords)}");
    Console.WriteLine($"엔터티: {string.Join(", ", metadata.Entities)}");
    Console.WriteLine($"중요도: {metadata.ImportanceScore:F2}");
    
    if (metadata.SectionLevel > 0)
    {
        Console.WriteLine($"섹션: H{metadata.SectionLevel} - {metadata.SectionTitle}");
    }
}
```

### 7. 사용자 피드백 반영

```csharp
// 검색 결과에 대한 사용자 피드백 저장
foreach (var result in searchResults)
{
    // 사용자가 클릭한 결과
    if (userClickedResults.Contains(result.Chunk.Id))
    {
        await UpdateClickFeedbackAsync(result.Chunk.Id);
    }
    
    // 사용자 평점 반영
    if (userRatings.TryGetValue(result.Chunk.Id, out var rating))
    {
        await UpdateUserRatingAsync(result.Chunk.Id, rating);
    }
}

// 피드백 기반 성능 개선
private async Task UpdateClickFeedbackAsync(string chunkId)
{
    // 클릭률 업데이트 로직
    var chunk = await client.GetChunkAsync(chunkId);
    chunk.Quality.RetrievalCount++;
    chunk.Quality.ClickThroughRate = /* 계산 로직 */;
    chunk.Quality.LastAccessed = DateTime.UtcNow;
    
    await client.UpdateChunkAsync(chunk);
}
```

## 📊 성능 최적화 팁

### 1. 메타데이터 크기 최적화
```csharp
// 키워드 수 제한 (성능 vs 정확도 균형)
var enrichmentOptions = new MetadataEnrichmentOptions
{
    MaxKeywords = 10,        // 기본값, 필요시 조정
    MaxEntities = 20,        // 엔터티 추출 제한
    EnableTopicClassification = true,
    EnableSentimentAnalysis = false  // 필요 없으면 비활성화
};
```

### 2. 재순위화 성능 튜닝
```csharp
// 배치 크기 조정 (LLM 재순위화)
var rerankingOptions = new RerankingOptions
{
    LLMBatchSize = 5,        // 토큰 제한 고려
    MaxCandidates = 50,      // 초기 검색 확장 크기
    UseParallelProcessing = true,
    CacheResults = true      // 반복 쿼리 최적화
};
```

### 3. 관계 분석 최적화
```csharp
// 관계 분석 범위 제한
var relationshipOptions = new RelationshipAnalysisOptions
{
    MaxRelationshipsPerChunk = 5,    // 청크당 최대 관계 수
    MinRelationshipStrength = 0.7,  // 최소 관계 강도
    EnabledRelationshipTypes = new[]
    {
        RelationshipType.Sequential,  // 항상 포함
        RelationshipType.Semantic,    // 의미적 관계만
        // RelationshipType.Reference 는 필요시만
    }
};
```

## 🔧 고급 설정

### 1. 커스텀 품질 평가기
```csharp
public class CustomQualityEvaluator : IQualityEvaluator
{
    public async Task<ChunkQuality> EvaluateAsync(DocumentChunk chunk, string? query = null)
    {
        var quality = new ChunkQuality();
        
        // 도메인 특화 품질 평가 로직
        quality.AuthorityScore = EvaluateDomainAuthority(chunk);
        quality.ContentCompleteness = EvaluateCompleteness(chunk);
        
        return quality;
    }
    
    private double EvaluateDomainAuthority(DocumentChunk chunk)
    {
        // 업계 전문 용어 비율, 출처 신뢰도 등
        return /* 도메인별 권위도 계산 */;
    }
}

// 커스텀 평가기 등록
services.AddSingleton<IQualityEvaluator, CustomQualityEvaluator>();
```

### 2. 실시간 학습 시스템
```csharp
public class RealtimeLearningService
{
    public async Task UpdateFromUserBehavior(string query, List<string> selectedChunkIds)
    {
        // 사용자 선택 패턴 학습
        foreach (var chunkId in selectedChunkIds)
        {
            await BoostChunkRelevance(query, chunkId);
        }
    }
    
    private async Task BoostChunkRelevance(string query, string chunkId)
    {
        // 쿼리-청크 관련성 점수 증대
        var chunk = await GetChunkAsync(chunkId);
        chunk.Quality.QueryRelevanceScore += 0.1; // 점진적 학습
        await UpdateChunkAsync(chunk);
    }
}
```

이제 FluxIndex는 최신 RAG 기술을 적용하여 더욱 정확하고 맥락을 이해하는 검색 시스템이 되었습니다! 🎯