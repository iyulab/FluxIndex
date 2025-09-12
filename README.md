# FluxIndex

[![CI/CD Pipeline](https://github.com/iyulab/FluxIndex/actions/workflows/cicd.yml/badge.svg)](https://github.com/iyulab/FluxIndex/actions/workflows/cicd.yml)
[![NuGet](https://img.shields.io/nuget/v/FluxIndex.Core.svg?label=FluxIndex.Core)](https://www.nuget.org/packages/FluxIndex.Core/)
[![NuGet](https://img.shields.io/nuget/v/FluxIndex.SDK.svg?label=FluxIndex.SDK)](https://www.nuget.org/packages/FluxIndex.SDK/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/FluxIndex.Core.svg)](https://www.nuget.org/packages/FluxIndex.Core/)
[![License](https://img.shields.io/github/license/iyulab/FluxIndex)](LICENSE)

**청킹된 데이터를 벡터 스토어에 저장하고 지능형 검색** - 고성능 RAG 인프라

> **재현율@10**: 94% | **MRR**: 0.86 | **완전한 AI Provider 중립성**

## 🎯 FluxIndex 역할

```
📄 Document → [FileFlux: Extract → Parse → Chunk] → 🏗️ FluxIndex
                                                    ↓
                                              📦 Index + 🔍 Search
```

**FluxIndex는 청킹 완료된 데이터부터 처리**:

```csharp
// FileFlux로 문서 처리 및 청킹
var chunks = await fileFlux.ProcessDocumentAsync(document);

// FluxIndex로 인덱싱 및 검색
var client = new FluxIndexClientBuilder()
    .ConfigureVectorStore(VectorStoreType.InMemory)
    .ConfigureEmbeddingService(config => config.UseOpenAI(apiKey))
    .Build();

// 1. 청킹된 데이터를 Indexer로 저장
await client.Indexer.IndexDocumentsAsync(chunks);

// 2. Retriever로 지능형 검색  
var results = await client.Retriever.SearchAsync("냉장고 온도 설정");
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

> 💡 **FileFlux와 완벽 연동** - 문서 처리는 FileFlux, 인덱싱과 검색은 FluxIndex

## 🚀 빠른 시작

### 설치

```bash
# Core 패키지 (필수)
dotnet add package FluxIndex.Core

# SDK 패키지 (편리한 API 제공)
dotnet add package FluxIndex.SDK

# AI Providers (선택적)
dotnet add package FluxIndex.AI.OpenAI

# Storage Providers (선택적)
dotnet add package FluxIndex.Storage.PostgreSQL
dotnet add package FluxIndex.Storage.SQLite

# Cache Providers (선택적)
dotnet add package FluxIndex.Cache.Redis
```

### 기본 사용법

```csharp
using FluxIndex.SDK;
using FluxIndex.Core.Models;

// 1. FluxIndex 클라이언트 생성
var client = new FluxIndexClientBuilder()
    .ConfigureVectorStore(VectorStoreType.InMemory)
    .ConfigureEmbeddingService(config => 
    {
        config.UseOpenAI("your-api-key");
    })
    .ConfigureSearchOptions(options => 
    {
        options.TopK = 10;
        options.MinimumScore = 0.7f;
    })
    .Build();

// 2. 문서 인덱싱
var documents = new[]
{
    new Document
    {
        Id = "doc1",
        Content = "냉장고 온도는 2-4도로 설정하세요.",
        Metadata = new Dictionary<string, object>
        {
            ["category"] = "가전제품",
            ["device"] = "냉장고"
        }
    },
    new Document
    {
        Id = "doc2",
        Content = "야채실 습도는 85-90%가 적절합니다.",
        Metadata = new Dictionary<string, object>
        {
            ["category"] = "가전제품",
            ["device"] = "냉장고"
        }
    }
};

await client.Indexer.IndexDocumentsAsync(documents);

// 3. 검색 수행
var searchResults = await client.Retriever.SearchAsync(
    "냉장고 온도 설정",
    new SearchOptions { TopK = 5 }
);

foreach (var result in searchResults.Documents)
{
    Console.WriteLine($"[{result.Score:F2}] {result.Content}");
    Console.WriteLine($"  Device: {result.Metadata["device"]}");
}
```

### AI Provider 선택

```csharp
// OpenAI 사용
var client = new FluxIndexClientBuilder()
    .ConfigureEmbeddingService(config => config.UseOpenAI(apiKey))
    .Build();

// Azure OpenAI 사용
var client = new FluxIndexClientBuilder()
    .ConfigureEmbeddingService(config => config.UseAzureOpenAI(
        endpoint: "https://your.openai.azure.com/",
        apiKey: azureApiKey,
        deploymentName: "text-embedding-ada-002"
    ))
    .Build();

// 로컬 전용 (AI 없이 키워드 검색만)
var client = new FluxIndexClientBuilder()
    .ConfigureVectorStore(VectorStoreType.InMemory)
    .UseLocalSearchOnly() // BM25, TF-IDF 등 로컬 알고리즘만 사용
    .Build();
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

## 📦 NuGet 패키지

| 패키지 | 버전 | 다운로드 | 설명 |
|--------|------|----------|------|
| [FluxIndex.Core](https://www.nuget.org/packages/FluxIndex.Core/) | ![NuGet](https://img.shields.io/nuget/v/FluxIndex.Core.svg) | ![Downloads](https://img.shields.io/nuget/dt/FluxIndex.Core.svg) | 핵심 RAG 인프라 |
| [FluxIndex.SDK](https://www.nuget.org/packages/FluxIndex.SDK/) | ![NuGet](https://img.shields.io/nuget/v/FluxIndex.SDK.svg) | ![Downloads](https://img.shields.io/nuget/dt/FluxIndex.SDK.svg) | 편리한 API 클라이언트 |
| [FluxIndex.AI.OpenAI](https://www.nuget.org/packages/FluxIndex.AI.OpenAI/) | ![NuGet](https://img.shields.io/nuget/v/FluxIndex.AI.OpenAI.svg) | ![Downloads](https://img.shields.io/nuget/dt/FluxIndex.AI.OpenAI.svg) | OpenAI/Azure 통합 |
| [FluxIndex.Storage.PostgreSQL](https://www.nuget.org/packages/FluxIndex.Storage.PostgreSQL/) | ![NuGet](https://img.shields.io/nuget/v/FluxIndex.Storage.PostgreSQL.svg) | ![Downloads](https://img.shields.io/nuget/dt/FluxIndex.Storage.PostgreSQL.svg) | PostgreSQL + pgvector |
| [FluxIndex.Storage.SQLite](https://www.nuget.org/packages/FluxIndex.Storage.SQLite/) | ![NuGet](https://img.shields.io/nuget/v/FluxIndex.Storage.SQLite.svg) | ![Downloads](https://img.shields.io/nuget/dt/FluxIndex.Storage.SQLite.svg) | SQLite 스토리지 |
| [FluxIndex.Cache.Redis](https://www.nuget.org/packages/FluxIndex.Cache.Redis/) | ![NuGet](https://img.shields.io/nuget/v/FluxIndex.Cache.Redis.svg) | ![Downloads](https://img.shields.io/nuget/dt/FluxIndex.Cache.Redis.svg) | Redis 캐싱 |

## 📖 문서

### 시작하기
- **[빠른 시작 가이드](./docs/getting-started.md)**: 5분 안에 시작하기
- **[설치 가이드](./docs/installation.md)**: 상세 설치 및 설정

### 개발자 가이드
- **[아키텍처 가이드](./docs/architecture.md)**: Clean Architecture 설계
- **[API 레퍼런스](./docs/api-reference.md)**: 전체 API 문서
- **[AI Provider 가이드](./docs/AI-Provider-Guide.md)**: AI 서비스 통합

### 통합 가이드
- **[FileFlux 통합](./docs/FILEFLUX_INTEGRATION_PLAN.md)**: 문서 처리 파이프라인
- **[애플리케이션 통합](./docs/APPLICATION_INTEGRATION_GUIDE.md)**: 실제 앱 통합

### 개발 현황
- **[TASKS.md](./TASKS.md)**: 개발 현황 및 로드맵
- **[개발 리포트](./docs/dev-report.md)**: 구현 상태

## 🤝 기여하기

기여를 환영합니다! [기여 가이드라인](CONTRIBUTING.md)을 참고해주세요.

## 📄 라이선스

이 프로젝트는 [MIT 라이선스](LICENSE)로 배포됩니다.