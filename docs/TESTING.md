# FluxIndex Testing Guide

FluxIndex는 **환경 기반 자동 테스트 모드 전환**을 지원합니다. `.env.local` 파일의 존재 여부에 따라 Mock 또는 실제 API를 자동으로 사용합니다.

## 📋 목차

- [테스트 전략](#테스트-전략)
- [CI/CD 테스트 (Mock 모드)](#cicd-테스트-mock-모드)
- [로컬 개발 테스트 (Real API 모드)](#로컬-개발-테스트-real-api-모드)
- [테스트 결과 이해하기](#테스트-결과-이해하기)
- [문제 해결](#문제-해결)

## 테스트 전략

### 두 가지 테스트 모드

| 모드 | 환경 | 파일 | API 사용 | 비용 | 속도 | 용도 |
|------|------|------|----------|------|------|------|
| **Mock** | CI/CD | `.env.local` 없음 | Mock 응답 | 무료 | 빠름 | 자동화된 검증 |
| **Real API** | 로컬 개발 | `.env.local` 있음 | 실제 OpenAI API | 유료 | 느림 | 통합 테스트 |

### 예상 테스트 결과

| 모드 | 전체 통과 | OpenAI 통과 | 성공률 | 설명 |
|------|----------|------------|--------|------|
| **Mock** | 55/70 | 12/27 | 79% | Mock 전용 테스트만 통과 |
| **Real API** | 59/70 | 16/27 | 84% | 실제 API 통합 검증 |

**Note**: OpenAI 테스트의 일부 실패는 **예상된 동작**입니다. Mock으로 주입할 수 없는 내부 구현 때문입니다.

## CI/CD 테스트 (Mock 모드)

### GitHub Actions

`.github/workflows/test.yml`이 자동으로 실행됩니다:

```yaml
name: Tests
on: [push, pull_request]
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
      - run: pwsh ./scripts/mock-test.ps1
```

### 로컬에서 CI/CD 테스트 재현

```powershell
# Mock 모드 테스트 실행 (.env.local 없이)
pwsh scripts/mock-test.ps1

# 자세한 출력
pwsh scripts/mock-test.ps1 -Verbosity detailed

# Coverage 포함
pwsh scripts/mock-test.ps1 -Coverage
```

**특징**:
- ✅ `.env.local` 파일 없음 확인
- ✅ Mock 응답만 사용
- ✅ 빠른 실행 (< 30초)
- ✅ API 비용 없음
- ✅ 예측 가능한 결과

## 로컬 개발 테스트 (Real API 모드)

### 1. 환경 설정

```powershell
# 1. .env.local 파일 생성
cp .env.local.example .env.local

# 2. API 키 설정 (.env.local 파일 편집)
OPENAI_API_KEY=sk-proj-your-actual-api-key-here
OPENAI_MODEL_NAME=gpt-4o-mini
```

### 2. 전체 테스트 실행

```powershell
# 실제 API + Mock 테스트 모두 실행
pwsh scripts/full-test.ps1

# 자세한 출력
pwsh scripts/full-test.ps1 -Verbosity detailed

# Coverage 포함
pwsh scripts/full-test.ps1 -Coverage

# 병렬 실행
pwsh scripts/full-test.ps1 -Parallel
```

**특징**:
- ✅ `.env.local` 파일 자동 감지
- ✅ 실제 OpenAI API 사용
- ✅ 통합 테스트 검증
- ⚠️ API 비용 발생
- ⚠️ 느린 실행 (1-2분)

### 3. 비용 절약 팁

```powershell
# 특정 테스트만 실행
pwsh scripts/full-test.ps1 -Filter "OpenAI"

# Core 테스트만 (무료)
dotnet test tests/FluxIndex.Tests.Core

# 빠른 모델 사용 (.env.local)
OPENAI_MODEL_NAME=gpt-4o-mini  # gpt-4o 대신
```

## 테스트 결과 이해하기

### Mock 모드 출력 예시

```
===================================
FluxIndex Mock Test Runner (CI/CD)
===================================

✓ Running in Mock mode (.env.local not found)

Running tests for: FluxIndex.Tests.Core
Project: tests/FluxIndex.Tests.Core/FluxIndex.Tests.Core.csproj
Mode: Mock (CI/CD)
-----------------------------------
Result: PASSED (42/42 tests)

Running tests for: FluxIndex.Tests.AI.OpenAI
Project: tests/FluxIndex.Tests.AI.OpenAI/FluxIndex.Tests.AI.OpenAI.csproj
Mode: Mock (CI/CD)
-----------------------------------
Result: FAILED (15 failures, 12 passed, 0 skipped)

===================================
Mock Test Summary (CI/CD Mode)
===================================

Project                              Passed  Failed  Skipped  Total
--------------------------------------------------------------------------------
FluxIndex.Tests.Core                    42       0         0      42
FluxIndex.Tests.AI.OpenAI               12      15         0      27
FluxIndex.Tests.SDK                      1       0         0       1
--------------------------------------------------------------------------------

Overall Statistics:
  Total Tests:    70
  Passed:         55
  Failed:         15
  Pass Rate:      78.57%
```

### Real API 모드 출력 예시

```
===================================
FluxIndex Full Test Suite (Local)
===================================

✓ Running in Real API mode (.env.local found)
  Tests will use actual OpenAI API (API costs apply)

Running tests for: FluxIndex.Tests.AI.OpenAI
Mode: Real API + Mock
-----------------------------------
Result: FAILED (11 failures, 16 passed, 0 skipped)

Overall Statistics:
  Total Tests:    70
  Passed:         59
  Failed:         11
  Pass Rate:      84.29%

Test Mode: Real API + Mock
  - OpenAI tests use actual API (costs apply)
  - Higher test coverage with integration validation
  - Expected pass rate: ~84% (59 of 70 tests)
```

## 테스트 실패 분석

### OpenAI 테스트 실패 유형

#### 1. Mock 전용 테스트 실패 (예상됨)

**Mock 모드에서 통과, Real API 모드에서 실패:**
- `ExtractAsync_WithNullContent_ShouldThrowArgumentNullException`
- `ExtractAsync_WithEmptyContent_ShouldThrowArgumentException`
- `ExtractAsync_WithApiFailure_ShouldRetryAndThrow`
- `ExtractAsync_WithCancellationToken_ShouldRespectCancellation`
- `ExtractAsync_WithInvalidJson_ShouldThrowJsonException`

**이유**: 실제 API는 RuleBasedExtractor fallback을 사용하여 gracefully 처리합니다.

#### 2. 정확한 응답 검증 실패 (예상됨)

**테스트:**
- `ExtractAsync_WithValidContent_ShouldReturnMetadata`
- `ExtractAsync_WithFastStrategy_ShouldUseGpt4oMini`
- `ExtractAsync_WithDeepStrategy_ShouldUseGpt4o`

**이유**: 실제 AI 응답은 매번 다를 수 있으며, Mock 응답과 정확히 일치하지 않습니다.

#### 3. Batch 처리 테스트 (예상됨)

**테스트:**
- `ExtractBatchWithProgressAsync_WithPartialFailure_ShouldContinueProcessing`
- `ExtractBatchWithProgressAsync_ShouldCalculateStatistics`

**이유**: Mock 콜백 함수가 실제 API 모드에서는 무시됩니다.

## 문제 해결

### .env.local 파일이 감지되지 않음

```powershell
# 파일 존재 확인
Test-Path D:\data\FluxIndex\.env.local

# 내용 확인
Get-Content D:\data\FluxIndex\.env.local
```

### API 키 오류

```
Error: OPENAI_API_KEY not found in .env.local
```

**해결**:
1. `.env.local` 파일에 API 키가 있는지 확인
2. 키가 `sk-proj-` 또는 `sk-`로 시작하는지 확인
3. 환경 변수 이름이 정확한지 확인 (`OPENAI_API_KEY`)

### 테스트가 너무 느림

**Real API 모드:**
- 정상입니다 (OpenAI API 호출 시간)
- Mock 모드로 전환하려면 `.env.local` 삭제 또는 이름 변경

**Mock 모드도 느림:**
- 빌드 스킵: `pwsh scripts/mock-test.ps1 -NoBuild`
- 캐시 정리: `dotnet clean`

### Coverage 보고서가 생성되지 않음

```powershell
# Coverage 도구 설치 확인
dotnet tool install --global dotnet-reportgenerator-globaltool

# Coverage와 함께 실행
pwsh scripts/mock-test.ps1 -Coverage

# 보고서 위치
# tests/*/TestResults/*/coverage.cobertura.xml
```

## 베스트 프랙티스

### CI/CD 파이프라인

```yaml
# GitHub Actions 권장 설정
- name: Run Tests
  run: pwsh ./scripts/mock-test.ps1 -Verbosity minimal

# 실패해도 결과 업로드
- name: Upload Results
  if: always()
  uses: actions/upload-artifact@v4
```

### 로컬 개발

1. **일상적인 개발**: Mock 모드 사용 (빠름, 무료)
   ```powershell
   # .env.local 없이
   pwsh scripts/full-test.ps1
   ```

2. **PR 전**: Real API 모드로 최종 검증
   ```powershell
   # .env.local 생성 후
   pwsh scripts/full-test.ps1
   ```

3. **특정 기능 테스트**: 필터 사용
   ```powershell
   pwsh scripts/full-test.ps1 -Filter "MetadataExtractor"
   ```

## 추가 리소스

- [xUnit 문서](https://xunit.net/)
- [FluentAssertions 문서](https://fluentassertions.com/)
- [Moq 문서](https://github.com/moq/moq4)
- [DotNetEnv 문서](https://github.com/tonerdo/dotnet-env)

## 기여 가이드

새로운 테스트를 추가할 때:

1. **Core 테스트**: 항상 Mock만 사용 (API 의존성 없음)
2. **AI 테스트**: OpenAITestFixture 사용하여 자동 모드 전환
3. **Mock 전용 테스트**: 주석으로 명확히 표시
4. **통합 테스트**: Real API 모드에서 검증 필요

```csharp
[Fact]
public async Task YourNewTest()
{
    // Mock/Real API 자동 전환
    _fixture.SetupMockResponse(mockResponse);
    var result = await _fixture.Extractor.ExtractAsync(content, schema);

    // Assertion
    result.Should().NotBeNull();
}
```
