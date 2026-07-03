# FileVault → Moved to FluxFeed

**As of FluxIndex 0.16.0, `FluxIndex.Extensions.FileVault` has been extracted to the [FluxFeed](https://github.com/iyulab/FluxFeed) repository.**

File-to-vector synchronization (git-like file tracking, folder monitoring, background ingestion) is now the responsibility of the **FluxFeed** document-pipeline surface, which feeds parsed and cleaned content into FluxIndex for indexing.

## What changed

- The `FluxIndex.Extensions.FileVault` NuGet package is no longer published from the FluxIndex family (family shrank from 12 to 11 packages at 0.16.0).
- The public API surface (`IVault`, `AddFileVaultWithFluxIndex`, etc.) is preserved — migration for existing consumers is a **package-id + namespace swap**, not an API rewrite.

## Migration

| Before | After |
|--------|-------|
| `dotnet add package FluxIndex.Extensions.FileVault` | `dotnet add package FluxFeed` |
| `using FluxIndex.Extensions.FileVault;` | `using FluxFeed;` |

For the full guide, configuration, and usage patterns, see the **[FluxFeed repository](https://github.com/iyulab/FluxFeed)**.
