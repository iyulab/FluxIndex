# FluxIndex.Extensions.FileFlux 통합 계획

## 🎯 통합 목표

FileFlux와 FluxIndex를 심리스하게 연결하여 완벽한 End-to-End RAG 파이프라인 구축

### 핵심 원칙
- **역할 분리**: FileFlux(문서→청크) + FluxIndex(저장/검색 품질)
- **인터페이스 기반**: AI Provider 중립성 유지
- **메타데이터 보존**: FileFlux 청킹 품질 정보 활용
- **성능 최적화**: 스트리밍 및 병렬 처리 통합

## 📐 아키텍처 설계

### 1. 데이터 흐름
```
📄 File 
  ↓ FileFlux
📖 Read → 📝 Parse → 🔪 Chunks (with metadata)
  ↓ FluxIndex.Extensions.FileFlux (Mapper)
📦 Store (Source + Chunks + Enriched Metadata)
  ↓ FluxIndex
🔍 Search (Vector + Keyword + Reranking)
```

### 2. 인터페이스 매핑

#### FileFlux 인터페이스
```csharp
// FileFlux가 제공하는 청크
public interface IDocumentChunk
{
    string Content { get; }
    int ChunkIndex { get; }
    int StartPosition { get; }
    int EndPosition { get; }
    Dictionary<string, object> Properties { get; } // 품질점수, 전략 등
}
```

#### FluxIndex DocumentChunk 매핑
```csharp
// FluxIndex.Extensions.FileFlux/Mappers/ChunkMapper.cs
public class FileFluxChunkMapper : IFileFluxChunkMapper
{
    public DocumentChunk MapToFluxIndexChunk(IDocumentChunk fileFluxChunk)
    {
        var fluxIndexChunk = new DocumentChunk(
            content: fileFluxChunk.Content,
            index: fileFluxChunk.ChunkIndex
        );

        // FileFlux 메타데이터 보존
        var metadata = new ChunkMetadata
        {
            TokenCount = EstimateTokens(fileFluxChunk.Content),
            CharacterCount = fileFluxChunk.Content.Length,
            ImportanceScore = ExtractQualityScore(fileFluxChunk.Properties)
        };

        // FileFlux 청킹 전략 정보 활용
        if (fileFluxChunk.Properties.TryGetValue("ChunkingStrategy", out var strategy))
        {
            metadata.Properties["fileflux_strategy"] = strategy;
        }

        if (fileFluxChunk.Properties.TryGetValue("QualityScore", out var quality))
        {
            metadata.Properties["fileflux_quality"] = quality;
        }

        fluxIndexChunk.SetMetadata(metadata);
        return fluxIndexChunk;
    }
}
```

## 🔧 구현 세부사항

### Phase 6-1: 기본 통합 (1주차)

#### 1. ChunkMapper 구현
```csharp
// FluxIndex.Extensions.FileFlux/FileFluxIntegration.cs
public class FileFluxIntegration : IFileFluxIntegration
{
    private readonly IDocumentProcessor _fileFlux;
    private readonly IIndexer _fluxIndex;
    private readonly IFileFluxChunkMapper _mapper;

    public async Task<string> ProcessAndIndexAsync(
        string filePath,
        ChunkingOptions? options = null)
    {
        // 1. FileFlux로 문서 처리
        var chunks = await _fileFlux.ProcessAsync(filePath, options);
        
        // 2. FluxIndex 청크로 변환
        var fluxIndexChunks = chunks
            .Select(chunk => _mapper.MapToFluxIndexChunk(chunk))
            .ToList();
        
        // 3. 메타데이터 풍부화
        await EnrichChunksAsync(fluxIndexChunks);
        
        // 4. FluxIndex로 인덱싱
        var documentId = await _fluxIndex.IndexChunksAsync(
            fluxIndexChunks,
            Path.GetFileNameWithoutExtension(filePath));
            
        return documentId;
    }
}
```

#### 2. 스트리밍 통합
```csharp
public async IAsyncEnumerable<IndexingProgress> ProcessWithProgressAsync(
    string filePath,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    await foreach (var result in _fileFlux.ProcessWithProgressAsync(filePath).WithCancellation(ct))
    {
        if (result.IsSuccess && result.Result != null)
        {
            var mappedChunks = result.Result
                .Select(chunk => _mapper.MapToFluxIndexChunk(chunk))
                .ToList();
                
            var documentId = await _fluxIndex.IndexChunksAsync(mappedChunks);
            
            yield return new IndexingProgress
            {
                DocumentId = documentId,
                ChunksProcessed = mappedChunks.Count,
                Status = IndexingStatus.Success
            };
        }
    }
}
```

### Phase 6-2: 품질 최적화 (2주차)

#### 1. 청킹 전략별 검색 전략 매핑
```csharp
public class StrategyMapper
{
    public RerankingStrategy GetOptimalRerankingStrategy(string fileFluxStrategy)
    {
        return fileFluxStrategy switch
        {
            "Smart" => RerankingStrategy.Quality,
            "Intelligent" => RerankingStrategy.LLM,
            "MemoryOptimizedIntelligent" => RerankingStrategy.Contextual,
            "Semantic" => RerankingStrategy.Semantic,
            "Auto" => RerankingStrategy.Adaptive,
            _ => RerankingStrategy.Hybrid
        };
    }
    
    public SearchOptions OptimizeSearchOptions(ChunkingMetadata metadata)
    {
        return new SearchOptions
        {
            ExpandContext = metadata.OverlapSize > 0,
            UseQualityBoost = metadata.QualityScore > 0.8,
            MinScore = metadata.QualityScore > 0.7 ? 0.6f : 0.5f
        };
    }
}
```

#### 2. 멀티모달 콘텐츠 통합
```csharp
public class MultimodalIntegration
{
    public async Task<DocumentChunk> ProcessMultimodalChunk(
        IDocumentChunk fileFluxChunk,
        IImageToTextService? visionService = null)
    {
        var chunk = _mapper.MapToFluxIndexChunk(fileFluxChunk);
        
        // FileFlux가 이미지를 텍스트로 변환한 경우
        if (fileFluxChunk.Properties.ContainsKey("ImageContent"))
        {
            chunk.Metadata.ContentType = "multimodal";
            chunk.Metadata.Properties["has_visual_content"] = true;
        }
        
        return chunk;
    }
}
```

### Phase 6-3: 고급 기능 (3주차)

#### 1. 품질 기반 재순위화 가중치
```csharp
public class QualityAwareReranking
{
    public async Task<List<EnhancedSearchResult>> RerankWithFileFluxQuality(
        string query,
        List<SearchResult> results)
    {
        foreach (var result in results)
        {
            // FileFlux 품질 점수 활용
            if (result.Chunk.Metadata.Properties.TryGetValue("fileflux_quality", out var quality))
            {
                var qualityScore = Convert.ToDouble(quality);
                result.RerankedScore *= (1.0 + qualityScore * 0.2); // 최대 20% 부스트
            }
            
            // FileFlux 청킹 전략 기반 조정
            if (result.Chunk.Metadata.Properties.TryGetValue("fileflux_strategy", out var strategy))
            {
                result.RerankedScore *= GetStrategyWeight(strategy.ToString());
            }
        }
        
        return results.OrderByDescending(r => r.RerankedScore).ToList();
    }
}
```

#### 2. 배치 처리 최적화
```csharp
public class BatchProcessingOptimization
{
    public async Task ProcessBatchAsync(string[] filePaths)
    {
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };
        
        await Parallel.ForEachAsync(filePaths, options, async (path, ct) =>
        {
            // FileFlux 병렬 처리 활용
            var chunks = await _fileFlux.ProcessAsync(path);
            var mapped = MapChunks(chunks);
            
            // FluxIndex 배치 인덱싱
            await _fluxIndex.IndexChunksAsync(mapped);
        });
    }
}
```

## 📦 패키지 구조

```
FluxIndex.Extensions.FileFlux/
├── FileFluxIntegration.cs           # 메인 통합 클래스
├── Mappers/
│   ├── ChunkMapper.cs               # 청크 변환
│   ├── MetadataMapper.cs            # 메타데이터 매핑
│   └── StrategyMapper.cs            # 전략 매핑
├── Optimizers/
│   ├── QualityOptimizer.cs          # 품질 기반 최적화
│   ├── PerformanceOptimizer.cs      # 성능 최적화
│   └── MultimodalOptimizer.cs       # 멀티모달 최적화
├── Extensions/
│   └── ServiceCollectionExtensions.cs # DI 확장
└── Configuration/
    └── FileFluxIntegrationOptions.cs # 설정 옵션
```

## 🚀 사용 예제

### 기본 사용법
```csharp
// DI 설정
services.AddFileFlux();
services.AddFluxIndex(builder => builder
    .ConfigureVectorStore(store => store.UsePostgreSQL(connectionString))
    .ConfigureEmbedding(embed => embed.UseOpenAI(apiKey)));
    
// FileFlux 통합 추가
services.AddFluxIndexFileFluxIntegration();

// 사용
var integration = provider.GetRequiredService<IFileFluxIntegration>();
var documentId = await integration.ProcessAndIndexAsync("document.pdf");
```

### 고급 사용법
```csharp
// 스트리밍 처리
await foreach (var progress in integration.ProcessWithProgressAsync("large.pdf"))
{
    Console.WriteLine($"Indexed {progress.ChunksProcessed} chunks");
}

// 품질 최적화 검색
var results = await integration.SearchWithQualityBoostAsync(
    query: "검색어",
    useFileFluxQuality: true);
```

## 📊 예상 성과

| 메트릭 | 현재 | Phase 6 완료 후 | 개선율 |
|--------|------|----------------|--------|
| **End-to-End 시간** | 수동 통합 | 자동화 | 80% 단축 |
| **검색 정확도** | 97% | 99%+ | +2%+ |
| **메타데이터 활용** | 기본 | 풍부화 | 300% 증가 |
| **품질 점수 반영** | 없음 | 자동 | 신규 |
| **청킹 전략 최적화** | 수동 | 자동 매핑 | 100% 자동화 |

## 🗓️ 일정

- **1주차**: 기본 매퍼 구현, 스트리밍 통합
- **2주차**: 품질 최적화, 전략 매핑
- **3주차**: 고급 기능, 배치 처리
- **4주차**: 테스트, 문서화, 패키지 배포

이 통합으로 FileFlux와 FluxIndex가 완벽하게 연동되어 최고 품질의 RAG 파이프라인이 완성됩니다! 🚀