# FluxIndex Stack Session State

**Last Updated**: 2025-12-07
**Session Focus**: Settings UI to SDK Integration (Option A)

## Completed Work

### 1. Dynamic Embedding Provider Integration
Settings UI에서 설정한 AI Provider가 실제 SDK 임베딩 서비스와 연동되도록 구현 완료.

**Created Files**:
- `src/FluxIndex.Stack.Application/Interfaces/Services/IEmbeddingServiceFactory.cs`
- `src/FluxIndex.Stack.Application/Interfaces/Services/IEmbeddingProviderCache.cs`
- `src/FluxIndex.Stack.Infrastructure/Services/EmbeddingServiceFactory.cs`
- `src/FluxIndex.Stack.Infrastructure/Services/DynamicEmbeddingProvider.cs`

**Modified Files**:
- `src/FluxIndex.Stack.Infrastructure/Extensions/ServiceCollectionExtensions.cs` - DI 등록 추가
- `src/FluxIndex.Stack.Api/Controllers/SettingsController.cs` - 새 엔드포인트 추가

**New API Endpoints**:
- `GET /api/v1/settings/ai/embedding/status` - 현재 임베딩 프로바이더 상태
- `POST /api/v1/settings/ai/embedding/refresh` - 캐시 수동 새로고침

### 2. Architecture Flow
```
Settings UI → DB (AiProviderSettings) → DynamicEmbeddingProvider
                                              ↓
                              ┌───────────────┴───────────────┐
                              │                               │
                        API Key 있음                    API Key 없음
                              │                               │
                              ↓                               ↓
                   OpenAI/Azure Provider            LocalEmbedder
                   (text-embedding-3-small)         (all-MiniLM-L6-v2)
```

### 3. DI Lifetime Issue Resolution
- **Problem**: Singleton `DynamicEmbeddingProvider` → Scoped `IAiProviderSettingsRepository`
- **Solution**: `IServiceScopeFactory` 패턴 적용

## Verified Working

| Component | Status | Notes |
|-----------|--------|-------|
| Build | ✅ | 0 errors, 2 warnings (NU1510) |
| Embedding Status API | ✅ | LocalEmbedder fallback 정상 |
| Document Upload | ✅ | Content 저장됨 |
| Content Storage | ✅ | `content/{shard}/{id}.txt` 생성 |

## Known Issues (Pending Resolution)

### 1. Background Indexing Service Not Processing
- **Symptom**: Job이 "Queued" 상태에서 진행되지 않음
- **Document Status**: "Processing" (서비스가 시작은 했으나 멈춤)
- **Likely Cause**: Hot reload 후 BackgroundService 중단
- **Resolution**: API 서버 재시작 필요

### 2. Original Test Document (chunkCount: 0)
- **Document ID**: `156ff523-6a7d-4a09-8ac3-0cf770959635`
- **Issue**: Content가 저장되지 않은 상태로 업로드됨
- **Resolution**: 삭제 후 재업로드 권장

## Test Data Created

| Item | Path/ID | Notes |
|------|---------|-------|
| Test Document | `5013a6f5-fa64-4652-9346-63db95f224ce` | Content 저장됨, Job Queued |
| Content File | `content/50/5013a6f5-...txt` | 1509 bytes |

## Next Steps

1. **Immediate**: API 서버 재시작하여 BackgroundService 복구
2. **Verify**: Job 처리 및 Chunk 생성 확인
3. **Test**: LocalEmbedder로 실제 임베딩 생성 확인
4. **Optional**: OpenAI API Key 설정 후 외부 프로바이더 테스트

## Configuration Reference

```json
// appsettings.json - Content Storage
{
  "FluxIndex": {
    "Content": {
      "StoragePath": "./content"
    }
  }
}
```

## API Quick Reference

```bash
# Check embedding provider status
curl http://localhost:5000/api/v1/settings/ai/embedding/status

# Refresh embedding provider cache
curl -X POST http://localhost:5000/api/v1/settings/ai/embedding/refresh

# Upload document (dev mode - no API key needed)
curl -X POST http://localhost:5000/api/v1/documents/upload \
  -F "title=Document Title" \
  -F "file=@path/to/file.txt"

# Check job status
curl http://localhost:5000/api/v1/jobs/{jobId}

# Check job summary
curl http://localhost:5000/api/v1/jobs/summary
```
