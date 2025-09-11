# Application Integration Guide

FluxIndex와 FileFlux를 사용자 애플리케이션에서 통합하는 방법을 안내합니다.

## 🎯 핵심 원칙

FluxIndex는 **이미 청킹된 데이터**만 처리합니다:
- ✅ DocumentChunk → 벡터 스토어 인덱싱
- ✅ 임베딩 생성 및 검색 
- ❌ 파일 읽기/쓰기
- ❌ 텍스트 파싱/청킹

## 🔄 통합 파이프라인

```
사용자 애플리케이션
├── 📄 파일 읽기 (File.ReadAllText 등)
├── 📝 FileFlux로 청킹
└── 📦 FluxIndex로 인덱싱

FluxIndex
├── 🏗️ DocumentChunk 받기
├── 🧠 임베딩 생성
└── 🔍 벡터 검색
```

## 💻 구현 예시

### 1. 기본 통합 패턴

```csharp
using FileFlux.Core;
using FluxIndex.SDK;

public class DocumentProcessor
{
    private readonly IFileFluxProcessor _fileFlux;
    private readonly FluxIndexClient _fluxIndex;

    public async Task ProcessDocumentAsync(string filePath)
    {
        // 1. FileFlux로 문서 청킹 (사용자 애플리케이션 책임)
        var fileFluxResult = await _fileFlux.ProcessAsync(filePath);
        
        // 2. FluxIndex용 청크로 변환
        var fluxIndexChunks = fileFluxResult.Chunks.Select(chunk => 
            new DocumentChunk(chunk.Content, chunk.Index)
            {
                TokenCount = chunk.TokenCount,
                // FileFlux 메타데이터를 FluxIndex 메타데이터로 매핑
            });

        // 3. FluxIndex로 인덱싱 (청킹된 데이터만 처리)
        var documentId = await _fluxIndex.IndexChunksAsync(
            fluxIndexChunks, 
            Path.GetFileNameWithoutExtension(filePath),
            new Dictionary<string, object>
            {
                ["source"] = filePath,
                ["processed_at"] = DateTime.UtcNow
            });
    }
}
```

### 2. 배치 처리 패턴

```csharp
public class BatchDocumentProcessor
{
    public async Task ProcessBatchAsync(string[] filePaths)
    {
        var tasks = filePaths.Select(async filePath =>
        {
            // FileFlux 처리 (병렬)
            var fileFluxResult = await _fileFlux.ProcessAsync(filePath);
            
            // FluxIndex 청크로 변환
            var chunks = ConvertToFluxIndexChunks(fileFluxResult.Chunks);
            
            // FluxIndex 인덱싱
            return await _fluxIndex.IndexChunksAsync(chunks, 
                GetDocumentId(filePath), GetMetadata(filePath));
        });

        var results = await Task.WhenAll(tasks);
        Console.WriteLine($"Processed {results.Length} documents");
    }
}
```

### 3. 실시간 처리 패턴

```csharp
public class RealtimeProcessor
{
    public async Task ProcessStreamAsync(Stream contentStream, string documentId)
    {
        // 1. 스트림에서 텍스트 읽기 (사용자 애플리케이션)
        using var reader = new StreamReader(contentStream);
        var content = await reader.ReadToEndAsync();

        // 2. FileFlux로 청킹
        var fileFluxChunks = await _fileFlux.ChunkTextAsync(content);

        // 3. FluxIndex용 청크로 변환 및 인덱싱
        var fluxIndexChunks = ConvertToFluxIndexChunks(fileFluxChunks);
        await _fluxIndex.IndexChunksAsync(fluxIndexChunks, documentId);
    }
}
```

## 🔧 설정 및 DI 구성

### Startup.cs / Program.cs

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // FileFlux 설정 (사용자 애플리케이션 레벨)
    services.AddFileFlux(options =>
    {
        options.ChunkSize = 512;
        options.ChunkOverlap = 64;
        options.Strategy = ChunkingStrategy.Semantic;
    });

    // FluxIndex 설정 (청킹된 데이터 처리 전용)
    var fluxIndexClient = FluxIndexClient.CreateBuilder()
        .ConfigureVectorStore(store => store.UsePostgreSQL(connectionString))
        .ConfigureEmbedding(embed => embed.UseOpenAI(apiKey))
        .ConfigureReranking(rerank => rerank.UseCohere(apiKey))
        .Build();

    services.AddSingleton(fluxIndexClient);
    services.AddScoped<DocumentProcessor>();
}
```

## 📊 성능 최적화 가이드

### 청킹 전략별 검색 성능

| FileFlux 청킹 전략 | FluxIndex 성능 | 추천 사용 사례 |
|------------------|---------------|--------------|
| **Semantic** | 재현율 96% | 복잡한 문서, 높은 정확도 필요 |
| **Sentence** | 재현율 92% | 일반적인 텍스트 문서 |
| **Fixed** | 재현율 88% | 대용량 배치 처리 |
| **Paragraph** | 재현율 90% | 구조화된 문서 |

### 배치 처리 최적화

```csharp
public class OptimizedBatchProcessor
{
    private readonly SemaphoreSlim _semaphore = new(Environment.ProcessorCount);

    public async Task ProcessLargeDatasetAsync(string[] filePaths)
    {
        const int batchSize = 100;
        
        for (int i = 0; i < filePaths.Length; i += batchSize)
        {
            var batch = filePaths.Skip(i).Take(batchSize);
            var tasks = batch.Select(ProcessSingleFileAsync);
            await Task.WhenAll(tasks);
            
            // 메모리 정리
            GC.Collect();
        }
    }

    private async Task ProcessSingleFileAsync(string filePath)
    {
        await _semaphore.WaitAsync();
        try
        {
            // FileFlux + FluxIndex 처리
            await ProcessDocumentAsync(filePath);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
```

## ⚠️ 주의사항

### 하지 말 것
- ❌ FluxIndex 내부에서 File.ReadAllText 사용
- ❌ FluxIndex에서 FileFlux 직접 참조
- ❌ 파일 경로를 FluxIndex에 전달
- ❌ FluxIndex가 청킹 로직 포함

### 해야 할 것  
- ✅ 사용자 애플리케이션에서 FileFlux 사용
- ✅ 청킹 완료 후 FluxIndex.IndexChunksAsync() 호출
- ✅ DocumentChunk 단위로 데이터 전달
- ✅ 메타데이터는 애플리케이션 레벨에서 관리

## 🚀 고급 사용 패턴

### 1. 멀티테넌트 환경

```csharp
public class MultiTenantProcessor
{
    public async Task ProcessForTenantAsync(string tenantId, string[] documents)
    {
        foreach (var doc in documents)
        {
            var chunks = await ProcessWithFileFlux(doc);
            await _fluxIndex.IndexChunksAsync(chunks, 
                documentId: $"{tenantId}_{Path.GetFileName(doc)}",
                metadata: new Dictionary<string, object> { ["tenant_id"] = tenantId });
        }
    }

    public async Task<SearchResults> SearchForTenantAsync(string tenantId, string query)
    {
        return await _fluxIndex.SearchAsync(query, 
            filter: new Dictionary<string, object> { ["tenant_id"] = tenantId });
    }
}
```

### 2. 점진적 업데이트

```csharp
public class IncrementalProcessor
{
    public async Task UpdateDocumentAsync(string documentId, string newContent)
    {
        // 1. 기존 문서 삭제
        await _fluxIndex.DeleteDocumentAsync(documentId);

        // 2. 새 콘텐츠 처리
        var newChunks = await ProcessWithFileFlux(newContent);
        
        // 3. 다시 인덱싱
        await _fluxIndex.IndexChunksAsync(newChunks, documentId);
    }
}
```

이제 FluxIndex는 명확한 역할 분리로 더 나은 아키텍처를 갖게 되었습니다. 🎯