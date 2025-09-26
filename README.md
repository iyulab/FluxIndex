# FluxIndex

[![CI/CD](https://github.com/iyulab/FluxIndex/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/iyulab/FluxIndex/actions/workflows/build-and-release.yml)
[![NuGet](https://img.shields.io/nuget/v/FluxIndex.SDK.svg?label=FluxIndex.SDK)](https://www.nuget.org/packages/FluxIndex.SDK/)
[![License](https://img.shields.io/github/license/iyulab/FluxIndex)](LICENSE)

RAG(Retrieval-Augmented Generation) 시스템 구축을 위한 .NET 라이브러리

## 🎯 개요

FluxIndex는 문서 인덱싱과 검색에 특화된 RAG 라이브러리입니다. 복잡한 인프라 구성 없이 벡터 검색과 키워드 검색을 결합한 하이브리드 검색을 제공합니다.

```
📄 문서 → 🔪 청킹 → 📦 인덱싱 → 🔍 검색 → 📊 평가
```

### 📦 Store 기능 (지능형 저장)
- **유연한 입력**: 단일문서, 청킹된 데이터, 그래프 구조 모두 지원
- **선택적 증강**: AI 기반 메타데이터 추출 (카테고리, 요약, 키워드)
- **계층적 청킹**: Small-to-Big 4단계 계층 자동 구성
- **관계 분석**: 청크 간 의미적/계층적 관계 자동 구축
- **네이티브 벡터**: sqlite-vec, pgvector 등 고성능 벡터 저장소 지원

### 🔍 Search 기능 (전략적 검색)
- **다중 전략**: 벡터(HNSW) + 하이브리드(BM25) + 그래프 검색
- **적응형 검색**: 쿼리 복잡도에 따른 최적 전략 자동 선택
- **재순위화**: RRF, Cross-encoder, LLM-as-Judge 다단계 정제
- **성능 최적화**: 95% 유사도 시맨틱 캐싱, HNSW 자동 튜닝
- **네이티브 검색**: sqlite-vec의 vec0 테이블을 통한 고성능 벡터 검색

### 🔧 자동 최적화
- **지속적 학습**: 쿼리 패턴 기반 성능 자동 향상
- **실시간 모니터링**: 9가지 품질 메트릭 자동 추적
- **AI Provider 중립성**: OpenAI, Azure, 커스텀 서비스 자유 선택

### 🎯 FluxIndex 책임 범위
- ✅ **Store**: 다양한 입력 수용 및 지능형 증강
- ✅ **Search**: 전략적 검색 및 재순위화
- ✅ **자동 최적화**: 성능 튜닝 및 품질 관리
- ✅ **확장성**: AI Provider 중립 및 전략 플러그인

### 🚫 다른 라이브러리 책임
- ❌ **파일 처리**: PDF/DOC 파싱 (FileFlux 담당, FluxIndex.Extensions.FileFlux로 통합 지원)
- ❌ **웹 크롤링**: URL 추출 (WebFlux 담당, FluxIndex.Extensions.WebFlux로 통합 지원)
- ❌ **웹 서버**: API 구현 (소비앱 담당)
- ❌ **인증 시스템**: 사용자 관리 (소비앱 담당)
- ❌ **AI 프로바이더**: 소비앱 담당, FluxIndex.AI.* 주요 공급자 통합 제공

---

## 📋 설치

### 핵심 패키지

| 패키지명 | 필수 여부 | 설명 |
|---------|----------|------|
| `FluxIndex.SDK` | 필수 | 통합 API 클라이언트 |
| `FluxIndex.Storage.*` | 택1 필수 | 벡터 저장소 (SQLite/PostgreSQL 중 선택) |

### AI 프로바이더 (선택사항)

| 패키지명 | 설명 |
|---------|------|
| `FluxIndex.AI.OpenAI` | OpenAI/Azure OpenAI 연동 편의 제공 |

> 💡 **AI 프로바이더**: 직접 `IEmbeddingService`, `ITextCompletionService` 구현 가능

### 저장소 선택 (택1 필수)

| 패키지명 | 설명 |
|---------|------|
| `FluxIndex.Storage.SQLite` | SQLite 벡터 저장소 (개발용, sqlite-vec 지원) |
| `FluxIndex.Storage.PostgreSQL` | PostgreSQL+pgvector (프로덕션용) |

> 💡 **sqlite-vec**: SQLite 저장소는 네이티브 벡터 검색을 위한 sqlite-vec 확장을 지원합니다.

### 캐시 시스템 (선택사항)

| 패키지명 | 설명 |
|---------|------|
| `FluxIndex.Cache.Redis` | Redis 기반 캐시 (기본: 메모리 캐시) |

### 확장 기능 (선택사항)

| 패키지명 | 설명 |
|---------|------|
| `FluxIndex.Extensions.FileFlux` | PDF/DOC/TXT 파일 처리 및 청킹 |
| `FluxIndex.Extensions.WebFlux` | 웹페이지 크롤링 및 콘텐츠 추출 |

### 설치 예제

#### 최소 구성 (로컬 개발)
```bash
# 필수: SDK + 저장소
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.Storage.SQLite
```

#### 커스텀 AI 구성
```bash
# 필수 패키지
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.Storage.PostgreSQL

# 커스텀 AI 서비스 구현
services.AddScoped<IEmbeddingService, MyCustomEmbeddingService>();
services.AddScoped<ITextCompletionService, MyCustomLLMService>();
```

#### 편의 패키지 활용
```bash
# OpenAI 편의 패키지 사용
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.AI.OpenAI
dotnet add package FluxIndex.Storage.PostgreSQL
dotnet add package FluxIndex.Cache.Redis
```

#### 풀 기능 구성
```bash
# 모든 확장 기능 포함
dotnet add package FluxIndex.SDK
dotnet add package FluxIndex.AI.OpenAI
dotnet add package FluxIndex.Storage.PostgreSQL
dotnet add package FluxIndex.Cache.Redis
dotnet add package FluxIndex.Extensions.FileFlux
dotnet add package FluxIndex.Extensions.WebFlux
```

---

## 💡 시작하기

```csharp
using FluxIndex.SDK;
using FluxIndex.Extensions.FileFlux;
using Microsoft.Extensions.DependencyInjection;

// 기본 설정
var services = new ServiceCollection();
services.AddFluxIndex()
    .AddSQLiteVectorStore()              // 저장소
    .UseOpenAIEmbedding(apiKey: "...");  // AI (선택적)

// FileFlux 확장 (파일 처리)
services.AddFileFlux(options =>
{
    options.DefaultChunkingStrategy = "Auto";
    options.DefaultMaxChunkSize = 512;
});

var serviceProvider = services.BuildServiceProvider();
var client = serviceProvider.GetRequiredService<FluxIndexClient>();
var fileFlux = serviceProvider.GetRequiredService<FileFluxIntegration>();

// 파일 인덱싱 (FileFlux 확장 사용)
var documentId = await fileFlux.ProcessAndIndexAsync("document.pdf");

// 텍스트 인덱싱 (기본 방식)
await client.Indexer.IndexDocumentAsync(
    "FluxIndex는 .NET RAG 라이브러리입니다.", "doc-001");

// 검색
var results = await client.Retriever.SearchAsync("RAG 라이브러리");
foreach (var result in results)
{
    Console.WriteLine($"{result.Score:F2}: {result.Content}");
}
```

> **📖 상세 가이드**: [튜토리얼](./docs/tutorial.md) | [치트시트](./docs/cheat-sheet.md) | [샘플 코드](./samples/)

---

## 🔍 검색 시스템

FluxIndex는 고도화된 검색 전략을 제공합니다:

- **키워드 검색**: BM25 알고리즘 기반 정확한 용어 매칭
- **벡터 검색**: HNSW 인덱스 기반 의미 유사도 검색
- **하이브리드 검색**: RRF(Reciprocal Rank Fusion) 기반 결과 융합
- **Small-to-Big**: 4단계 계층적 컨텍스트 확장
- **재순위화**: Local/Cross-encoder 기반 결과 개선
- **쿼리 변환**: HyDE, QuOTE 등 고급 검색 기법
- **시맨틱 캐싱**: 중복 검색 요청 최적화

---

## 🏗️ 아키텍처

### 프로젝트 구조

```
FluxIndex.Core          # 핵심 도메인 및 애플리케이션 로직
FluxIndex.SDK           # 통합 API 클라이언트
FluxIndex.AI.*          # AI 서비스 어댑터
FluxIndex.Storage.*     # 저장소 구현
FluxIndex.Cache.*       # 캐시 구현
FluxIndex.Extensions.*  # 확장 기능
```

### 의존성 주입

```csharp
// 구성 요소를 인터페이스 기반으로 교체 가능
services.AddScoped<IEmbeddingService, OpenAIEmbeddingService>();
services.AddScoped<IVectorStore, PostgreSQLVectorStore>();
services.AddScoped<ICacheService, RedisCacheService>();
```

---

## ✨ 주요 특징

- **모듈형 설계**: 필요한 구성 요소만 선택적 설치
- **AI 중립성**: 다양한 AI 서비스 지원 (OpenAI, 커스텀 등)
- **확장 가능**: 인터페이스 기반으로 새로운 구현체 추가 가능
- **평가 도구**: 9가지 지표로 RAG 성능 측정 및 개선
- **Clean Architecture**: 테스트 가능하고 유지보수 용이한 설계
- **성능 최적화**: HNSW 인덱스, 시맨틱 캐싱, 자동 튜닝

---

## 📚 문서 및 학습 자료

### 🚀 빠른 시작
- **[📖 튜토리얼](./docs/tutorial.md)** - 단계별 학습 가이드 (추천)
- **[⚡ 치트시트](./docs/cheat-sheet.md)** - 빠른 참조용 코드 패턴
- **[🏃 빠른 시작](./docs/getting-started.md)** - 5분만에 시작하기

### 📋 상세 문서
- **[🏗️ 아키텍처 가이드](./docs/architecture.md)** - Clean Architecture 설계 원칙
- **[🧠 RAG 시스템 가이드](./docs/FLUXINDEX_RAG_SYSTEM.md)** - 고급 RAG 패턴
- **[📁 문서 허브](./docs/README.md)** - 모든 문서 목록 및 학습 경로

### 💻 실습 자료
- **[📂 샘플 코드](./samples/)** - 다양한 실전 사용 사례
  - **[🚀 RealWorldDemo](./samples/FluxIndex.RealWorldDemo/)** - sqlite-vec를 활용한 실제 OpenAI API 연동 데모
- **[🧪 테스트 코드](./tests/)** - 단위 테스트 및 통합 테스트
- **[📋 개발 로드맵](./TASKS.md)** - 완료된 기능과 향후 계획

### 🎯 추천 학습 경로

**초보자**: [튜토리얼](./docs/tutorial.md) → [치트시트](./docs/cheat-sheet.md) → [RealWorldDemo](./samples/FluxIndex.RealWorldDemo/)

**중급자**: [하이브리드 검색](./docs/tutorial.md#4-하이브리드-검색) → [아키텍처](./docs/architecture.md) → [실전 예제](./samples/RealQualityTest/)

**고급자**: [RAG 시스템](./docs/FLUXINDEX_RAG_SYSTEM.md) → [Core 라이브러리](./src/FluxIndex.Core/) → 커스터마이징