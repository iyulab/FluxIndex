# FluxIndex CI/CD

## 🚀 단순화된 CI/CD 워크플로우

FluxIndex는 **버전 기반 자동 배포** 시스템을 사용합니다.

### 📦 새 버전 배포 방법

1. **버전 업데이트**: `Directory.Build.props` 파일의 `<Version>` 태그 수정
2. **커밋 & 푸시**: main 브랜치에 변경사항 푸시
3. **자동 실행**: CI/CD가 자동으로 빌드, 테스트, NuGet 배포, GitHub 릴리즈 생성

### 🔄 워크플로우 특징

- **트리거**: `Directory.Build.props` 파일 변경 시에만 실행
- **전체 프로세스**: Build → Test → Package → Publish → Release
- **중복 방지**: 동일 버전이 NuGet에 존재하면 스킵
- **수동 실행**: workflow_dispatch로 강제 배포 가능

### 📋 주요 장점

- ✅ **단순함**: 단일 워크플로우 파일
- ✅ **효율성**: 버전 변경 시에만 실행
- ✅ **안전성**: 중복 배포 방지
- ✅ **투명성**: 상세한 빌드 요약 제공

### 🛠️ 워크플로우 파일

- `.github/workflows/build-and-release.yml` - 통합 빌드/배포 워크플로우

### 🎯 Version 관리

모든 패키지 버전은 `Directory.Build.props`에서 중앙 관리됩니다:

```xml
<Version>0.1.4</Version>
```

이 값을 변경하고 main 브랜치에 푸시하면 자동으로 CI/CD가 실행됩니다.