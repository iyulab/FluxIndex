# FluxIndex.Extensions.WebFlux

FluxIndex와 WebFlux를 통합하여 웹 콘텐츠를 RAG 시스템에 인덱싱하는 확장 패키지입니다.

## 개요

이 패키지는 WebFlux 라이브러리를 사용하여 웹 페이지를 크롤링하고, 콘텐츠를 추출하여 FluxIndex 벡터 데이터베이스에 인덱싱합니다.

## 주요 기능

- **웹 콘텐츠 크롤링**: WebFlux를 통한 지능형 웹 페이지 크롤링
- **다양한 청킹 전략**: Auto, Smart, Semantic, Intelligent, MemoryOptimized, Paragraph, FixedSize
- **풍부한 메타데이터**: 제목, 설명, 작성자, 키워드, 게시일, 수정일 등 추출
- **스트리밍 API**: 메모리 효율적인 대용량 웹사이트 처리
- **품질 점수**: 각 청크에 대한 품질 점수 (0.0-1.0)
- **계층 구조**: 부모-자식 청크 관계 추적

## 빠른 시작

```csharp
using FluxIndex.SDK;
using FluxIndex.Extensions.WebFlux;

var services = new ServiceCollection();

// AI 서비스 등록 (WebFlux 요구사항)
services.AddScoped<ITextEmbeddingService, YourEmbeddingService>();

// FluxIndex 등록
services.AddFluxIndex()
    .AddSQLiteVectorStore()
    .UseOpenAIEmbedding(apiKey: "your-api-key");

// WebFlux 통합 등록
services.AddWebFluxIntegration(options =>
{
    options.DefaultChunkingStrategy = ChunkingStrategyType.Auto;
    options.DefaultMaxChunkSize = 512;
    options.DefaultChunkOverlap = 50;
});

var serviceProvider = services.BuildServiceProvider();
var webFlux = serviceProvider.GetRequiredService<WebFluxIntegration>();

// 단일 URL 인덱싱
var documentId = await webFlux.IndexWebContentAsync("https://docs.microsoft.com");
```

## 청킹 전략

| 전략 | 설명 | 적합한 사용 사례 |
|------|------|------------------|
| **Auto** | 콘텐츠 유형에 따라 자동 선택 | 일반적인 웹 페이지 (권장) |
| **Smart** | HTML 구조 기반 분할 | 구조화된 문서, 기술 문서 |
| **Semantic** | 의미 기반 분할 | 블로그, 기사 |
| **Intelligent** | LLM 기반 고급 분할 | 복잡한 콘텐츠 |
| **MemoryOptimized** | 메모리 효율적 | 대용량 웹사이트 |
| **Paragraph** | 단락 경계 기반 | 마크다운 문서 |
| **FixedSize** | 고정 크기 분할 | 테스트용 |

## 메타데이터

각 청크는 다음 메타데이터를 포함합니다:

### 기본 메타데이터
- `wf_chunk_id`: WebFlux 청크 고유 ID
- `wf_sequence_number`: 문서 내 순서
- `wf_source_url`: 원본 URL
- `wf_quality_score`: 품질 점수 (0.0-1.0)
- `wf_chunk_type`: 청크 유형
- `wf_created_at`: 생성 시간

### 페이지 메타데이터
- `wf_page_title`: 페이지 제목
- `wf_description`: 페이지 설명
- `wf_published_date`: 게시일
- `wf_modified_date`: 수정일
- `wf_author`: 작성자
- `wf_keywords`: 키워드
- `wf_language`: 언어 코드

## 문서

자세한 내용은 다음을 참조하세요:

- [FluxIndex README](../../README.md)
- [WebFlux Tutorial](https://github.com/iyulab/WebFlux/blob/main/docs/TUTORIAL.md)
- [WebFlux Chunking Strategies](https://github.com/iyulab/WebFlux/blob/main/docs/CHUNKING_STRATEGIES.md)
