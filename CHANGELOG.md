# Changelog

All notable changes to FluxIndex packages are documented here.
Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) conventions.

---

## [Unreleased]

---

## [0.20.2] - 2026-07-24

### Fixed
- **SQLite-vec: writes silently broke after the effective embedding fingerprint drifted on a
  latched store instance** — once `SQLiteVecVectorStore.EnsureInitializedAsync` succeeded it
  short-circuited on `_initialized` alone, so a later fingerprint change (e.g. a `BindIdentity`
  in another scope mutating the shared `SQLiteVecOptions`) left subsequent writes targeting a
  `chunk_embeddings_{fingerprint}` table that was never created (`no such table`). The store now
  tracks the table name captured at init and re-initializes when the current effective name
  diverges, creating the new vec0 table (`CREATE VIRTUAL TABLE IF NOT EXISTS`) before writing.
  Regression guard: `SQLiteVecBindIdentityDriftTests`.

### Docs
- `SQLiteVecOptions.EmbeddingFingerprint` doc corrected: a null fingerprint throws
  `InvalidOperationException` from `GetVecTableName()` — there is no automatic
  `chunk_embeddings_{dimension}` fallback (the comment contradicted the throw contract).

---

## [0.20.1] - 2026-07-22

### Fixed
- **EntityGraph (PostgreSQL): `EnsureCreated` failed whenever `EmbeddingDimension > 0`** —
  the entity/community `Embedding` columns were mapped as dimensionless `vector`, which pgvector
  rejects for any vector index ("column does not have dimensions"). Columns now declare
  `vector(EmbeddingDimension)`. Latent since the ivfflat era; exposed by the new schema
  integration tests.
- EntityGraph vector indexes converted **ivfflat → HNSW** (entity + community), matching the main
  vector store: ivfflat trains centroids at CREATE INDEX time, so an index created on an empty
  table silently loses recall for data inserted afterwards. `EntityGraphOptions.IvfflatLists` is
  now `[Obsolete]` and has no effect (removal in a future minor).

### Removed
- Expired `NU1903` (CVE-2025-6965) build suppression — SQLitePCLRaw 2.1.12 has shipped and src
  projects pin it directly; restore is warning-clean without it.

---

## [0.20.0] - 2026-07-21

### Added
- **Multi-value (MatchAny) metadata filters** across every `IVectorStore` implementation: a
  collection-valued filter entry (`List<string>`, arrays, JSON arrays) now matches when the chunk's
  metadata value equals ANY element — Qdrant `Match.Keywords`, PostgreSQL per-element jsonb `@>`
  OR-combined (each branch GIN-indexable), in-memory stores via the shared backstop. One query
  replaces the N-way per-value fan-out consumers previously had to run
  (`filters: new() { ["document_id"] = fileHashes }`).
- `VectorStoreBase.ExpandFilterValue` / `VectorStoreBase.ValidateFilters` — public helpers that
  define and enforce the filter-value contract for store implementations.
- Shared filter-contract regression suite (`VectorStoreFilterContractSuite`) run against InMemory,
  SQLite, and SQLite-quantized stores; PostgreSQL/Qdrant cover the same cases in their own suites.

### Changed (behavioral)
- **Unsupported filter values now throw `ArgumentException`** at call time instead of silently
  matching nothing. Previously e.g. a `List<string>` filter value degraded to its `ToString()`
  type name and returned zero results with no signal; empty collections, nested collections, and
  arbitrary objects are rejected loudly. Validation is eager (at `SearchAsync` call), not deferred
  to result enumeration.

### Fixed
- `PostgreSQLQuantizedVectorStore.SearchAsync` and `SQLiteQuantizedVectorStore.SearchAsync`
  silently **ignored the `filters` parameter entirely**, leaking chunks across filter scope
  (e.g. other tenants). Both now apply the shared match semantics before the topK trim.

---

## [0.16.0] - 2026-07-02

### Changed (BREAKING)

- **`FluxIndex.Extensions.FileVault` extracted to the [FluxFeed](https://github.com/iyulab/FluxFeed) repository.** File-to-vector synchronization (git-like file tracking, folder monitoring, background ingestion) is now the FluxFeed document-pipeline surface (④b), which feeds into FluxIndex (④a). The `FluxIndex.Extensions.FileVault` package is no longer published from the FluxIndex family (family: 12 → 11 packages). The public API surface (`IVault`, `AddFileVaultWithFluxIndex`, etc.) is preserved, so consumer migration is a package-id + namespace swap (`FluxIndex.Extensions.FileVault` → `FluxFeed`), not an API rewrite. See [docs/FILEVAULT_GUIDE.md](./docs/FILEVAULT_GUIDE.md) for the migration note.

---

## [0.15.0] - 2026-06-29

### Added
- `FluxIndex.Extensions.FileVault` (MU-2): **terminal-await for background memorize**. The facade previously
  discarded the queued job id and returned an early-stage `VaultEntry`, so consumers in background mode polled
  entry stage / queue status to know when memorize actually finished ("success lie"). Two additive members:
  - `IVaultQueueService.WaitForJobAsync(jobId, ct)` — signal-driven (no polling) wait that resolves on the
    Completed/Failed/Cancelled transition and immediately for an already-terminal job (race-free).
  - `IVault.MemorizeAsync(filePath, bool waitForCompletion, ct)` — when `true`, awaits terminal completion and
    returns the entry at its Memorized stage; a failed/cancelled job surfaces as an exception rather than a
    silently-incomplete entry. `false` is identical to the existing single-arg overload (zero regression).
  Reported via umbrella MU-2 (rule-of-three: AIMS, Filer, textree all hand-rolled completion polling).

## [0.13.19] - 2026-06-10

### Fixed
- `FluxIndex.Extensions.FileVault`: a removed entry could persist in `ListAsync(null)` indefinitely after a preceding hybrid `SearchAsync`. Root cause: `VaultEntry.Load`/`SaveMetadata` opened `meta.json` without `FileShare.Delete`, so a concurrent `ListAsync` enumeration read blocked the background remove job's `Directory.Delete` (Windows `ERROR_SHARING_VIOLATION`), leaving the entry directory on disk (and growing it unboundedly). Now opened with `FileShare.ReadWrite | FileShare.Delete`, and `VaultStorageService.DeleteEntryStorageAsync` retries the directory delete (5×, 100 ms backoff) to absorb the residual `RemoveDirectory` race and transient foreign locks. Entries stuck in `RemovalPartial` from before the fix self-heal via `RecoverPartialRemovalsAsync` on next host start. Reported by Filer (golden gate `SC-RAG-1`).

---

## [0.13.15] - 2026-05-28

### Fixed
- `FluxIndex.Extensions.FileVault`: `VaultBackgroundService` — replaced polling `Task.Delay` loop with event-driven wake signal via `IVaultQueueService.JobEnqueued`. Job scheduling latency drops from 5–10s (idle poll interval) to < 1ms after enqueue.

### Changed
- `FluxIndex.Extensions.FileVault.Tests`: Added `[Trait("Category", "Integration")]` to `FileVaultPipelineSimulationTests` and `VaultSubfolderScenariosTests` — these were missing the trait despite living in the `Integration/` folder, causing them to run with unit tests.

---

## [0.13.14] - 2026-05-22

### Added
- `FluxIndex.Providers.OpenAI`: `WellKnownOpenAIModels` — static lookup for 30+ model embedding dimensions
- `FluxIndex.Providers.OpenAI`: `OpenAICompatibleEmbeddingService(endpoint, apiKey, model, logger)` constructor — auto-resolves dimension for well-known models
- `FluxIndex.Providers.OpenAI`: `AddOpenAICompatibleEmbedding(endpoint, apiKey, model)` DI overload — no dimension required for well-known models

### Changed
- `build-and-release.yml`: Pack step now uses `dotnet pack FluxIndex.slnx` (solution-wide) instead of per-project loop — ensures all family packages are published together at the same version
- `build-and-release.yml`: `version_check` step now uses correct `fluxindex.sdk` package ID (was `fluxindex` which doesn't exist on NuGet)

### Docs
- Added `CHANGELOG.md` (historical breaking changes from 0.2.x → 0.13.x)
- Added `docs/MIGRATION.md` (step-by-step upgrade guide for 0.2.x → 0.13.x consumers)
- Updated `docs/AI_PROVIDER_INTEGRATION.md` with `FluxIndex.Providers.OpenAI` official package usage
- Updated `docs/README.md` with MIGRATION.md quick link

---

## [0.13.12] - 2026-05-22

### Changed
- `FluxIndex.Core`: Remove `TokenMeter.Abstractions` dependency — `ITokenCounter` is now defined locally

---

## [0.13.10] - 2026-04-xx

### Fixed
- `FluxIndex.Core`: Remove unnecessary `FileFlux` dependency

### Added
- `FluxIndex.Extensions.FileVault`: `IVault.RemoveAsync(IEnumerable<string>)` batch overload

---

## [0.13.7] - 2026-03-xx

### Fixed
- `FluxIndex.Storage.SQLite`: Clean legacy-fingerprint vec0 orphans on delete and startup sweep
- `FluxIndex.Storage.SQLite`: Pass `CancellationToken` correctly to `ExecuteSqlRawAsync`

---

## [0.13.3] - 2026-03-xx

### Changed
- `FluxIndex.Core`: All `[LoggerMessage]` strings translated from Korean to English (ASCII-only)
- Added `LogLanguageConventionTests` regression test to prevent Korean log string regressions

---

## [0.13.0] - 2026-02-xx

### Added
- `ProcessingStage.Error` — new terminal error stage for vault pipeline
- `VaultStatus`: `RefinedCount`, `StaleCount`, `ErrorStageCount` counters
- `IVault.GetErrorEntriesAsync` — query entries in Error stage

---

## [0.12.0] - 2026-02-xx

### Added
- `EmbeddingIdentity` / `ModelFingerprint` for model-aware vector collection naming
- `IVectorStoreManager` interface with collection listing support
- **Require `EmbeddingFingerprint` for vec table naming** (breaking for custom `IEmbeddingService` implementations that do not return a stable model name)

---

## [0.11.0] — 2026-03-20

### Removed (BREAKING)
- `FluxIndex.SDK`: `AddOpenAIEmbedding()`, `AddAzureOpenAIEmbedding()` extension methods removed.
  These were no-ops in prior versions. Use `FluxIndex.Providers.OpenAI` package instead:

  ```csharp
  // Before (0.10.x, was already a no-op)
  services.AddOpenAIEmbedding(apiKey);

  // After (0.11.0+)
  // Install: dotnet add package FluxIndex.Providers.OpenAI
  services.AddOpenAICompatibleEmbedding(
      "https://api.openai.com/v1", apiKey, "text-embedding-3-small", dimension: 1536);
  ```

### Changed
- `FluxIndex.SDK`: Storage provider registration decoupled from SDK (was already separate but stubs removed)

---

## [0.10.1] - 2026-02-xx

### Added
- Dimension-aware vault + SQLite-vec table naming (breaking if using raw `IVectorStore` without dimension)

---

## [0.9.0] - 2026-01-xx

### Added
- `FluxIndex.Providers.OpenAI` — new package for OpenAI-compatible embedding and reranking
  - `OpenAICompatibleEmbeddingService(endpoint, apiKey, model, dimension, logger)`
  - `AddOpenAICompatibleEmbedding(endpoint, apiKey, model, dimension)` DI extension
  - `OpenAICompatibleRerankerService` + `AddOpenAICompatibleReranker(endpoint, apiKey, model)` DI extension
- `FluxIndex.Providers.LMSupply` — new package for LMSupply local embedding and reranking

---

## [0.6.0] - 2025-xx-xx

### Added
- SQLite Entity Graph Store for local GraphRAG
- Unified storage provider architecture (auto-maximize)

---

## [0.4.0] — 2025-12-15

### Removed (BREAKING)
**Package consolidation — several packages were merged or renamed.**

#### Removed packages
| Old package | Replacement |
|-------------|-------------|
| `FluxIndex.AI.OpenAI` | `FluxIndex.Providers.OpenAI` (added in 0.9.0) |
| `FluxIndex.AI.Anthropic` | Implement `IEmbeddingService` directly in your app |
| `FluxIndex.AI.Google` | Implement `IEmbeddingService` directly in your app |
| `FluxIndex.AI.Local` | Merged into `FluxIndex.SDK` |
| `FluxIndex.Extensions.FileFlux` | Merged into `FluxIndex.SDK` |
| `FluxIndex.Extensions.FluxCurator` | Merged into `FluxIndex.SDK` |
| `FluxIndex.Extensions.FluxImprover` | Merged into `FluxIndex.SDK` |
| `FluxIndex.Extensions.WebFlux` | Merged into `FluxIndex.SDK` |

#### Namespace changes (SDK extensions)
| Old namespace | New namespace |
|---------------|---------------|
| `FluxIndex.Extensions.WebFlux` | `FluxIndex.SDK.Extensions.WebFlux` |
| `FluxIndex.Extensions.FileFlux` | `FluxIndex.SDK.Extensions.FileFlux` |

#### Builder API changes
- `FluxIndexContextBuilder.UseOpenAI(apiKey, model)` — removed
- `FluxIndexContextBuilder.UseAzureOpenAI(endpoint, apiKey, model)` — removed
- `FluxIndexContextBuilder.UseLocalAI()` — still available (ONNX local model)

  Migration:
  ```csharp
  // Before (0.2.x)
  var ctx = FluxIndexContext.CreateBuilder()
      .UseLocalStorage("index.db")
      .UseOpenAI(apiKey, "text-embedding-3-small")
      .Build();

  // After (0.9.0+, using FluxIndex.Providers.OpenAI)
  var ctx = FluxIndexContext.CreateBuilder()
      .UseLocalStorage("index.db")
      .UseEmbeddingService(new OpenAICompatibleEmbeddingService(
          "https://api.openai.com/v1", apiKey, "text-embedding-3-small", 1536, logger))
      .Build();

  // Or with DI
  services.AddOpenAICompatibleEmbedding(
      "https://api.openai.com/v1", apiKey, "text-embedding-3-small", dimension: 1536);
  ```

---

## [0.3.1] - 2025-xx-xx

### Changed
- Namespace reorganization: `FluxIndex.Domain.Entities` → `FluxIndex.Core.Domain.Entities`

  **Migration:**
  ```csharp
  // Before (0.2.x)
  using FluxIndex.Domain.Entities;

  // After (0.3.x+)
  using FluxIndex.Core.Domain.Entities;
  ```

  Affected types: `DocumentChunk`, `Document`, `SearchResult` and all other domain entities.

---

## [0.2.16] - 2025-xx-xx

Last version with:
- `FluxIndex.Domain.Entities` namespace (use `FluxIndex.Core.Domain.Entities` in 0.3.x+)
- `FluxIndex.AI.OpenAI` package (use `FluxIndex.Providers.OpenAI` in 0.9.x+)
- `FluxIndex.Extensions.WebFlux` separate package (merged into `FluxIndex.SDK` in 0.4.0)
- `UseOpenAI()` / `UseAzureOpenAI()` builder methods (removed in 0.4.0)

---

## [0.2.x] - 2025

Initial public versions. Feature development.
