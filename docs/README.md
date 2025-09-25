# FluxIndex 문서

FluxIndex RAG 라이브러리의 완전한 문서 모음

## 📚 문서 목록

### 시작하기
- **[튜토리얼](tutorial.md)** - 소비 앱에서 FluxIndex를 활용하는 단계별 가이드
- **[치트시트](cheat-sheet.md)** - 빠른 참조를 위한 핵심 코드 패턴
- **[빠른 시작](getting-started.md)** - 5분만에 RAG 시스템 구축하기

### 심화 학습
- **[아키텍처 가이드](architecture.md)** - Clean Architecture와 설계 원칙
- **[RAG 시스템 가이드](FLUXINDEX_RAG_SYSTEM.md)** - RAG 구현 상세 설명

### 실습 자료
- **[샘플 코드](../samples/)** - 다양한 실전 사용 사례
  - **[IntegrationTestSample](../samples/IntegrationTestSample/)**: FileFlux/WebFlux 완전 통합 테스트
  - **[RealQualityTest](../samples/RealQualityTest/)**: RAG 품질 평가 및 성능 측정
  - **[WebFluxSample](../samples/WebFluxSample/)**: 웹 콘텐츠 처리 데모
- **[테스트 코드](../tests/)** - 단위 테스트 및 통합 테스트

## 🎯 학습 경로

### 초보자
1. [튜토리얼](tutorial.md) 1-3장: 기본 설정부터 AI 연동까지
2. [치트시트](cheat-sheet.md): 핵심 패턴 숙지
3. [IntegrationTestSample](../samples/IntegrationTestSample/): 완전한 통합 예제 실행

### 중급자
1. [튜토리얼](tutorial.md) 4-6장: 하이브리드 검색과 성능 최적화
2. [아키텍처 가이드](architecture.md): 내부 구조 이해
3. [샘플 코드](../samples/RealQualityTest/): 실전 RAG 시스템

### 고급자
1. [RAG 시스템 가이드](FLUXINDEX_RAG_SYSTEM.md): 고급 RAG 패턴
2. [테스트 코드](../tests/): 확장 및 커스터마이징
3. Core 라이브러리 직접 활용

## 🔍 주제별 가이드

### 기본 사용법
- [기본 설정](tutorial.md#1-기본-설정)
- [인덱싱과 검색](tutorial.md#2-간단한-인덱싱과-검색)
- [빠른 시작 패턴](cheat-sheet.md#-빠른-시작)

### AI Provider 연동
- [OpenAI 설정](tutorial.md#openai-설정)
- [Azure OpenAI](tutorial.md#azure-openai-사용)
- [임베딩 벡터 검색](tutorial.md#임베딩-벡터로-검색)

### 고급 검색
- [하이브리드 검색](tutorial.md#4-하이브리드-검색)
- [적응형 검색](tutorial.md#적응형-검색-권장)
- [검색 전략](cheat-sheet.md#검색-전략)

### 파일 처리
- [PDF, DOCX 처리](tutorial.md#fileflux-extension-사용)
- [웹페이지 크롤링](tutorial.md#웹-페이지-처리)
- [배치 인덱싱](tutorial.md#배치-인덱싱)

### 성능 최적화
- [Redis 캐싱](tutorial.md#캐싱-설정)
- [PostgreSQL 설정](tutorial.md#postgresql-프로덕션-설정)
- [성능 팁](cheat-sheet.md#-성능-팁)

## 💡 자주 묻는 질문

### Q: FluxIndex와 다른 RAG 라이브러리의 차이점은?
- **AI Provider 중립성**: OpenAI 외에도 커스텀 AI 서비스 지원
- **Clean Architecture**: 확장 가능한 모듈형 설계
- **하이브리드 검색**: 벡터 + 키워드 검색 결합
- **적응형 전략**: 쿼리 복잡도에 따른 자동 최적화

### Q: 어떤 저장소를 선택해야 하나요?
- **개발/테스트**: SQLite (설정 불필요)
- **프로덕션**: PostgreSQL + pgvector (확장성)
- **메모리**: InMemory (임시 사용)

### Q: AI Provider 없이 사용할 수 있나요?
네, FluxIndex.Core는 AI Provider 없이도 동작합니다:
- BM25 키워드 검색
- TF-IDF 벡터화
- 로컬 재순위화

### Q: 성능 최적화 방법은?
1. Redis 캐싱 활용
2. 배치 인덱싱 사용
3. PostgreSQL 벡터 인덱스 최적화
4. 적절한 청킹 전략 선택

## 🔗 추가 리소스

- **GitHub 저장소**: [iyulab/FluxIndex](https://github.com/iyulab/FluxIndex)
- **NuGet 패키지**: [FluxIndex.SDK](https://www.nuget.org/packages/FluxIndex.SDK/)
- **이슈 트래킹**: [GitHub Issues](https://github.com/iyulab/FluxIndex/issues)
- **CI/CD 상태**: ![Build Status](https://github.com/iyulab/FluxIndex/actions/workflows/build-and-release.yml/badge.svg)

## 📖 문서 기여

문서 개선에 기여하고 싶으시다면:
1. [GitHub 저장소](https://github.com/iyulab/FluxIndex) 포크
2. `docs/` 디렉토리 수정
3. Pull Request 제출

---

**FluxIndex와 함께 강력한 RAG 시스템을 구축해보세요!** 🚀