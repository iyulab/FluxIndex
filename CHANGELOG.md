# Changelog

All notable changes to FluxIndex packages are documented here.
Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) conventions.

---

## [Unreleased]

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
