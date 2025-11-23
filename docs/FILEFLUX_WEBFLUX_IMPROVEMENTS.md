# FileFlux/WebFlux 개선 권장사항

## 개요

RAG 메타데이터 증강 연구 결과를 바탕으로 FileFlux와 WebFlux 라이브러리에서 직접 개선해야 할 사항을 정리합니다. 이 개선 사항들은 FluxIndex의 메타데이터 증강 시스템을 효과적으로 지원하기 위해 필수적입니다.

---

## 1. FileFlux 개선 사항

### 1.1 구조적 메타데이터 추출 강화 (P0 - 필수)

**현재 상태**: 기본적인 파일 메타데이터만 추출
**필요 사항**: 문서 구조 정보를 청크에 포함

#### 1.1.1 Heading Path 추출

```csharp
public class StructuralMetadata
{
    /// <summary>
    /// 현재 청크가 속한 섹션의 계층 구조
    /// 예: ["1장 서론", "1.2 배경", "1.2.1 연구 목적"]
    /// </summary>
    public List<string> HeadingPath { get; set; } = new();

    /// <summary>
    /// 현재 섹션 제목
    /// </summary>
    public string? SectionTitle { get; set; }

    /// <summary>
    /// 문서 내 청크 위치 (0-1 정규화)
    /// </summary>
    public double PositionRatio { get; set; }
}
```

**구현 요구사항**:
- PDF: 폰트 크기/스타일로 헤딩 감지
- DOCX: Word 스타일(Heading 1, 2, 3) 파싱
- Markdown: `#`, `##`, `###` 파싱
- HTML: `<h1>`, `<h2>`, `<h3>` 태그 파싱

#### 1.1.2 페이지 번호 추출

```csharp
public class ChunkWithPage
{
    public string Content { get; set; }
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }
}
```

**중요성**: Page-level chunking이 NVIDIA 벤치마크에서 0.648 accuracy로 최고 성능

### 1.2 시맨틱 청킹 전략 지원 (P1)

**현재 상태**: 고정 크기 청킹만 지원
**필요 사항**: 의미 단위 기반 청킹 옵션

```csharp
public enum ChunkingStrategy
{
    FixedSize,           // 현재 기본
    Semantic,            // 의미 단위 (문장/문단)
    Hierarchical,        // 구조 기반 (섹션/챕터)
    PageLevel,           // 페이지 단위
    Hybrid               // 구조 우선 + 크기 제한
}

public class ChunkingOptions
{
    public ChunkingStrategy Strategy { get; set; } = ChunkingStrategy.FixedSize;

    // 시맨틱 청킹 옵션
    public bool PreserveParagraphs { get; set; } = true;
    public bool PreserveSentences { get; set; } = true;

    // 계층적 청킹 옵션
    public int MaxHeadingLevel { get; set; } = 3;  // h1, h2, h3까지 분리
}
```

### 1.3 언어 감지 (P1)

**필요 사항**: 문서/청크 단위 언어 자동 감지

```csharp
public class DocumentMetadata
{
    /// <summary>
    /// 감지된 언어 코드 (ISO 639-1)
    /// 예: "ko", "en", "ja"
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// 언어 감지 신뢰도 (0-1)
    /// </summary>
    public double LanguageConfidence { get; set; }
}
```

**구현 제안**:
- NTextCat 또는 langdetect 라이브러리 사용
- 문서 전체와 개별 청크 모두 감지 (다국어 문서 대응)

### 1.4 테이블/이미지 컨텍스트 보존 (P2)

**문제**: 테이블이 청크로 분리되면 컬럼 헤더 정보 소실

```csharp
public class TableChunk
{
    public string Content { get; set; }

    /// <summary>
    /// 테이블 캡션 또는 제목
    /// </summary>
    public string? TableCaption { get; set; }

    /// <summary>
    /// 컬럼 헤더 (컨텍스트용)
    /// </summary>
    public List<string> ColumnHeaders { get; set; }

    /// <summary>
    /// 테이블 내 행 범위
    /// </summary>
    public (int Start, int End) RowRange { get; set; }
}
```

### 1.5 청크 품질 점수 개선 (P2)

**현재 상태**: 기본 품질 점수 제공
**추가 필요**: Contextual Header 생성 필요성 판단용 점수

```csharp
public class ChunkQualityMetrics
{
    /// <summary>
    /// 기존 품질 점수
    /// </summary>
    public double Quality { get; set; }

    /// <summary>
    /// 컨텍스트 의존도 (높을수록 Contextual Header 필요)
    /// - 대명사 비율
    /// - 참조 표현 비율 ("위의", "앞서 언급한")
    /// - 고유명사 부재
    /// </summary>
    public double ContextDependency { get; set; }

    /// <summary>
    /// 정보 밀도 (높을수록 중요한 청크)
    /// </summary>
    public double InformationDensity { get; set; }
}
```

**활용**: `ContextDependency`가 높은 청크에만 LLM 기반 Contextual Header 생성 (비용 최적화)

---

## 2. WebFlux 개선 사항

### 2.1 웹 문서 메타데이터 추출 강화 (P0 - 필수)

**현재 상태**: 기본 URL/제목만 추출
**필요 사항**: SEO 및 구조화 데이터 추출

```csharp
public class WebDocumentMetadata
{
    // 기본 정보
    public string Url { get; set; }
    public string Title { get; set; }

    // SEO 메타데이터
    public string? Description { get; set; }      // <meta name="description">
    public List<string> Keywords { get; set; }    // <meta name="keywords">
    public string? Author { get; set; }           // <meta name="author">

    // Open Graph
    public string? OgTitle { get; set; }
    public string? OgDescription { get; set; }
    public string? OgImage { get; set; }
    public string? OgType { get; set; }           // article, website, etc.

    // 시간 정보
    public DateTime? PublishedAt { get; set; }    // <meta property="article:published_time">
    public DateTime? ModifiedAt { get; set; }     // <meta property="article:modified_time">

    // 구조화 데이터
    public string? SchemaOrgType { get; set; }    // JSON-LD @type
    public Dictionary<string, string> StructuredData { get; set; }
}
```

### 2.2 DOM 구조 기반 청킹 (P1)

**문제**: HTML을 플레인 텍스트로 변환 후 청킹하면 구조 정보 소실

```csharp
public class HtmlChunkingOptions
{
    /// <summary>
    /// DOM 구조 기반 청킹 활성화
    /// </summary>
    public bool PreserveDomStructure { get; set; } = true;

    /// <summary>
    /// 주요 콘텐츠 영역 선택자
    /// 예: "article", "main", ".content"
    /// </summary>
    public List<string> ContentSelectors { get; set; } = new() { "article", "main" };

    /// <summary>
    /// 제외할 영역 선택자
    /// 예: "nav", "footer", ".sidebar"
    /// </summary>
    public List<string> ExcludeSelectors { get; set; } = new() { "nav", "footer", "aside" };

    /// <summary>
    /// 섹션 분리 기준 태그
    /// </summary>
    public List<string> SectionTags { get; set; } = new() { "section", "article", "div.section" };
}
```

### 2.3 사이트맵 및 크롤링 컨텍스트 (P1)

**필요 사항**: 사이트 전체 구조 내에서의 문서 위치 정보

```csharp
public class SiteContext
{
    /// <summary>
    /// 사이트 내 계층 구조
    /// 예: ["Documentation", "API Reference", "Search API"]
    /// </summary>
    public List<string> Breadcrumbs { get; set; }

    /// <summary>
    /// 관련 페이지 URL (내부 링크 분석)
    /// </summary>
    public List<string> RelatedPages { get; set; }

    /// <summary>
    /// 사이트맵에서의 우선순위
    /// </summary>
    public double? SitemapPriority { get; set; }
}
```

### 2.4 동적 콘텐츠 처리 개선 (P2)

**문제**: JavaScript 렌더링 콘텐츠 누락

```csharp
public class DynamicContentOptions
{
    /// <summary>
    /// JavaScript 렌더링 대기 시간 (ms)
    /// </summary>
    public int RenderWaitTime { get; set; } = 3000;

    /// <summary>
    /// 특정 요소 대기
    /// </summary>
    public string? WaitForSelector { get; set; }

    /// <summary>
    /// 무한 스크롤 페이지 처리
    /// </summary>
    public int MaxScrollCount { get; set; } = 0;
}
```

---

## 3. 공통 개선 사항

### 3.1 통합 메타데이터 인터페이스 (P0)

FluxIndex와의 원활한 연동을 위한 표준 인터페이스:

```csharp
public interface IEnrichedChunk
{
    string Content { get; }

    // 식별
    string ChunkId { get; }
    int ChunkIndex { get; }

    // 구조
    List<string> HeadingPath { get; }
    string? SectionTitle { get; }
    int? PageNumber { get; }

    // 품질
    double Quality { get; }
    double ContextDependency { get; }

    // 소스 참조
    ISourceMetadata Source { get; }
}

public interface ISourceMetadata
{
    string SourceId { get; }
    string SourceType { get; }
    string Title { get; }
    string? Url { get; }
    string? FilePath { get; }
    DateTime CreatedAt { get; }
    string Language { get; }
    int WordCount { get; }
    int ChunkCount { get; }
}
```

### 3.2 배치 처리 최적화 (P1)

**필요 사항**: 대량 문서 처리 시 메타데이터 추출 병렬화

```csharp
public interface IBatchProcessor
{
    Task<IEnumerable<IEnrichedChunk>> ProcessBatchAsync(
        IEnumerable<string> inputs,
        ProcessingOptions options,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public class BatchProgress
{
    public int TotalDocuments { get; set; }
    public int ProcessedDocuments { get; set; }
    public int TotalChunks { get; set; }
    public int ProcessedChunks { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan EstimatedRemaining { get; set; }
}
```

### 3.3 품질 검증 콜백 (P2)

**필요 사항**: 청킹 결과 품질 검증을 위한 콜백

```csharp
public class ChunkValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Warnings { get; set; }
    public List<string> Errors { get; set; }
}

public interface IChunkValidator
{
    ChunkValidationResult Validate(IEnrichedChunk chunk);
}

// 기본 검증기 예시
public class DefaultChunkValidator : IChunkValidator
{
    public ChunkValidationResult Validate(IEnrichedChunk chunk)
    {
        var result = new ChunkValidationResult { IsValid = true };

        // 최소 토큰 수 검증
        if (chunk.Content.Split().Length < 10)
            result.Warnings.Add("Chunk is very short (< 10 words)");

        // 헤딩 경로 유무 검증
        if (chunk.HeadingPath?.Count == 0)
            result.Warnings.Add("No heading path detected");

        // 높은 컨텍스트 의존도 경고
        if (chunk.ContextDependency > 0.8)
            result.Warnings.Add("High context dependency - contextual header recommended");

        return result;
    }
}
```

---

## 4. 구현 우선순위

### Phase 1: 필수 기능 (2주)

| 라이브러리 | 항목 | 설명 |
|------------|------|------|
| FileFlux | HeadingPath 추출 | PDF/DOCX/Markdown 헤딩 파싱 |
| FileFlux | 페이지 번호 추출 | PDF 페이지 번호 매핑 |
| WebFlux | 웹 메타데이터 강화 | SEO/OpenGraph/Schema.org 추출 |
| 공통 | IEnrichedChunk 인터페이스 | FluxIndex 연동 표준화 |

### Phase 2: 향상 기능 (3주)

| 라이브러리 | 항목 | 설명 |
|------------|------|------|
| FileFlux | 시맨틱 청킹 | 의미 단위 기반 분할 |
| FileFlux | 언어 감지 | ISO 639-1 언어 코드 |
| WebFlux | DOM 구조 청킹 | HTML 구조 보존 |
| WebFlux | Breadcrumbs 추출 | 사이트 내 위치 정보 |

### Phase 3: 고급 기능 (4주)

| 라이브러리 | 항목 | 설명 |
|------------|------|------|
| FileFlux | 테이블 컨텍스트 | 테이블 헤더 보존 |
| FileFlux | ContextDependency 점수 | LLM 비용 최적화용 |
| WebFlux | 동적 콘텐츠 | JavaScript 렌더링 |
| 공통 | 배치 최적화 | 병렬 처리 강화 |

---

## 5. FluxIndex 연동 시나리오

### 5.1 Contextual Header 생성 최적화

```csharp
// FileFlux에서 청크 생성
var chunks = await fileFlux.ExtractChunksAsync(document);

// FluxIndex에서 선택적 Contextual Header 생성
foreach (var chunk in chunks)
{
    string header;

    if (chunk.ContextDependency > 0.7)
    {
        // 높은 컨텍스트 의존도 → LLM 기반 생성
        header = await llm.GenerateContextualHeaderAsync(document, chunk);
    }
    else if (chunk.HeadingPath.Any())
    {
        // 구조 정보 있음 → 규칙 기반 생성
        header = $"[{chunk.Source.Title}] {string.Join(" > ", chunk.HeadingPath)}";
    }
    else
    {
        // 기본 헤더
        header = $"[{chunk.Source.Title}]";
    }

    await indexer.IndexChunkAsync(chunk, header);
}
```

### 5.2 메타데이터 필터링

```csharp
// WebFlux에서 추출한 메타데이터 활용
var webChunks = await webFlux.CrawlAndChunkAsync(urls);

// FluxIndex 검색 시 필터링
var results = await retriever.SearchAsync(new SearchRequest
{
    Query = "최신 API 변경사항",
    Filter = new SearchFilter
    {
        Languages = new[] { "ko" },
        CreatedAfter = DateTime.Now.AddMonths(-3),
        Categories = new[] { "Documentation" }  // Schema.org type 활용
    }
});
```

---

## 6. 결론

FileFlux와 WebFlux의 개선은 FluxIndex 메타데이터 증강 시스템의 효과를 극대화하는 핵심 전제조건입니다. 특히:

1. **HeadingPath와 구조 정보**는 규칙 기반 Contextual Header 생성에 필수
2. **ContextDependency 점수**는 LLM 비용 최적화에 핵심
3. **표준화된 인터페이스**는 FluxIndex와의 원활한 연동 보장

이 개선 사항들을 통해 검색 실패율 35-67% 감소라는 Anthropic 연구 결과를 .NET 생태계에서도 달성할 수 있습니다.

---

*문서 버전: 1.0*
*작성일: 2025-11-23*
*관련 문서: [METADATA_AUGMENTATION_GUIDE.md](./METADATA_AUGMENTATION_GUIDE.md)*
