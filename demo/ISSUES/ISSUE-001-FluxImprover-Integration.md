# ISSUE-001: FluxImprover Integration for QA Generation and Metadata Enrichment

## Summary
Integrate FluxImprover's LLM-powered capabilities into FluxIndex Demo for automatic QA generation, chunk enrichment, and RAG quality evaluation.

## Current State (Updated: 2024-12-05)

### ✅ Completed

| Feature | Backend | Frontend | Notes |
|---------|---------|----------|-------|
| QA Generation API | ✅ | ✅ | Streaming via ProcessStreamAsync |
| Auto-Generate QA Button | ✅ | ✅ | In DocumentDetailModal Q&A tab |
| Process Logs | ✅ | ✅ | LogsPage with real-time updates |
| LLM Model Display | ✅ | ✅ | Sidebar shows completion model |
| RAG Evaluation API | ✅ | 🔄 | Backend complete, frontend types added |

### 🔄 In Progress

| Feature | Status | Next Steps |
|---------|--------|------------|
| Evaluate QA Button | Frontend types done | Add useEvaluateDocumentQA hook, then UI button |

### ⏸️ Blocked

| Feature | Blocker | Resolution |
|---------|---------|------------|
| Chunk Enrichment | FluxImprover DI | Create issue for ISummarizationService/IKeywordExtractionService |

## API Endpoints

```
POST /api/documents/{id}/generate-qa     ✅ Streaming QA generation
POST /api/chunks/{id}/evaluate-qa        ✅ Single QA evaluation
POST /api/documents/{id}/evaluate-qa     ✅ Document QA evaluation
GET  /api/logs                           ✅ Process logs
DELETE /api/logs                         ✅ Clear logs
```

## DI Registration (Program.cs)

```csharp
// ✅ Registered
services.AddSingleton<QAGeneratorService>();
services.AddSingleton<QAFilterService>();
services.AddSingleton<QAPipeline>();
services.AddQAGeneration();
services.AddRAGEvaluation();
services.AddSingleton<ParallelPipelineExecutor>();
services.AddSingleton<ProcessLogService>();
```

## Acceptance Criteria

- [x] QA can be auto-generated from chunk content (streaming)
- [x] Progress logs displayed in real-time
- [ ] Chunks can be enriched with LLM-generated summary/keywords (blocked)
- [x] RAG quality can be evaluated (backend done, UI pending)
- [x] Manual editing still available (Monaco Editor)
- [x] No duplicate logic - all ML operations delegated to FluxImprover

## Identified Library Issues

### FluxImprover (External Package)
| Issue | Description | Priority |
|-------|-------------|----------|
| DI for Enrichment | Need `AddChunkEnrichment()` with ISummarizationService/IKeywordExtractionService | High |
| MetricResult.Reasoning | MetricResult only has Score, no Reasoning property | Low |

### FluxIndex.Extensions.FluxImprover
| Issue | Description | Priority |
|-------|-------------|----------|
| Missing AddEnrichment | ChunkEnrichmentServiceWrapper needs underlying services | Medium |

## Resume Point

Next session should:
1. Add `useEvaluateDocumentQA` hook to `useApi.ts`
2. Add "Evaluate QA" button to DocumentDetailModal
3. Create FluxImprover GitHub issues for DI improvements

## Labels
enhancement, fluxindex-demo, fluxindex-ui, fluxindex-extensions, fluxindex-library-issues
