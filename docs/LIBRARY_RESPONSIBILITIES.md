# 라이브러리 역할 및 책임 범위 정의

## 개요

FluxIndex 메타데이터 증강 시스템을 구현할 때, 각 라이브러리의 역할과 책임을 명확히 구분하여 **중복 개발 방지**와 **책임 소재 명확화**가 필요합니다.

---

## 1. 라이브러리별 핵심 정체성

### FileFlux
> **"파일을 텍스트와 구조 정보로 변환하는 전처리 라이브러리"**

- **입력**: PDF, DOCX, XLSX, PPTX, Markdown, HTML, TXT, JSON, CSV, ZIP
- **출력**: 텍스트 청크 + 메타데이터
- **핵심 가치**: 다양한 파일 포맷의 통일된 처리
- **현재 기능**:
  - 6가지 청킹 전략 (Auto, Smart, Intelligent, Semantic, Paragraph, FixedSize)
  - AI 기반 Metadata Enrichment (topics, keywords)
  - Quality Analysis (Quality Score, Q&A Benchmark)
  - Streaming/Parallel processing

### WebFlux
> **"웹 콘텐츠를 수집하고 구조화하는 크롤링 라이브러리"**

- **입력**: URL, HTML
- **출력**: 텍스트 청크 + 웹 메타데이터
- **핵심 가치**: 웹 문서의 효율적 수집과 정규화
- **현재 기능**:
  - 7가지 청킹 전략 (Auto, Smart, Semantic, Intelligent, MemoryOptimized, Paragraph, FixedSize)
  - 웹 표준 지원 (robots.txt, sitemap.xml, ai.txt, llms.txt, manifest.json)
  - 다양한 콘텐츠 포맷 (HTML, Markdown, JSON, XML, PDF)
  - Streaming/Parallel processing

### FluxIndex
> **"청크를 최적화된 형태로 저장하고 검색하는 RAG 라이브러리"**

- **입력**: 텍스트 청크 + 메타데이터
- **출력**: 검색 결과 + 컨텍스트
- **핵심 가치**: 고품질 검색과 컨텍스트 제공
- **현재 기능**:
  - 하이브리드 검색 (Vector + BM25 + RRF)
  - Small-to-Big 컨텍스트 확장
  - 시맨틱 캐싱, 성능 모니터링
  - 평가 프레임워크 (9가지 메트릭)

---

## 2. 책임 범위 매트릭스

### 현재 상태

| 기능 | FileFlux | WebFlux | FluxIndex |
|------|:--------:|:--------:|:---------:|
| **파일 파싱** | ✅ | - | - |
| **웹 크롤링** | - | ✅ | - |
| **텍스트 추출** | ✅ | ✅ | - |
| **청킹 (분할)** | ✅ | ✅ | - |
| **AI 기반 topics/keywords** | ✅ | - | - |
| **Quality Score** | ✅ | - | - |
| **임베딩 생성** | - | ✅ (필수) | ✅ |
| **벡터 저장** | - | - | ✅ |
| **하이브리드 검색** | - | - | ✅ |
| **재순위화** | - | - | ✅ |

### 추가 필요 (이슈 요청)

| 기능 | FileFlux | WebFlux | FluxIndex |
|------|:--------:|:--------:|:---------:|
| **HeadingPath 추출** | 🔴 요청 | 🔴 요청 | - |
| **PageNumber 추출** | 🔴 요청 | - | - |
| **ContextDependency 점수** | 🟡 요청 | 🟡 요청 | - |
| **SEO/OG 메타데이터** | - | 🔴 요청 | - |
| **Breadcrumbs 추출** | - | 🟡 요청 | - |
| **Contextual Header 생성** | - | - | ✅ 구현 |
| **Cross-Encoder Reranking** | - | - | ✅ 구현 |
| **Query Understanding** | - | - | ✅ 구현 |

---

## 3. FileFlux 역할 상세

### ✅ FileFlux 현재 기능

```csharp
// FileFlux 현재 출력 (Metadata.CustomProperties)
{
    "enriched_topics": ["토픽1", "토픽2"],      // AI 생성
    "enriched_keywords": ["키워드1", "키워드2"], // AI 생성
    "quality_score": 0.85                       // 품질 점수
}
```

### 🔴 FileFlux에 추가 요청 (구조 정보)

```csharp
// 추가로 필요한 구조 메타데이터
public class StructuralMetadata
{
    // 구조 정보 (파일에서 직접 추출)
    public List<string> HeadingPath { get; set; }   // 헤딩 계층 (P0)
    public string? SectionTitle { get; set; }       // 현재 섹션
    public int? PageNumber { get; set; }            // PDF 페이지 (P0)

    // 품질 지표 추가
    public double ContextDependency { get; set; }   // 대명사 비율 등 (P1)
}
```

### 요청 이유

| 필드 | 이유 | 활용 |
|------|------|------|
| **HeadingPath** | 규칙 기반 Contextual Header 생성에 필수 | LLM 비용 50-80% 절감 |
| **PageNumber** | 인용 참조 생성, 출처 명시 | 사용자 신뢰도 향상 |
| **ContextDependency** | LLM 호출 필요 여부 판단 | 비용 최적화 |

### ❌ FileFlux가 하지 않을 것
- **Contextual Header 생성** → FluxIndex에서 수행
- **임베딩 생성** → FluxIndex에서 수행
- **벡터 저장/검색** → FluxIndex에서 수행

---

## 4. WebFlux 역할 상세

### ✅ WebFlux 현재 기능

```csharp
// WebFlux 현재 출력
public class WebContentChunk
{
    public string Content { get; set; }
    public int ChunkIndex { get; set; }
    public string Url { get; set; }
    // ... 기본 메타데이터
}

// 웹 표준 지원
- robots.txt 준수
- sitemap.xml 파싱
- ai.txt, llms.txt, manifest.json 지원
```

**참고**: WebFlux는 **ITextEmbeddingService가 필수**로 요구됨 (시맨틱 청킹용)

### 🔴 WebFlux에 추가 요청 (웹 메타데이터)

```csharp
// 추가로 필요한 웹 메타데이터
public class WebMetadata
{
    // SEO 메타데이터 (P0)
    public string? Description { get; set; }        // <meta name="description">
    public List<string> Keywords { get; set; }      // <meta name="keywords">
    public string? Author { get; set; }             // <meta name="author">

    // Open Graph (P0)
    public string? OgTitle { get; set; }
    public string? OgDescription { get; set; }
    public string? OgType { get; set; }             // article, website
    public DateTime? PublishedAt { get; set; }      // article:published_time

    // 구조 정보 (P1)
    public List<string> HeadingPath { get; set; }   // h1/h2/h3 계층
    public List<string> Breadcrumbs { get; set; }   // 사이트 내 위치

    // 품질 지표 (P1)
    public double ContextDependency { get; set; }
}
```

### 요청 이유

| 필드 | 이유 | 활용 |
|------|------|------|
| **SEO 메타데이터** | 이미 웹 문서에 존재하는 데이터 | topics/keywords 추출 비용 절감 |
| **Open Graph** | 발행일, 타입으로 필터링 | 최신성 기반 검색 |
| **HeadingPath** | 규칙 기반 Contextual Header | LLM 비용 절감 |
| **Breadcrumbs** | 사이트 내 문서 위치 | 카테고리 자동 분류 |

### ❌ WebFlux가 하지 않을 것
- **Contextual Header 생성** → FluxIndex에서 수행
- **벡터 저장/검색** → FluxIndex에서 수행
- **재순위화** → FluxIndex에서 수행

---

## 5. FluxIndex 역할 상세

### ✅ FluxIndex 현재 기능

- 하이브리드 검색 (Vector + BM25 + RRF)
- Small-to-Big 컨텍스트 확장
- 시맨틱 캐싱
- HNSW 자동 튜닝
- 평가 프레임워크 (9가지 메트릭)

### 🟢 FluxIndex에서 구현할 기능 (Phase 8)

#### 5.1 메타데이터 증강 (Phase 8.1)

```csharp
// FluxIndex가 생성하는 증강 데이터
public class AugmentedMetadata
{
    // Contextual Header (핵심!)
    public string ContextualHeader { get; set; }

    // 추가 증강 (FileFlux에서 제공하지 않는 경우)
    public string? Summary { get; set; }            // 문서 요약
    public List<string> PotentialQuestions { get; set; }  // 예상 질문
}
```

**핵심 구현**:
- **Contextual Header 생성기**: 규칙 기반(HeadingPath 활용) + LLM 하이브리드
- **비용 최적화**: ContextDependency 점수로 LLM 호출 선택적 적용

#### 5.2 검색 파이프라인 고도화 (Phase 8.2)

- **Query Understanding**: 의도 분류, 엔티티 추출, 쿼리 확장
- **Pre-filtering**: 메타데이터 기반 필터링 API
- **Cross-Encoder Re-ranking**: BAAI/bge-reranker-v2-m3 또는 Cohere
- **Context Assembly**: 토큰 예산 관리, 인용 생성

#### 5.3 Agentic 기능 (Phase 8.3)

- **Potential Questions**: HyDE 스타일 검색
- **Query Decomposition**: 복합 질문 분리
- **Filter Extraction**: 자연어 → 메타데이터 필터

### ❌ FluxIndex가 하지 않을 것
- 파일 파싱 → FileFlux
- 웹 크롤링 → WebFlux
- 텍스트 청킹 → FileFlux/WebFlux
- 구조 메타데이터 추출 (HeadingPath, PageNumber) → FileFlux/WebFlux
- topics/keywords 기본 추출 → FileFlux (이미 지원)

---

## 6. 데이터 흐름

```
┌─────────────────────┐     ┌─────────────────────┐
│      FileFlux       │     │      WebFlux        │
│      (파일)          │     │      (URL)          │
└──────────┬──────────┘     └──────────┬──────────┘
           │                           │
           │  현재 출력:                │  현재 출력:
           │  - Content                │  - Content
           │  - enriched_topics        │  - Url
           │  - enriched_keywords      │  - Title
           │  - quality_score          │
           │                           │
           │  추가 요청:                │  추가 요청:
           │  - HeadingPath (P0)       │  - SEO/OG 메타 (P0)
           │  - PageNumber (P0)        │  - HeadingPath (P1)
           │  - ContextDependency      │  - Breadcrumbs (P1)
           │                           │
           └─────────────┬─────────────┘
                         │
                         ▼
              ┌─────────────────────┐
              │     FluxIndex       │
              │                     │
              │ Phase 8.1: 증강     │
              │ - Contextual Header │
              │ - Summary (필요시)   │
              │                     │
              │ Phase 8.2: 검색     │
              │ - Query Understanding│
              │ - Re-ranking        │
              │ - Context Assembly  │
              │                     │
              │ 기존 기능:           │
              │ - Hybrid Search     │
              │ - Vector Storage    │
              │ - Evaluation        │
              └─────────────────────┘
```

---

## 7. 이슈 요청 vs 자체 구현 구분

### FileFlux에 요청할 이슈

| 우선순위 | 이슈 | 이유 |
|----------|------|------|
| **P0** | HeadingPath 추출 | 규칙 기반 Contextual Header에 필수 |
| **P0** | PageNumber 추출 | 인용 참조 생성에 필수 |
| **P1** | ContextDependency 점수 | LLM 비용 최적화 |

### WebFlux에 요청할 이슈

| 우선순위 | 이슈 | 이유 |
|----------|------|------|
| **P0** | SEO/OG 메타데이터 | 이미 존재하는 데이터 활용 |
| **P1** | HeadingPath 추출 | h1/h2/h3 계층 구조 |
| **P1** | Breadcrumbs 추출 | 사이트 내 위치 정보 |
| **P1** | ContextDependency 점수 | LLM 비용 최적화 |

### FluxIndex 자체 구현 (Phase 8)

| Phase | 기능 | 이유 |
|-------|------|------|
| **8.1** | Contextual Header 생성기 | LLM 호출/비용 최적화 로직 포함 |
| **8.1** | 메타데이터 필터링 API | 검색 파이프라인의 일부 |
| **8.2** | Query Understanding | 검색 전략 결정 |
| **8.2** | Cross-Encoder Re-ranking | 모델 관리/추론 |
| **8.2** | Context Assembly | 토큰 예산/인용 생성 |
| **8.3** | Potential Questions | HyDE 스타일 검색 |

**참고**: FileFlux가 이미 topics/keywords 추출을 지원하므로, FluxIndex에서는 이를 정제하거나 보완하는 역할만 수행

---

## 8. FluxIndex Phase 8 구현 범위 재정의

### Phase 8.1: 핵심 기반 (FluxIndex 구현)

| 항목 | 설명 | 의존성 |
|------|------|--------|
| **SourceMetadata 스키마** | FluxIndex 내부 모델 | - |
| **ChunkMetadata 스키마** | FluxIndex 내부 모델 | - |
| **Contextual Header 생성기** | 규칙 기반 + LLM 하이브리드 | FileFlux: HeadingPath |
| **메타데이터 필터링 API** | 검색 시 필터 적용 | WebFlux: Keywords, PublishedAt |

**FileFlux 의존성**: HeadingPath, PageNumber, ContextDependency
**WebFlux 의존성**: Keywords, PublishedAt, Breadcrumbs, OgType

### Phase 8.2: 검색 강화 (FluxIndex 구현)

| 항목 | 설명 | 의존성 |
|------|------|--------|
| **Keywords 정제** | TextRank + LLM | WebFlux: Keywords (base) |
| **Topics 추출** | LLM 기반 | - |
| **Cross-Encoder 재순위화** | BAAI/bge-reranker | - |
| **Query Understanding** | 의도 분류, 엔티티 추출 | - |
| **Summary 생성** | LLM 기반 | - |

### Phase 8.3: Agentic 기능 (FluxIndex 구현)

| 항목 | 설명 | 의존성 |
|------|------|--------|
| **Potential Questions** | HyDE 스타일 | - |
| **Query Decomposition** | 복합 질문 분리 | - |
| **Filter Extraction** | 자연어 → 필터 | - |

---

## 9. 통합 인터페이스 정의

FluxIndex가 FileFlux/WebFlux로부터 받을 표준 인터페이스:

```csharp
/// <summary>
/// FileFlux/WebFlux가 구현해야 할 인터페이스
/// FluxIndex는 이 인터페이스를 통해 청크를 받음
/// </summary>
public interface ISourceChunk
{
    // 콘텐츠
    string Content { get; }
    int ChunkIndex { get; }

    // 구조 (FileFlux/WebFlux가 추출)
    IReadOnlyList<string> HeadingPath { get; }
    string? SectionTitle { get; }
    int? PageNumber { get; }

    // 품질 (FileFlux/WebFlux가 계산)
    double Quality { get; }
    double ContextDependency { get; }

    // 소스 정보
    string SourceId { get; }
    string SourceType { get; }  // "pdf", "docx", "url"
    string Title { get; }
    string? Url { get; }
    string? FilePath { get; }
    DateTime CreatedAt { get; }
    string Language { get; }

    // 웹 전용 (있으면 사용, 없으면 null)
    DateTime? PublishedAt { get; }
    string? Author { get; }
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Breadcrumbs { get; }
}
```

FluxIndex 내부에서 증강 후 사용할 모델:

```csharp
/// <summary>
/// FluxIndex 내부 모델 (증강 데이터 포함)
/// </summary>
public class EnrichedChunk
{
    // ISourceChunk에서 복사
    public string Content { get; set; }
    public int ChunkIndex { get; set; }
    public List<string> HeadingPath { get; set; }
    // ... 기타 필드

    // FluxIndex가 생성하는 증강 데이터
    public string ContextualHeader { get; set; }
    public string? Summary { get; set; }
    public List<string> Topics { get; set; }
    public List<string> RefinedKeywords { get; set; }
    public List<string> PotentialQuestions { get; set; }

    // 임베딩 (FluxIndex 생성)
    public float[] Embedding { get; set; }
}
```

---

## 10. 요약

### 핵심 원칙

1. **FileFlux/WebFlux**: 원천 데이터에서 **추출**만 수행
2. **FluxIndex**: LLM을 활용한 **생성/증강** 수행
3. **비용 발생 작업**은 모두 FluxIndex에서 관리

### 역할 한 줄 정리

| 라이브러리 | 핵심 역할 | 키워드 |
|------------|----------|--------|
| FileFlux | 파일 → 구조화된 청크 | 추출, 파싱, 청킹 |
| WebFlux | URL → 구조화된 청크 | 크롤링, 메타 파싱 |
| FluxIndex | 청크 → 검색 가능한 컨텍스트 | 증강, 임베딩, 검색 |

---

*문서 버전: 1.0*
*작성일: 2025-11-23*
