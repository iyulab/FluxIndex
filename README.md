# FluxIndex

**청킹된 데이터를 벡터 스토어에 저장하고 지능형 검색** - 고성능 RAG 인프라

> **재현율@10**: 94% | **MRR**: 0.86 | **완전한 AI Provider 중립성**

## 🎯 FluxIndex 역할

```
📄 Document → 📤 Extract → 📝 Parse → 🔪 Chunk → 🏗️ FluxIndex
                                              ↓
                                        📦 Index + 🔍 Search
```

**FluxIndex는 청킹 완료된 데이터부터 처리**:

```csharp
// 1. 청킹된 데이터를 Indexer로 저장
await indexer.IndexAsync(chunks, metadata);

// 2. Retriever로 지능형 검색  
var results = await retriever.SearchAsync("냉장고 온도 설정");
```

## ✨ 주요 특징

- **📦 Chunk → Vector Store**: 청킹된 데이터를 벡터 스토어에 최적화 저장
- **🔍 지능형 Retriever**: 쿼리에 맞는 최적 검색 전략 자동 선택  
- **⚡ 고성능**: 재현율 94%, 적응형 검색으로 품질 자동 최적화
- **🔧 AI 중립성**: OpenAI, Azure, 커스텀 서비스 자유 선택
- **🏗️ 확장성**: Clean Architecture, 수백만 벡터까지 확장

## ⚡ FluxIndex 책임 범위

### ✅ FluxIndex가 하는 일
- **청킹된 텍스트 → 임베딩 → 벡터 스토어 저장**
- **쿼리 복잡도 분석 → 최적 검색 전략 선택**  
- **벡터 검색 + 키워드 검색 + 재순위화**
- **Self-RAG 품질 평가 및 검색 결과 개선**

### ❌ FluxIndex가 하지 않는 일
- **파일 추출** (PDF, DOCX → 텍스트)
- **문서 파싱** (구조 분석, 메타데이터 추출)  
- **텍스트 청킹** (문단, 의미, 고정 크기 분할)

> 💡 **FileFlux 등의 청킹 라이브러리와 완벽 연동** - Phase 6에서 구현 예정

## 🚀 빠른 시작

### 설치

```bash
dotnet add package FluxIndex.Core
dotnet add package FluxIndex.AI.OpenAI        # 선택적
dotnet add package FluxIndex.Storage.PostgreSQL  # 선택적
```

### 기본 사용법

```csharp
// 1. 서비스 설정
services.AddFluxIndexCore();
services.AddFluxIndexOpenAI(configuration);

var serviceProvider = services.BuildServiceProvider();

// 2. 청킹된 데이터를 Indexer로 저장
var indexer = serviceProvider.GetRequiredService<IIndexer>();

var chunks = new[]
{
    new DocumentChunk("냉장고 온도는 2-4도로 설정하세요.", metadata),
    new DocumentChunk("야채실 습도는 85-90%가 적절합니다.", metadata)
};

await indexer.IndexChunksAsync(chunks);

// 3. Retriever로 검색
var retriever = serviceProvider.GetRequiredService<IRetriever>();
var results = await retriever.SearchAsync("냉장고 온도 설정");

foreach (var doc in results)
{
    Console.WriteLine($"[{doc.Score:F2}] {doc.Content}");
}
```

### AI Provider 선택

```csharp
// OpenAI 사용
services.AddFluxIndexOpenAI(config);

// 로컬 전용 (AI 없이도 대부분 기능 사용)
services.AddFluxIndexCore(); // BM25, LocalReranker 등
```

## 🎯 똑똑한 Retriever

FluxIndex Retriever는 쿼리에 따라 **자동으로 최적 전략 선택**:

- **간단한 키워드** → BM25 키워드 검색
- **자연어 질문** → 벡터 + 키워드 하이브리드  
- **복잡한 질의** → Self-RAG 품질 개선
- **전문 용어** → 2단계 재순위화
- **비교 질문** → 다중 쿼리 분해

> 🤖 **사용자는 그냥 검색하면 됩니다** - FluxIndex가 알아서 최적화

## 📊 성능

- **재현율@10**: 94% (업계 최고 수준)
- **MRR**: 0.86 (22% 향상)  
- **Self-RAG**: 평균 18% 품질 개선
- **응답 시간**: 245ms (품질 평가 포함)
- **확장성**: 수백만 벡터 선형 확장

## 📖 문서

- **[TASKS.md](./TASKS.md)**: 개발 현황 및 로드맵
- **[docs/](./docs/)**: 상세 가이드 및 설계 문서

