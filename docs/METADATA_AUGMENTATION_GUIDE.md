# FluxIndex 메타데이터 증강 구현 가이드

## 개요

본 문서는 RAG 메타데이터 증강 연구 결과를 바탕으로 FluxIndex에 구현할 핵심 기능과 전략을 정리합니다.

---

## 1. 핵심 연구 결과 요약

### 1.1 검증된 효과

| 기법 | 효과 | 출처 |
|------|------|------|
| **Contextual Retrieval** | 검색 실패율 35-67% 감소 | Anthropic (2024) |
| **Hybrid Search + Reranking** | 정확도 2-3배 향상 | 다수 연구 |
| **메타데이터 필터링** | 정확도 12%↑, 비용 20%↓ | Deasy Labs |
| **Page-level Chunking** | 0.648 accuracy (최고) | NVIDIA |

### 1.2 핵심 발견

1. **Contextual Header가 가장 중요**: 단일 기법으로 검색 실패율 67% 감소
2. **하이브리드 검색 필수**: Vector + BM25 + RRF 융합이 표준
3. **Cross-Encoder 재순위화**: 최종 정확도 20-30% 향상
4. **과잉 증강 방지**: 토큰 비율 20-30% 제한 필요

---

## 2. 메타데이터 스키마 설계

### 2.1 소스 레벨 메타데이터 (Phase 1 - 필수)

```csharp
public class SourceMetadata
{
    // 식별자
    public string SourceId { get; set; }          // SHA256(content)[:16]

    // 기본 정보
    public SourceType SourceType { get; set; }    // Pdf, Docx, Html, Url, Markdown
    public string Title { get; set; }
    public string? Url { get; set; }
    public string? FilePath { get; set; }

    // 시간
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // 메트릭
    public int WordCount { get; set; }
    public int ChunkCount { get; set; }
}
```

### 2.2 소스 레벨 확장 메타데이터 (Phase 2)

```csharp
public class SourceMetadataExtended : SourceMetadata
{
    // 언어 및 분류
    public string Language { get; set; } = "en";  // ISO 639-1
    public string? Category { get; set; }
    public List<string> Tags { get; set; } = new();

    // LLM 생성 (비용 발생)
    public string? Summary { get; set; }          // 전체 요약 (< 500자)
    public List<string> Topics { get; set; } = new();  // 주요 토픽 3-7개

    // 출처 메타
    public string? Author { get; set; }
    public DateTime? PublishedAt { get; set; }
}
```

### 2.3 청크 레벨 메타데이터 (Phase 1 - 필수)

```csharp
public class ChunkMetadata
{
    // 식별자
    public string ChunkId { get; set; }           // source_id + '_' + chunk_index
    public string SourceId { get; set; }          // FK to SourceMetadata
    public int ChunkIndex { get; set; }

    // 핵심: Contextual Header (가장 중요!)
    public string ContextualHeader { get; set; }  // < 200자

    // 메트릭
    public int CharCount { get; set; }
    public int TokenCount { get; set; }
}
```

### 2.4 청크 레벨 확장 메타데이터 (Phase 2)

```csharp
public class ChunkMetadataExtended : ChunkMetadata
{
    // 구조적 위치
    public string? SectionTitle { get; set; }
    public List<string> HeadingPath { get; set; } = new();  // ["1장", "1.2절"]
    public int? PageNumber { get; set; }

    // 검색 강화
    public List<string> Keywords { get; set; } = new();  // 5-10개

    // Phase 3: Agentic
    public List<string> PotentialQuestions { get; set; } = new();  // 2-3개
}
```

---

## 3. Contextual Header 생성 시스템

### 3.1 핵심 중요성

> **Contextual Header는 본 연구의 가장 중요한 증강 요소입니다.**
> Anthropic 연구에 따르면, 이 단일 기법만으로 검색 실패율을 67%까지 감소시킬 수 있습니다.

### 3.2 생성 프롬프트

```
<document>
{WHOLE_DOCUMENT}
</document>

<chunk>
{CHUNK_CONTENT}
</chunk>

위 문서에서 추출된 청크입니다.
이 청크를 문서 전체 맥락 내에서 이해할 수 있도록,
검색 시 활용될 간결한 문맥 정보를 작성하세요.

요구사항:
- 50-150자 이내
- 문서 제목, 섹션, 시간적 맥락 포함
- 청크 내용을 반복하지 말 것
- 검색 키워드가 될 수 있는 핵심 개체명 포함

문맥 정보만 출력하세요:
```

### 3.3 예시

**원본 청크:**
> "회사의 매출이 전 분기 대비 3% 증가했다."

**Contextual Header:**
> "[ACME Corp 2023년 2분기 SEC 제출 보고서, 재무 성과 섹션] 전 분기 매출 $3.14억 기준."

**최종 저장 형태:**
```json
{
  "chunk_id": "doc123_chunk_5",
  "contextual_header": "[ACME Corp 2023년 2분기 SEC 제출 보고서, 재무 성과 섹션] 전 분기 매출 $3.14억 기준.",
  "content": "회사의 매출이 전 분기 대비 3% 증가했다.",
  "embedding": [...]  // header + content 결합하여 임베딩
}
```

### 3.4 규칙 기반 대안 (비용 절감)

명확한 구조의 문서는 LLM 없이 규칙 기반으로 생성 가능:

```csharp
public string GenerateHeaderRuleBased(SourceMetadata source, ChunkMetadata chunk)
{
    var parts = new List<string>();

    if (!string.IsNullOrEmpty(source.Title))
        parts.Add($"[{source.Title}]");

    if (chunk.HeadingPath?.Any() == true)
        parts.Add(string.Join(" > ", chunk.HeadingPath));
    else if (!string.IsNullOrEmpty(chunk.SectionTitle))
        parts.Add(chunk.SectionTitle);

    if (chunk.PageNumber.HasValue)
        parts.Add($"(p.{chunk.PageNumber})");

    return string.Join(" ", parts);
}
```

### 3.5 비용 최적화 전략

| 전략 | 설명 | 비용 절감 |
|------|------|----------|
| **Prompt Caching** | 동일 문서의 청크들에 캐시 적용 | ~90% |
| **소형 모델 사용** | Claude 3 Haiku, GPT-4o-mini | ~80% |
| **선택적 적용** | 복잡한 문서에만 LLM 사용 | ~50% |
| **규칙 기반 우선** | 구조화된 문서는 규칙으로 생성 | 100% |

---

## 4. 지능형 검색 파이프라인

### 4.1 전체 아키텍처

```
User Query
    │
    ▼
┌─────────────────────────────────────────┐
│  Stage 1: Query Understanding           │
│  - Intent classification                │
│  - Entity extraction                    │
│  - Query expansion                      │
└─────────────────┬───────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────┐
│  Stage 2: Pre-filtering                 │
│  - Metadata-based filtering             │
│  - Index routing                        │
└─────────────────┬───────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────┐
│  Stage 3: Hybrid Retrieval              │
│  ┌─────────┐    ┌─────────┐            │
│  │ Vector  │    │  BM25   │            │
│  │ Search  │    │ Search  │            │
│  └────┬────┘    └────┬────┘            │
│       └──────┬───────┘                  │
│              ▼                          │
│       ┌──────────┐                      │
│       │ RRF Fusion│                     │
│       └────┬─────┘                      │
└────────────┼────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────┐
│  Stage 4: Re-ranking                    │
│  - Cross-encoder scoring                │
│  - Diversity filtering                  │
└─────────────────┬───────────────────────┘
             │
             ▼
┌─────────────────────────────────────────┐
│  Stage 5: Context Assembly              │
│  - Source summary inclusion             │
│  - Citation reference                   │
│  - Token budget management              │
└─────────────────────────────────────────┘
             │
             ▼
        Final Context
```

### 4.2 Query Understanding (Stage 1)

```csharp
public class QueryAnalysis
{
    public string OriginalQuery { get; set; }

    // 의도 분류
    public QueryIntent Intent { get; set; }  // Factual, Analytical, Comparative, Exploratory

    // 개체 추출
    public List<Entity> Entities { get; set; }

    // 시간 제약
    public DateRange? TimeConstraint { get; set; }

    // 쿼리 확장
    public List<string> ExpandedQueries { get; set; }

    // 검색 전략
    public SearchStrategy Strategy { get; set; }  // Vector, Keyword, Hybrid

    // 라우팅
    public List<string>? TargetCategories { get; set; }
}
```

**의도별 검색 전략:**

| 의도 | 설명 | 검색 전략 | 청크 크기 |
|------|------|----------|----------|
| factual | 단순 사실 질문 | hybrid | 256-512 토큰 |
| analytical | 분석/설명 요청 | vector 우선 | 1024+ 토큰 |
| comparative | 비교 질문 | multi-query | 512-1024 토큰 |
| exploratory | 탐색적 질문 | vector | 다양 |

### 4.3 Pre-filtering (Stage 2)

```csharp
public class SearchFilter
{
    // 메타데이터 필터
    public List<SourceType>? SourceTypes { get; set; }
    public List<string>? Languages { get; set; }
    public List<string>? Categories { get; set; }

    // 시간 필터
    public DateTime? CreatedAfter { get; set; }
    public DateTime? CreatedBefore { get; set; }

    // 토픽 필터
    public List<string>? TopicsAny { get; set; }   // OR 조건
    public List<string>? TopicsAll { get; set; }   // AND 조건
}
```

---

## 5. 과잉 증강 방지 전략

### 5.1 "Lost in the Middle" 현상

LLM과 임베딩 모델은 입력의 앞/뒷부분에 집중하고 중간을 간과합니다.

**방지 전략:**
- **토큰 비율 제한**: 문맥 텍스트는 청크 전체의 20-30% 이내
- **압축 요약**: 문장 대신 핵심 키워드/구(Phrase) 사용
- **앞단 배치**: 문맥 정보는 항상 청크 앞에 위치

### 5.2 비용 관리

| 전략 | 설명 |
|------|------|
| 문서 단위 캐싱 | 요약은 한 번만 생성, 모든 청크에 재사용 |
| 조건부 증강 | 정보가 불충분한 청크에만 LLM 적용 |
| 배치 처리 | 여러 청크를 한 번의 API 호출로 처리 |

---

## 6. API 설계 제안

### 6.1 인덱싱 API

```csharp
public interface IFluxIndexer
{
    Task<IndexResult> IndexDocumentAsync(
        string content,
        string sourceId,
        IndexOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<IndexResult> IndexDocumentAsync(
        string content,
        SourceMetadata metadata,
        IndexOptions? options = null,
        CancellationToken cancellationToken = default);
}

public class IndexOptions
{
    // 핵심 옵션
    public bool GenerateContextualHeaders { get; set; } = true;
    public bool ExtractKeywords { get; set; } = true;
    public bool GenerateSummary { get; set; } = true;
    public bool ExtractTopics { get; set; } = true;

    // 청크 설정
    public int ChunkSize { get; set; } = 512;
    public int ChunkOverlap { get; set; } = 50;

    // 비용 최적화
    public ContextualHeaderMode HeaderMode { get; set; } = ContextualHeaderMode.Hybrid;
}

public enum ContextualHeaderMode
{
    RuleBased,      // 규칙 기반만 (무료)
    LlmBased,       // LLM만 (비용 발생)
    Hybrid          // 규칙 우선, 필요시 LLM (권장)
}
```

### 6.2 검색 API

```csharp
public interface IFluxRetriever
{
    Task<SearchResponse> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default);

    Task<SearchResponse> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default);

    Task<AssembledContext> GetContextAsync(
        string query,
        ContextOptions? options = null,
        CancellationToken cancellationToken = default);
}

public class ContextOptions
{
    public int TokenBudget { get; set; } = 8000;
    public bool IncludeSourceSummaries { get; set; } = true;
    public CitationFormat CitationFormat { get; set; } = CitationFormat.Numbered;
}
```

---

## 7. 평가 메트릭

### 7.1 검색 품질

| 메트릭 | 설명 | 목표값 |
|--------|------|--------|
| Recall@K | 상위 K개 중 관련 문서 비율 | Recall@10 ≥ 0.85 |
| MRR | Mean Reciprocal Rank | MRR ≥ 0.70 |
| NDCG@K | Normalized DCG | NDCG@10 ≥ 0.75 |
| Retrieval Failure Rate | 관련 문서 미검색 비율 | < 3% |

### 7.2 생성 품질

| 메트릭 | 설명 | 목표값 |
|--------|------|--------|
| Groundedness | 컨텍스트 기반 응답 비율 | ≥ 0.90 |
| Relevance | 질문 답변 관련도 | ≥ 0.85 |
| Faithfulness | 할루시네이션 없음 | ≥ 0.95 |

---

## 8. 구현 우선순위

### Phase 1: 핵심 기반 (2-3주)
- P0: SourceMetadata/ChunkMetadata 기본 스키마
- P0: **Contextual Header 생성** (검색 실패율 35-40%↓)
- P1: 메타데이터 필터링 API
- P1: 벤치마크 기준선 설정

### Phase 2: 검색 강화 (3-4주)
- P1: Keywords 추출 (BM25 강화)
- P1: Topics 추출 (전역 쿼리 지원)
- P1: **Cross-Encoder 재순위화** (정확도 20-30%↑)
- P2: Query Understanding
- P2: Summary 생성

### Phase 3: Agentic 기능 (4-6주)
- P2: potential_questions[] (HyDE 스타일)
- P2: 자가 평가
- P3: 반복 검색
- P3: 다중 인덱스 라우팅

---

## 참고 자료

- Anthropic - "Introducing Contextual Retrieval" (2024.09)
- Microsoft Research - "GraphRAG" (2024.04)
- NVIDIA - "Chunking Strategies Benchmark" (2024)
- LlamaIndex - Metadata Extraction Documentation

---

*문서 버전: 1.0*
*작성일: 2025-11-23*
