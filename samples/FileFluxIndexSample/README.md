# FileFluxIndexSample

FileFlux + FluxIndex + OpenAI를 통합한 End-to-End RAG 파이프라인 테스트 환경입니다.

## 특징

- **SQLite 기반**: 별도의 데이터베이스 서버 설치 없이 즉시 테스트 가능
- **대화형 UI**: Spectre.Console을 사용한 직관적인 메뉴 시스템
- **포괄적 테스트**: 성능, 품질, 재순위화 전략 비교
- **OpenAI 통합**: Text Completion 및 Vision API 지원

## 사전 요구사항

- .NET 9.0 SDK
- OpenAI API Key (환경변수 `OPENAI_API_KEY` 또는 appsettings.json에 설정)
- FileFlux 패키지 (NuGet에서 자동 설치)

## 테스트 데이터

기본적으로 `D:\data\FileFlux\test` 디렉토리의 테스트 파일을 사용합니다:
- **PDF**: `test-pdf/` - GPT 모델 카드 등
- **DOCX**: `test-docx/` - Word 문서 샘플
- **PPTX**: `test-pptx/` - PowerPoint 프레젠테이션
- **XLSX**: `test-xlsx/` - Excel 스프레드시트
- **MD**: `test-md/` - Markdown 문서

테스트 경로는 `appsettings.json`의 `TestConfiguration:TestDataPath`에서 변경 가능합니다.

## 설정

### 1. OpenAI API Key 설정

환경변수로 설정:
```bash
export OPENAI_API_KEY="your-api-key-here"  # Linux/Mac
set OPENAI_API_KEY=your-api-key-here       # Windows
```

또는 `appsettings.json` 파일 수정:
```json
{
  "OpenAI": {
    "ApiKey": "your-api-key-here"
  }
}
```

### 2. SQLite 데이터베이스

- 자동으로 `fluxindex.db` 파일이 생성됩니다
- 개발 환경에서는 `fluxindex_dev.db` 사용
- 데이터베이스 초기화는 자동으로 수행됩니다

## 실행 방법

```bash
# 프로젝트 디렉토리로 이동
cd samples/FileFluxIndexSample

# 실행
dotnet run
```

## 테스트 시나리오

### 1. 단일 문서 처리 (End-to-End)
- 문서를 읽고, 청킹하고, 인덱싱하는 전체 과정 테스트
- 처리 시간, 청크 수, 품질 점수 표시

### 2. 배치 처리 성능 테스트
- 여러 문서를 병렬로 처리
- 처리량, 메모리 사용량 측정

### 3. 검색 품질 테스트
- Recall@10, MRR, NDCG 등 품질 메트릭 계산
- 다양한 쿼리로 검색 성능 평가

### 4. 재순위화 전략 비교
- 6가지 재순위화 전략 성능 비교
  - Semantic
  - Quality
  - Contextual
  - Hybrid
  - LLM
  - Adaptive

### 5. 스트리밍 처리 데모
- 대용량 문서의 실시간 처리
- 진행 상황 표시

### 6. 멀티모달 문서 처리
- 텍스트와 이미지가 혼합된 문서 처리
- Vision API를 통한 이미지 텍스트 추출

### 7. 전체 벤치마크 실행
- BenchmarkDotNet을 사용한 종합 성능 측정
- 상세한 성능 리포트 생성

## 프로젝트 구조

```
FileFluxIndexSample/
├── Program.cs                 # 메인 애플리케이션
├── Services/
│   ├── PerformanceTester.cs  # 성능 테스트
│   ├── QualityTester.cs      # 품질 메트릭
│   ├── FullBenchmark.cs      # BenchmarkDotNet
│   ├── OpenAITextCompletionService.cs
│   └── OpenAIImageToTextService.cs
├── TestDocuments/             # 로컬 테스트 문서 (선택적)
├── D:\data\FileFlux\test\     # FileFlux 테스트 문서 (기본)
├── appsettings.json          # 설정 파일
└── fluxindex.db              # SQLite 데이터베이스 (자동 생성)
```

## 성능 메트릭

### 품질 메트릭
- **Recall@10**: 상위 10개 결과의 재현율
- **MRR (Mean Reciprocal Rank)**: 첫 번째 관련 결과의 순위
- **NDCG (Normalized Discounted Cumulative Gain)**: 순위별 가중치 적용
- **Precision**: 정확도
- **F1 Score**: Precision과 Recall의 조화평균

### 성능 메트릭
- **처리량**: 초당 처리 문서/청크 수
- **응답 시간**: p50, p95, p99 백분위수
- **메모리 사용량**: 피크 메모리, GC 압력
- **병렬 처리 효율**: 멀티코어 활용도

## 문제 해결

### SQLite 관련 오류
- 권한 문제: 현재 디렉토리에 쓰기 권한 확인
- 잠금 오류: 다른 프로세스가 DB를 사용 중인지 확인

### OpenAI API 오류
- API Key 확인
- 할당량 및 요금 제한 확인
- 네트워크 연결 상태 확인

### 메모리 부족
- 배치 크기 조정 (`TestConfiguration:ParallelDegree`)
- 청크 크기 조정 (`FluxIndex:ChunkingOptions:MaxChunkSize`)

## PostgreSQL로 전환하기

SQLite 대신 PostgreSQL을 사용하려면:

1. `Program.cs`에서:
```csharp
// 변경 전
.ConfigureVectorStore(store => store.UseSQLite(connectionString))

// 변경 후
.ConfigureVectorStore(store => store.UsePostgreSQL(connectionString))
```

2. `FileFluxIndexSample.csproj`에서:
```xml
<!-- 변경 전 -->
<ProjectReference Include="..\..\src\FluxIndex.Storage.SQLite\FluxIndex.Storage.SQLite.csproj" />

<!-- 변경 후 -->
<ProjectReference Include="..\..\src\FluxIndex.Storage.PostgreSQL\FluxIndex.Storage.PostgreSQL.csproj" />
```

3. Connection String 변경:
```json
"ConnectionStrings": {
  "PostgreSQL": "Host=localhost;Database=fluxindex;Username=postgres;Password=postgres"
}
```

## 라이선스

이 샘플 프로젝트는 FluxIndex 프로젝트의 일부입니다.