# FluxIndex Stack Issues Draft

Generated from investigation on 2025-12-10
**Last Updated**: 2025-12-10

---

## Issue 1: FileFlux HtmlDocumentReader not used in Stack - HTML content stored as raw text

### Status: ✅ **RESOLVED** (2025-12-10)

### Problem
When HTML files are uploaded to FluxIndex Stack, the content is stored as raw HTML with all tags, CSS styles, and inline base64 images intact. This results in:
- Single chunks with 40K+ tokens (due to CSS/script/style content)
- base64 images embedded in content (4.4MB for a document with 2 images)
- Wasted token budget on non-semantic content

### Solution Implemented
Added HTML preprocessing in `FileFluxChunkingService.cs`:
- `PreprocessContentAsync()` method detects HTML and uses `HtmlDocumentReader`
- `IsHtmlContent()` helper for HTML detection
- FileFlux v0.7+ automatically extracts base64 images to `RawContent.Images`

### Impact
- **40K tokens → ~2K tokens** per HTML document
- Better semantic chunking boundaries
- No base64 images in text content

---

## Issue 2: FluxImprover not integrated into IndexingService - Empty extracts and QA

### Status: ⏳ **PENDING** (Future enhancement)

### Problem
Indexed documents have:
- Empty `extracts` array in chunk metadata
- Empty `qa` (question-answer pairs) in chunk metadata
- No AI-powered enrichment despite FluxImprover being registered in DI

### Root Cause
`IndexingService.ProcessDocumentAsync` only performs:
1. Chunking via `IChunkingService`
2. Embedding generation via `IEmbeddingProvider`
3. Chunk persistence via `IChunkRepository`

### Preparation Completed
- `StackDocumentChunkAdapter.cs` created for `IEnrichedChunk` interface compatibility
- Ready for FluxImproverPipeline integration when LLM services are available

### Dependencies
- Requires `ITextCompletionService` (LLM service) to be configured
- Optional feature - should work without LLM when not configured

---

## Issue 3: base64 images in content should be extracted and stored separately

### Status: ✅ **RESOLVED** (2025-12-10)

### Problem
HTML documents with embedded base64 images result in:
- **4.4MB** text content (vs ~50KB without images)
- Massive token waste in embeddings
- Poor RAG retrieval quality

### Solution Implemented
**FileFlux v0.7.0** (upgraded in `Directory.Packages.props`):
- `HtmlDocumentReader.ProcessImage()` now extracts base64 images
- Images stored in `RawContent.Images` collection with binary data
- Text replaced with placeholders: `![alt](embedded:img_000)`

### Impact
- **4.4MB → ~50KB** content size (99% reduction)
- Better embedding quality
- Images accessible via `RawContent.Images` for future vision AI integration

---

## Issue 4: Delete documents not working from UI

### Status: ✅ **RESOLVED** (2025-12-10)

### Root Cause
API 키가 Admin 역할이 아닐 때 403 Forbidden 반환되나, UI에서 적절한 에러 메시지 없음

### Solution Implemented
1. `stack/frontend/src/lib/api.ts`: 403 에러 로깅 추가
2. `stack/frontend/src/pages/DocumentsPage.tsx`: 권한 부족 시 사용자 친화적 토스트 메시지

### Behavior
- Development 모드 + API 키 없음 → Admin 자동 부여 → 삭제 가능
- API 키 설정됨 + Admin 역할 → 삭제 가능
- API 키 설정됨 + Reader/Writer 역할 → 403 에러 + "Admin role required" 메시지

---

## Priority Ranking (Updated)

1. ~~**P0 (Critical)**: Issue 1 - HTML content as raw text~~ ✅ RESOLVED
2. ~~**P1 (High)**: Issue 3 - base64 images in content~~ ✅ RESOLVED
3. **P2 (Medium)**: Issue 2 - FluxImprover not integrated (future enhancement)
4. ~~**P3 (Low)**: Issue 4 - Delete UI debugging~~ ✅ RESOLVED

---

## Files Modified (2025-12-10)

### FluxIndex Stack Infrastructure
- `stack/src/FluxIndex.Stack.Infrastructure/Services/FileFluxChunkingService.cs` - HTML preprocessing
- `stack/src/FluxIndex.Stack.Infrastructure/Services/StackDocumentChunkAdapter.cs` - NEW: IEnrichedChunk adapter

### FluxIndex Stack Frontend
- `stack/frontend/src/lib/api.ts` - 403 error handling
- `stack/frontend/src/pages/DocumentsPage.tsx` - Permission denied toast

### Project Configuration
- `Directory.Packages.props` - FileFlux 0.6.2 → 0.7.0

---

## Remaining Work

### FluxImprover Integration (Issue 2)
When LLM services are available:
1. Add `IChunkEnrichmentService` interface to Application layer
2. Implement in Infrastructure using `FluxImproverPipeline`
3. Inject into `IndexingService` and call after chunking
4. Store enriched metadata (qa_pairs, summary, keywords) in chunk

```csharp
// Planned integration point in IndexingService.ProcessDocumentAsync:
if (_chunkEnrichmentService != null && settings.EnableEnrichment)
{
    await EnrichChunksAsync(chunkList, job.Id, cancellationToken);
}
```
