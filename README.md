# FluxIndex

[![CI/CD Pipeline](https://github.com/iyulab/FluxIndex/actions/workflows/release.yml/badge.svg)](https://github.com/iyulab/FluxIndex/actions/workflows/release.yml)
[![NuGet](https://img.shields.io/nuget/v/FluxIndex.svg?label=FluxIndex)](https://www.nuget.org/packages/FluxIndex/)
[![NuGet](https://img.shields.io/nuget/v/FluxIndex.SDK.svg?label=FluxIndex.SDK)](https://www.nuget.org/packages/FluxIndex.SDK/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/FluxIndex.svg)](https://www.nuget.org/packages/FluxIndex/)
[![License](https://img.shields.io/github/license/iyulab/FluxIndex)](LICENSE)

**모듈형 RAG 인프라** - Clean Architecture, AI Provider 중립적, 최소 의존성

> **v0.1.4**: 의존성 최적화 완료 | FluxIndex + Extensions 분리 아키텍처

## 🎯 FluxIndex 아키텍처

```
📄 Documents → 🔪 Chunk → 🧠 Embed → 📦 Store → 🔍 Search
                ↓           ↓         ↓         ↓
           지능형 청킹    AI Provider   Vector DB  하이브리드 검색
```

**Clean Architecture 기반 모듈형 RAG 시스템**:

```csharp
// 1. 핵심 FluxIndex 사용 (FileFlux 없이)
var client = new FluxIndexClientBuilder()
    .UseOpenAI(apiKey, "text-embedding-ada-002") // AI Provider 선택
    .UseSQLiteInMemory() // 경량 벡터 저장소
    .UseMemoryCache() // 임베딩 캐싱
    .Build();

// 2. 문서 인덱싱 (최소 의존성)
var document = Document.Create("doc1");
document.AddChunk(new DocumentChunk("텍스트 내용", 0));
await client.Indexer.IndexDocumentAsync(document);

// 3. 빠른 검색
var results = await client.Retriever.SearchAsync("검색어");

// 4. FileFlux가 필요한 경우만 Extension 사용
// dotnet add package FluxIndex.Extensions.FileFlux
```

## ✨ 핵심 특징

- **🏗️ Clean Architecture**: 의존성 역전, 단일 책임 원칙 준수
- **📦 최소 의존성**: FluxIndex 코어는 FileFlux와 완전 분리
- **🔧 AI Provider 중립적**: OpenAI, Azure, 커스텀 서비스 지원
- **⚡ 성능 최적화**: 임베딩 캐싱, 배치 처리, 메모리 효율성
- **🧩 모듈형 설계**: 필요한 기능만 선택적 설치 가능
- **📊 벡터 검색**: 코사인 유사도, 하이브리드 검색 지원

## 🏗️ 아키텍처 설계

### 🎯 의존성 분리 (v0.1.4)
```
FluxIndex (Core)                    # 핵심 RAG 인프라
├── Microsoft.Extensions.*          # DI, Configuration, Logging
└── Microsoft.ML.OnnxRuntime       # 로컬 임베딩 지원

FluxIndex.SDK                       # 편리한 통합 API
├── FluxIndex 참조                  # 코어 기능
└── 최소 Microsoft.Extensions.*     # 필수 확장만

FluxIndex.Extensions.FileFlux       # 고급 문서 처리
├── FluxIndex 참조                  # 코어 기능 사용
└── FileFlux                        # 문서 파싱 (유일한 FileFlux 의존성)
```

### ✅ 현재 구현 상태
- **✅ Core RAG**: 벡터 저장, 검색, 임베딩 인터페이스
- **✅ AI Providers**: OpenAI, Azure OpenAI, 로컬 모델 지원
- **✅ Storage**: SQLite, PostgreSQL + pgvector 지원
- **✅ Caching**: 메모리, Redis 캐싱 구현
- **✅ Extensions**: FileFlux 통합 (선택적)

## 🚀 빠른 시작

### 📦 모듈형 설치

```bash
# 1. 핵심 패키지 (필수)
dotnet add package FluxIndex        # 코어 RAG 인프라 (FileFlux 없음)
dotnet add package FluxIndex.SDK    # 편리한 통합 API

# 2. AI Provider (선택 - 하나 필요)
dotnet add package FluxIndex.AI.OpenAI    # OpenAI + Azure OpenAI

# 3. 저장소 (선택 - 하나 필요)
dotnet add package FluxIndex.Storage.SQLite      # 가벼운 개발용
dotnet add package FluxIndex.Storage.PostgreSQL  # 프로덕션용

# 4. 캐싱 (선택)
dotnet add package FluxIndex.Cache.Redis         # 분산 캐싱

# 5. 문서 처리 Extension (선택 - 필요시만)
dotnet add package FluxIndex.Extensions.FileFlux # 고급 문서 파싱
```

### ⚡ 최소 설치 (가장 가벼운 구성)
```bash
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.AI.OpenAI
# → FileFlux 의존성 없음, 최소 패키지 크기
```

### 💡 최소 의존성 사용법

```csharp
using FluxIndex.SDK;

// 1. 가벼운 FluxIndex 클라이언트 (FileFlux 없음)
var client = new FluxIndexClientBuilder()
    .UseOpenAI("your-api-key", "text-embedding-3-small")
    .UseSQLiteInMemory()
    .UseMemoryCache()
    .Build();

// 2. 텍스트 직접 인덱싱 (FileFlux 불필요)
var document = Document.Create("doc1");
document.AddChunk(new DocumentChunk("FluxIndex는 모듈형 RAG 시스템입니다.", 0));
document.AddChunk(new DocumentChunk("Clean Architecture를 따릅니다.", 1));

await client.Indexer.IndexDocumentAsync(document);

// 3. 빠른 검색
var results = await client.Retriever.SearchAsync("RAG 시스템");

foreach (var result in results)
{
    Console.WriteLine($"점수: {result.Score:F3} | {result.Chunk.Content}");
}
```

### 🚀 FileFlux Extension 활용법

```csharp
// FluxIndex.Extensions.FileFlux 패키지 필요
using FluxIndex.Extensions.FileFlux;

var client = new FluxIndexClientBuilder()
    .UseOpenAI("your-api-key")
    .UseSQLiteInMemory()
    .Build();

// FileFlux로 고급 문서 처리
await client.Indexer.ProcessDocumentAsync("document.pdf"); // PDF, DOCX, etc.
```

### AI Provider 선택

```csharp
// OpenAI 사용
var client = new FluxIndexClientBuilder()
    .UseOpenAI(apiKey, "text-embedding-ada-002")
    .UseSQLiteInMemory()
    .Build();

// Azure OpenAI 사용
var client = new FluxIndexClientBuilder()
    .UseAzureOpenAI("https://your.openai.azure.com/", azureApiKey, "text-embedding-ada-002")
    .UseSQLiteInMemory()
    .Build();

// PostgreSQL 벡터 스토어 사용
var client = new FluxIndexClientBuilder()
    .UseOpenAI(apiKey)
    .UsePostgreSQL("Host=localhost;Database=fluxindex;Username=user;Password=pass")
    .UseRedisCache("localhost:6379")
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

## 📊 검증된 성능

### 🏆 실제 테스트 결과 (Phase 6.5 완료)
- **검색 정확도**: 100% (모든 테스트 질문이 올바른 문서 매칭)
- **평균 코사인 유사도**: 0.638 (우수한 의미적 관련성)
- **평균 응답시간**: 473ms (실시간 애플리케이션 적용 가능)
- **임베딩 성공률**: 100% (11개 청크 모두 성공)
- **시스템 안정성**: 오류 없는 안정적 동작

### 💡 최적화 기능
- **지능형 청킹**: 문장 경계 기반으로 맥락 완성도 향상
- **임베딩 캐싱**: API 비용 절감 + 응답속도 향상
- **배치 처리**: 5개 단위 배치로 처리량 최적화
- **SQLite 통합**: 가벼운 벡터 저장소로 개발 친화적

## 📦 NuGet 패키지

| 패키지 | 버전 | 다운로드 | 설명 |
|--------|------|----------|------|
| [FluxIndex](https://www.nuget.org/packages/FluxIndex/) | ![NuGet](https://img.shields.io/nuget/v/FluxIndex.svg) | ![Downloads](https://img.shields.io/nuget/dt/FluxIndex.svg) | 핵심 RAG 인프라 (통합됨) |
| [FluxIndex.SDK](https://www.nuget.org/packages/FluxIndex.SDK/) | ![NuGet](https://img.shields.io/nuget/v/FluxIndex.SDK.svg) | ![Downloads](https://img.shields.io/nuget/dt/FluxIndex.SDK.svg) | 편리한 API 클라이언트 |
| [FluxIndex.AI.OpenAI](https://www.nuget.org/packages/FluxIndex.AI.OpenAI/) | ![NuGet](https://img.shields.io/nuget/v/FluxIndex.AI.OpenAI.svg) | ![Downloads](https://img.shields.io/nuget/dt/FluxIndex.AI.OpenAI.svg) | OpenAI/Azure 통합 |
| [FluxIndex.Storage.PostgreSQL](https://www.nuget.org/packages/FluxIndex.Storage.PostgreSQL/) | ![NuGet](https://img.shields.io/nuget/v/FluxIndex.Storage.PostgreSQL.svg) | ![Downloads](https://img.shields.io/nuget/dt/FluxIndex.Storage.PostgreSQL.svg) | PostgreSQL + pgvector |
| [FluxIndex.Storage.SQLite](https://www.nuget.org/packages/FluxIndex.Storage.SQLite/) | ![NuGet](https://img.shields.io/nuget/v/FluxIndex.Storage.SQLite.svg) | ![Downloads](https://img.shields.io/nuget/dt/FluxIndex.Storage.SQLite.svg) | SQLite 스토리지 |
| [FluxIndex.Cache.Redis](https://www.nuget.org/packages/FluxIndex.Cache.Redis/) | ![NuGet](https://img.shields.io/nuget/v/FluxIndex.Cache.Redis.svg) | ![Downloads](https://img.shields.io/nuget/dt/FluxIndex.Cache.Redis.svg) | Redis 캐싱 |
| [FluxIndex.Extensions.FileFlux](https://www.nuget.org/packages/FluxIndex.Extensions.FileFlux/) | ![NuGet](https://img.shields.io/nuget/v/FluxIndex.Extensions.FileFlux.svg) | ![Downloads](https://img.shields.io/nuget/dt/FluxIndex.Extensions.FileFlux.svg) | FileFlux 통합 |

## 📖 문서

### 핵심 가이드
- **[빠른 시작 가이드](./docs/getting-started.md)**: 실제 동작하는 예제로 5분 시작
- **[아키텍처 가이드](./docs/architecture.md)**: Clean Architecture 설계 및 실제 구현

### 실제 구현 상태
- **[TASKS.md](./TASKS.md)**: 완료된 Phase와 검증된 성능 메트릭
- **[samples/RealQualityTest](./samples/RealQualityTest/)**: 실제 OpenAI API로 검증된 품질 테스트

### 현재 사용 가능한 기능
- ✅ **지능형 청킹**: 문장 경계 기반 청킹 (검증됨)
- ✅ **임베딩 캐싱**: 해시 기반 중복 방지 (구현됨)
- ✅ **배치 처리**: 5개 단위 배치 최적화 (구현됨)
- ✅ **SQLite 저장소**: Entity Framework Core 통합 (동작함)
- ✅ **OpenAI 통합**: text-embedding-3-small 모델 (검증됨)

## 🤝 기여하기

기여를 환영합니다! GitHub Issues를 통해 버그 리포트나 기능 제안을 해주세요.

## 📄 라이선스

이 프로젝트는 [MIT 라이선스](LICENSE)로 배포됩니다.