# Migration Guide

Step-by-step upgrade checklists for FluxIndex consumers.

---

## Migrating from 0.2.x to 0.13.x

This covers the most common upgrade path. See [CHANGELOG.md](../CHANGELOG.md) for the full version history.

### Quick Summary of Breaking Changes

| Version | Change |
|---------|--------|
| 0.3.1 | `FluxIndex.Domain.Entities` → `FluxIndex.Core.Domain.Entities` (namespace) |
| 0.4.0 | `FluxIndex.AI.OpenAI`, `FluxIndex.Extensions.WebFlux` packages removed |
| 0.4.0 | `UseOpenAI()` / `UseAzureOpenAI()` builder methods removed |
| 0.9.0 | `FluxIndex.Providers.OpenAI` introduced (new pattern) |
| 0.11.0 | `AddOpenAIEmbedding()` / `AddAzureOpenAIEmbedding()` removed from SDK |
| 0.19.0 | `Build()` throws when a provider is selected with `Use*` but never registered with `Add*Storage()` |

---

### Step 1: Fix namespace references

Replace `FluxIndex.Domain.Entities` with `FluxIndex.Core.Domain.Entities` in all files:

```diff
- using FluxIndex.Domain.Entities;
+ using FluxIndex.Core.Domain.Entities;
```

Affected types: `DocumentChunk`, `Document`, `SearchResult`, and other domain entities.

**Tip:** Use your IDE's global find-and-replace on `using FluxIndex.Domain.Entities`.

---

### Step 2: Replace `FluxIndex.AI.OpenAI` package

The `FluxIndex.AI.OpenAI` NuGet package no longer exists. Replace with `FluxIndex.Providers.OpenAI`:

```bash
# Remove old package
dotnet remove package FluxIndex.AI.OpenAI

# Add new package
dotnet add package FluxIndex.Providers.OpenAI
```

---

### Step 3: Replace `UseOpenAI()` / `UseAzureOpenAI()` builder methods

These builder methods were removed in 0.4.0.

**Direct construction:**
```csharp
// Before (0.2.x)
var ctx = FluxIndexContext.CreateBuilder()
    .UseLocalStorage("index.db")
    .UseOpenAI(apiKey, "text-embedding-3-small")
    .Build();

// After (0.9.0+)
using FluxIndex.Providers.OpenAI.Services;
using FluxIndex.Storage.SQLite;

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger = loggerFactory.CreateLogger<OpenAICompatibleEmbeddingService>();

var ctx = FluxIndexContext.CreateBuilder()
    .UseLocalStorage("index.db")
    .AddSQLiteStorage()
    .UseEmbeddingService(new OpenAICompatibleEmbeddingService(
        endpoint: "https://api.openai.com/v1",
        apiKey: apiKey,
        model: "text-embedding-3-small",
        dimension: 1536,
        logger: logger))
    .Build();
```

**With Dependency Injection (recommended):**
```csharp
// Before (0.2.x)
services.AddOpenAIEmbedding(apiKey);

// After (0.9.0+)
using FluxIndex.Providers.OpenAI.Extensions;

services.AddOpenAICompatibleEmbedding(
    endpoint: "https://api.openai.com/v1",
    apiKey: apiKey,
    model: "text-embedding-3-small",
    dimension: 1536);
```

**Common embedding dimensions:**
| Model | Dimension |
|-------|-----------|
| `text-embedding-3-small` | 1536 |
| `text-embedding-3-large` | 3072 |
| `text-embedding-ada-002` | 1536 |
| `qwen3-embedding-0.6b` | 1024 |

---

### Step 4: Replace `FluxIndex.Extensions.WebFlux` package

This package was merged into `FluxIndex.SDK`.

```bash
dotnet remove package FluxIndex.Extensions.WebFlux
# FluxIndex.SDK is already included as a transitive dependency
```

Update namespace references:

```diff
- using FluxIndex.Extensions.WebFlux;
+ using FluxIndex.SDK.Extensions.WebFlux;
```

---

### Step 5: Replace `AddOpenAIEmbedding()` / `AddAzureOpenAIEmbedding()` (if present)

If you were calling these SDK methods directly, they were removed in 0.11.0 (they were no-ops in 0.10.x). Use the `FluxIndex.Providers.OpenAI` DI extension instead (Step 3 above).

---

### Step 6: Verify EmbeddingIdentity (0.12.0+)

If you use a custom `IEmbeddingService`, ensure `GetModelName()` returns a stable, unique string. Starting from 0.12.0, model name is used for vector collection naming (`EmbeddingFingerprint`). Returning an empty or null string will cause `ArgumentException` at indexing time.

---

### Step 7: Register the storage provider you select (0.19.0+)

`Use*` only sets options — the matching `Add*Storage()` extension from the storage package is what
registers the store. Earlier versions silently fell back to an in-memory store when the registration
was missing, so the application ran normally and lost its whole index on restart. From 0.19.0
`Build()` throws an `InvalidOperationException` naming the missing call instead.

```diff
  var ctx = FluxIndexContext.CreateBuilder()
      .UseLocalStorage("index.db")
+     .AddSQLiteStorage()
      .Build();
```

Add the extension matching each provider you select:

| Selection | Registration | Package |
|-----------|--------------|---------|
| `UsePostgreSQL(conn)` | `AddPostgreSQLStorage()` | `FluxIndex.Storage.PostgreSQL` |
| `UseSQLite(path)` / `UseSQLiteInMemory()` / `UseLocalStorage(path)` | `AddSQLiteStorage()` | `FluxIndex.Storage.SQLite` |
| `UseQdrant(...)` / `UseQdrantFixed(...)` / `UseQdrantCloud(...)` / `UseQdrantCloudFixed(...)` | `AddQdrantStorage()` | `FluxIndex.Storage.Qdrant` |
| `UseRedisCache(conn)` | `AddRedisStorage()` | `FluxIndex.Cache.Redis` |

`UseMemoryCache()` and builders that select no provider at all need nothing extra — the in-memory
fallback is the intended behaviour there.

---

### Step 8: PostgreSQL auto-initializes its schema on Build() (0.19.0+)

`AddPostgreSQLStorage()` now creates the pgvector extension and vector tables on `Build()` — symmetric
with SQLite, which has always self-initialized. Earlier versions left PostgreSQL uninitialized, so
consumers had to run `CREATE EXTENSION vector` + `EnsureCreated` by hand. That manual step can now be
removed.

Opt out when you manage the schema externally (EF migrations, an ops-owned schema) or run on managed
PostgreSQL where the connecting role lacks `CREATE EXTENSION` privilege **and** the extension is not
pre-installed — the one case auto-init throws (`CREATE EXTENSION IF NOT EXISTS` is a privilege-free
no-op when the extension already exists):

```csharp
var builder = FluxIndexContext.CreateBuilder()
    .UsePostgreSQL(conn);
builder.Options.VectorStore.EnableAutoMigration = false;  // opt out of auto-init
var ctx = builder
    .AddPostgreSQLStorage()
    .Build();
```

**Sharing a database with your application (fixed in 0.21.1).** Auto-init provisions only the
relations FluxIndex owns, so pointing it at a database that already contains your own application
tables works. Through 0.21.0 it used EF's `EnsureCreated()`, which skips schema creation entirely once
the database holds *any* relation — `Build()` succeeded, nothing was created, and the first index
write failed with `42P01: relation "vectors" does not exist`. Fresh databases were unaffected, so the
failure only appeared in production. If you hit that, upgrade to 0.21.1; no code change is needed.

A half-built schema (some FluxIndex relations present, some missing) is refused at `Build()` with an
actionable exception instead of being silently half-repaired. The vector context currently owns a
single relation, so this guard cannot trigger yet — it becomes live when the context owns more than
one.

A dedicated database for the index remains a perfectly good choice — derived data has its own
lifecycle (retention, rebuild, dump/restore cadence). Sharing is supported, not recommended.

---

## Migrating from 0.11.x to 0.13.x

Only Step 6 above may apply. No breaking package changes.

---

## Azure OpenAI migration

If you were using Azure OpenAI, `OpenAICompatibleEmbeddingService` works with Azure endpoints:

```csharp
services.AddOpenAICompatibleEmbedding(
    endpoint: "https://your-resource.openai.azure.com/openai/deployments/your-deployment/v1",
    apiKey: azureApiKey,
    model: "text-embedding-3-small",
    dimension: 1536);
```

---

## Getting help

- [Full CHANGELOG](../CHANGELOG.md)
- [AI Provider Integration Guide](./AI_PROVIDER_INTEGRATION.md)
- [GitHub Issues](https://github.com/iyulab/FluxIndex/issues)
