# FluxIndex Quality Test

실제 OpenAI API를 사용하여 FluxIndex의 품질과 성능을 테스트하는 프로젝트입니다.

## 실행 방법

### 1. OpenAI API 키 설정

다음 중 하나의 방법으로 API 키를 설정하세요:

**방법 1: 환경 변수 (추천)**
```bash
export OPENAI_API_KEY="your-openai-api-key-here"
```

**방법 2: appsettings.json 파일**
```json
{
  "OpenAI": {
    "ApiKey": "your-openai-api-key-here"
  }
}
```

### 2. 프로젝트 실행

```bash
cd samples/RealQualityTest
dotnet run
```

## 테스트 내용

### 1. 테스트 문서 생성
- 머신러닝 관련 5개 문서 자동 생성
- 각 문서는 특정 주제 (ML, 신경망, 그라디언트 디센트, 트랜스포머, 역전파)

### 2. 문서 청킹
- 200자 단위로 문서 분할
- 50자 오버랩으로 연속성 보장

### 3. 임베딩 생성
- OpenAI `text-embedding-3-small` 모델 사용
- SQLite 데이터베이스에 임베딩 벡터 저장

### 4. 검색 품질 테스트
- 5가지 질문으로 검색 품질 확인
- 코사인 유사도 기반 벡터 검색
- 검색 응답시간 측정

### 5. 성능 측정
- 50회 반복 성능 테스트
- 평균/최소/최대/P95 응답시간 측정
- 전체 처리량 통계

## 출력 결과

테스트 완료 후 다음 메트릭들이 표시됩니다:

- **Total Chunks**: 총 청크 수
- **Embedded Chunks**: 임베딩 생성된 청크 수  
- **Average Response Time**: 평균 응답시간
- **Min Response Time**: 최소 응답시간
- **Max Response Time**: 최대 응답시간
- **P95 Response Time**: 95퍼센타일 응답시간
- **Embedding Model**: 사용된 임베딩 모델
- **Vector Dimensions**: 벡터 차원수 (1536)

## 주요 특징

- **실제 API 호출**: 모킹 없이 실제 OpenAI API 사용
- **SQLite 저장소**: 가벼운 로컬 데이터베이스
- **자동 청킹**: 200자 단위 + 50자 오버랩
- **코사인 유사도**: 벡터 검색 품질 측정
- **성능 벤치마크**: 실제 사용 상황 모방

## 설정

`appsettings.json`에서 다음 설정들을 조정할 수 있습니다:

```json
{
  "OpenAI": {
    "EmbeddingModel": "text-embedding-3-small"
  },
  "TestSettings": {
    "TestQueries": [
      "What is machine learning?",
      "How does neural network work?",
      "Explain gradient descent",
      "What are transformers in AI?",
      "How to implement backpropagation?"
    ]
  }
}
```