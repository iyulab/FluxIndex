# FluxIndex

[![CI/CD Pipeline](https://github.com/iyulab/FluxIndex/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/iyulab/FluxIndex/actions/workflows/build-and-release.yml)
[![NuGet](https://img.shields.io/nuget/v/FluxIndex.svg?label=FluxIndex)](https://www.nuget.org/packages/FluxIndex/)
[![NuGet](https://img.shields.io/nuget/v/FluxIndex.SDK.svg?label=FluxIndex.SDK)](https://www.nuget.org/packages/FluxIndex.SDK/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/FluxIndex.svg)](https://www.nuget.org/packages/FluxIndex/)
[![License](https://img.shields.io/github/license/iyulab/FluxIndex)](LICENSE)

RAG(Retrieval-Augmented Generation) 시스템 구축을 위한 .NET 라이브러리

> **v0.2.1**: 고도화된 RAG 평가 시스템, Small-to-Big 검색, 컨텍스트 확장 기능

## 🎯 개요

FluxIndex는 문서 인덱싱과 검색에 특화된 RAG 라이브러리입니다. 복잡한 인프라 구성 없이 벡터 검색과 키워드 검색을 결합한 하이브리드 검색을 제공합니다.

```
📄 문서 → 🔪 청킹 → 📦 인덱싱 → 🔍 검색 → 📊 평가
```

### 주요 기능
- **하이브리드 검색**: 벡터(HNSW) + 키워드(BM25) 융합 검색
- **Small-to-Big**: 정밀 검색 후 4단계 컨텍스트 확장
- **고도화된 평가**: 9가지 지표로 RAG 성능 측정
- **AI Provider 중립성**: OpenAI, 커스텀 서비스 등 자유 선택
- **모듈형 아키텍처**: 필요한 구성 요소만 선택적 사용
- **성능 최적화**: 시맨틱 캐싱, 자동 파라미터 튜닝

### 범위
- ✅ 문서 인덱싱 및 하이브리드 검색
- ✅ 임베딩 및 벡터 저장 (HNSW)
- ✅ RAG 성능 평가 시스템
- ✅ 컨텍스트 확장 및 재순위화
- ❌ 웹 서버 구현
- ❌ 사용자 인증
- ❌ 배포 인프라

---

## 📋 설치

### 기본 패키지
```bash
# 핵심 라이브러리
dotnet add package FluxIndex.SDK

# AI 서비스 (하나 선택)
dotnet add package FluxIndex.AI.OpenAI

# 저장소 (하나 선택)
dotnet add package FluxIndex.Storage.SQLite      # 개발용
dotnet add package FluxIndex.Storage.PostgreSQL  # 프로덕션용
```

### 확장 패키지 (선택사항)
```bash
# 파일 처리
dotnet add package FluxIndex.Extensions.FileFlux

# 웹 콘텐츠 처리
dotnet add package FluxIndex.Extensions.WebFlux

# 캐싱
dotnet add package FluxIndex.Cache.Redis
```

---

## 💡 사용법

### 기본 설정

```csharp
using FluxIndex.SDK;

// 클라이언트 설정
var client = new FluxIndexClientBuilder()
    .UseOpenAI("your-api-key", "text-embedding-3-small")
    .UseSQLiteInMemory()
    .Build();

// 문서 인덱싱
var document = Document.Create("doc1");
document.AddChunk(new DocumentChunk("문서 내용 첫 번째 청크", 0));
document.AddChunk(new DocumentChunk("문서 내용 두 번째 청크", 1));

await client.Indexer.IndexDocumentAsync(document);

// 검색
var results = await client.Retriever.SearchAsync("검색 질의");

foreach (var result in results)
{
    Console.WriteLine($"점수: {result.Score:F3} | {result.Chunk.Content}");
}
```

### RAG 평가 시스템

```csharp
// 평가 프레임워크 활성화
var client = new FluxIndexClientBuilder()
    .UseOpenAI("your-api-key")
    .UseSQLiteInMemory()
    .WithEvaluationSystem()  // 평가 시스템 추가
    .Build();

// 9가지 평가 지표로 RAG 성능 측정
var evaluationService = serviceProvider.GetService<IRAGEvaluationService>();
var result = await evaluationService.EvaluateQueryAsync(query, chunks, answer, goldenItem);

Console.WriteLine($"Precision@K: {result.Precision:F3}");
Console.WriteLine($"Recall@K: {result.Recall:F3}");
Console.WriteLine($"MRR: {result.MRR:F3}");
Console.WriteLine($"Faithfulness: {result.Faithfulness:F3}");
Console.WriteLine($"Answer Relevancy: {result.AnswerRelevancy:F3}");
Console.WriteLine($"Context Precision: {result.ContextPrecision:F3}");
```

### 커스텀 AI 서비스

```csharp
// 커스텀 AI 서비스 구현 후 등록
services.AddScoped<IEmbeddingService, YourCustomEmbeddingService>();
services.AddScoped<ITextCompletionService, YourLLMService>();

var client = new FluxIndexClientBuilder()
    .UseCustomAI()
    .UsePostgreSQL("connection-string")
    .Build();
```

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

## 📖 추가 정보

- **[TASKS.md](./TASKS.md)**: 완료된 기능과 개발 로드맵
- **[samples/](./samples/)**: 사용 예제 및 테스트 코드

## 🤝 기여

- 버그 리포트: [GitHub Issues](https://github.com/iyulab/FluxIndex/issues)
- 기능 제안 및 개선사항 환영

## 📄 라이선스

이 프로젝트는 [MIT 라이선스](LICENSE)로 배포됩니다.