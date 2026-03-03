# Dimension-Aware FileVault + SQLite-Vec Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** SQLite-vec 테이블을 dimension별로 분리하고 VaultEntry에 per-entry dimension 추적을 추가하여, embedding 모델 변경 시 기존 데이터 보존 및 dimension별 독립 상태 관리를 가능하게 한다.

**Architecture:** SQLiteVecVectorStore/SQLiteVecDbContext의 모든 `chunk_embeddings` 하드코딩을 `chunk_embeddings_{dim}` 동적 테이블명으로 교체. VaultEntry에 `EmbeddedDimension` 필드를 추가하여 meta.json에 영속화. 초기화 시 레거시 테이블 자동 마이그레이션.

**Tech Stack:** C# / .NET 10.0 / SQLite-vec / FluxIndex SDK

---

### Task 1: SQLiteVecOptions — dimension-based 테이블명 헬퍼

**Files:**
- Modify: `src/FluxIndex.Storage.SQLite/SQLiteVecOptions.cs:316`

**Step 1: Modify GetVecTableSchema to always use dimension suffix**

`GetVecTableSchema`의 기본 tableName 파라미터를 제거하고, dimension suffix를 자동 포함하도록 변경.

```csharp
// 라인 316-318 교체
public string GetVecTableName() => $"chunk_embeddings_{VectorDimension}";

public string GetVecTableSchema()
{
    var tableName = GetVecTableName();
    return $"CREATE VIRTUAL TABLE {tableName} USING vec0(chunk_id TEXT PRIMARY KEY, embedding float[{VectorDimension}], {VecTableOptions})";
}
```

**Step 2: Run build to verify compilation**

Run: `dotnet build src/FluxIndex.Storage.SQLite/FluxIndex.Storage.SQLite.csproj`
Expected: 컴파일 오류 발생 (호출부에서 파라미터 불일치) — 이 오류는 Task 2에서 해결

**Step 3: Commit**

```bash
git add src/FluxIndex.Storage.SQLite/SQLiteVecOptions.cs
git commit -m "refactor: SQLiteVecOptions uses dimension-based table naming"
```

---

### Task 2: SQLiteVecDbContext — 동적 테이블명 적용

**Files:**
- Modify: `src/FluxIndex.Storage.SQLite/SQLiteVecDbContext.cs:278,401,407,429`

**Step 1: Replace all hardcoded "chunk_embeddings" with options-based table name**

4곳의 하드코딩된 `"chunk_embeddings"` → `_options.GetVecTableName()` 교체.

`InitializeSQLiteVecAsync` (라인 276-281):
```csharp
await _extensionLoader.CreateVecTableAsync(
    (Microsoft.Data.Sqlite.SqliteConnection)connection,
    _options.GetVecTableName(),       // was: "chunk_embeddings"
    _options.VectorDimension,
    _options.VecTableOptions,
    cancellationToken);
```

`StoreVectorInVecTableAsync` (라인 400-409):
```csharp
// DELETE
await Database.ExecuteSqlRawAsync(
    $"DELETE FROM {_options.GetVecTableName()} WHERE chunk_id = {{0}}",
    new object[] { chunkId },
    cancellationToken);

// INSERT
await Database.ExecuteSqlRawAsync(
    $"INSERT INTO {_options.GetVecTableName()} (chunk_id, embedding) VALUES ({{0}}, {{1}})",
    new object[] { chunkId, vectorString },
    cancellationToken);
```

`DeleteVectorFromVecTableAsync` (라인 428-431):
```csharp
await Database.ExecuteSqlRawAsync(
    $"DELETE FROM {_options.GetVecTableName()} WHERE chunk_id = {{0}}",
    chunkId,
    cancellationToken);
```

**Step 2: Run build**

Run: `dotnet build src/FluxIndex.Storage.SQLite/FluxIndex.Storage.SQLite.csproj`
Expected: 컴파일 오류 — SQLiteVecVectorStore에서도 하드코딩 사용 중 (Task 3에서 해결)

**Step 3: Commit**

```bash
git add src/FluxIndex.Storage.SQLite/SQLiteVecDbContext.cs
git commit -m "refactor: SQLiteVecDbContext uses dimension-based table name"
```

---

### Task 3: SQLiteVecVectorStore — 동적 테이블명 적용

**Files:**
- Modify: `src/FluxIndex.Storage.SQLite/SQLiteVecVectorStore.cs:290,402,949,1004`

**Step 1: Replace all hardcoded "chunk_embeddings" (4곳)**

배치 삽입 (라인 290):
```csharp
var sql = $"INSERT INTO {_options.GetVecTableName()} (chunk_id, embedding) VALUES {string.Join(", ", valuesClauses)}";
```

벡터 검색 (라인 402):
```csharp
var sql = $@"
    SELECT
        vc.Id,
        vc.DocumentId,
        vc.ChunkIndex,
        vc.Content,
        vc.TokenCount,
        vc.Metadata,
        ce.distance
    FROM {_options.GetVecTableName()} ce
    JOIN vector_chunks vc ON vc.Id = ce.chunk_id
    WHERE ce.embedding MATCH @vector AND k = @k
    AND ce.distance >= @minScore
    ORDER BY ce.distance";
```

Clear (라인 949):
```csharp
await _context.Database.ExecuteSqlRawAsync($"DELETE FROM {_options.GetVecTableName()}", cancellationToken);
```

초기화 (라인 1002-1007):
```csharp
await _extensionLoader.CreateVecTableAsync(
    (Microsoft.Data.Sqlite.SqliteConnection)connection,
    _options.GetVecTableName(),       // was: "chunk_embeddings"
    _options.VectorDimension,
    _options.VecTableOptions,
    cancellationToken);
```

**Step 2: Run build — full solution**

Run: `dotnet build`
Expected: PASS (모든 chunk_embeddings 하드코딩 제거 완료)

**Step 3: Run SQLite tests**

Run: `dotnet test tests/FluxIndex.Storage.SQLite.Tests`
Expected: PASS

**Step 4: Commit**

```bash
git add src/FluxIndex.Storage.SQLite/SQLiteVecVectorStore.cs
git commit -m "refactor: SQLiteVecVectorStore uses dimension-based table name"
```

---

### Task 4: Legacy table migration — auto-rename on startup

**Files:**
- Modify: `src/FluxIndex.Storage.SQLite/SQLiteVecDbContext.cs` (InitializeSQLiteVecAsync 메서드)

**Step 1: Add migration logic before table creation**

`InitializeSQLiteVecAsync`에서 vec0 테이블 생성 전, 레거시 `chunk_embeddings` 테이블이 존재하면 자동 rename.

```csharp
// InitializeSQLiteVecAsync 내부, CreateVecTableAsync 호출 전에 추가:

// Migrate legacy table if exists
await MigrateLegacyVecTableAsync(
    (Microsoft.Data.Sqlite.SqliteConnection)connection, cancellationToken);
```

새 private 메서드:
```csharp
private async Task MigrateLegacyVecTableAsync(
    Microsoft.Data.Sqlite.SqliteConnection connection,
    CancellationToken cancellationToken)
{
    var newTableName = _options.GetVecTableName();

    // Check if legacy "chunk_embeddings" table exists (and new table doesn't)
    using var checkCmd = connection.CreateCommand();
    checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='chunk_embeddings'";
    var legacyExists = await checkCmd.ExecuteScalarAsync(cancellationToken) != null;

    if (!legacyExists)
        return;

    // Check if the new dimension-based table already exists
    using var checkNewCmd = connection.CreateCommand();
    checkNewCmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{newTableName}'";
    var newExists = await checkNewCmd.ExecuteScalarAsync(cancellationToken) != null;

    if (newExists)
    {
        // Both exist — drop legacy to avoid confusion
        using var dropCmd = connection.CreateCommand();
        dropCmd.CommandText = "DROP TABLE chunk_embeddings";
        await dropCmd.ExecuteNonQueryAsync(cancellationToken);
        return;
    }

    // Rename legacy table to dimension-based name
    using var renameCmd = connection.CreateCommand();
    renameCmd.CommandText = $"ALTER TABLE chunk_embeddings RENAME TO {newTableName}";
    await renameCmd.ExecuteNonQueryAsync(cancellationToken);

    _logger.LogInformation("Migrated legacy chunk_embeddings table to {NewTable}", newTableName);
}
```

**NOTE:** vec0 가상 테이블은 `ALTER TABLE RENAME`을 지원하지 않을 수 있음. 그 경우 데이터 복사 전략 필요:
1. 새 테이블 생성
2. `INSERT INTO {new} SELECT * FROM chunk_embeddings`
3. `DROP TABLE chunk_embeddings`

구현 시 먼저 RENAME을 시도하고, 실패하면 복사 전략으로 폴백.

**Step 2: Run build and tests**

Run: `dotnet build src/FluxIndex.Storage.SQLite/FluxIndex.Storage.SQLite.csproj`
Expected: PASS

**Step 3: Commit**

```bash
git add src/FluxIndex.Storage.SQLite/SQLiteVecDbContext.cs
git commit -m "feat: auto-migrate legacy chunk_embeddings table to dimension-based naming"
```

---

### Task 5: VaultEntry — EmbeddedDimension 필드 추가

**Files:**
- Modify: `src/FluxIndex.Extensions.FileVault/Domain/Entities/VaultEntry.cs`
- Test: `tests/FluxIndex.Extensions.FileVault.Tests/Domain/VaultEntryTests.cs`

**Step 1: Write failing test**

```csharp
[Fact]
public void MarkMemorized_WithDimension_StoresDimension()
{
    var entry = CreateTestEntry();
    var hash = ContentHash.FromHex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    entry.MarkExtracted(hash);

    entry.MarkMemorized(5, embeddedDimension: 384);

    entry.Stage.Should().Be(ProcessingStage.Memorized);
    entry.ChunkCount.Should().Be(5);
    entry.EmbeddedDimension.Should().Be(384);
}

[Fact]
public void MarkMemorized_WithoutDimension_LeavesNull()
{
    var entry = CreateTestEntry();
    var hash = ContentHash.FromHex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    entry.MarkExtracted(hash);

    entry.MarkMemorized(5);

    entry.EmbeddedDimension.Should().BeNull();
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/FluxIndex.Extensions.FileVault.Tests --filter "MarkMemorized_WithDimension"`
Expected: FAIL — `EmbeddedDimension` 속성 없음

**Step 3: Add EmbeddedDimension property and update MarkMemorized**

VaultEntry.cs 속성 추가 (기존 `ChunkCount` 근처):
```csharp
public int? EmbeddedDimension { get; private set; }
```

MarkMemorized 시그니처 변경 (라인 217-224):
```csharp
public void MarkMemorized(int chunkCount, int? embeddedDimension = null)
{
    Stage = ProcessingStage.Memorized;
    ChunkCount = chunkCount;
    EmbeddedDimension = embeddedDimension;
    LastProcessedAt = DateTimeOffset.UtcNow;
    LastError = null;
    RetryCount = 0;
}
```

EntryMetadata 내부 클래스에 추가 (라인 455 근처):
```csharp
public int? EmbeddedDimension { get; set; }
```

SaveMetadata에 추가 (라인 358 근처):
```csharp
EmbeddedDimension = EmbeddedDimension,
```

Load에 추가 (라인 167 근처):
```csharp
EmbeddedDimension = meta.EmbeddedDimension,
```

**Step 4: Run tests**

Run: `dotnet test tests/FluxIndex.Extensions.FileVault.Tests`
Expected: PASS

**Step 5: Commit**

```bash
git add src/FluxIndex.Extensions.FileVault/Domain/Entities/VaultEntry.cs tests/FluxIndex.Extensions.FileVault.Tests/Domain/VaultEntryTests.cs
git commit -m "feat: add EmbeddedDimension to VaultEntry for per-entry dimension tracking"
```

---

### Task 6: VaultEntry — SaveMetadata/Load 라운드트립 테스트

**Files:**
- Test: `tests/FluxIndex.Extensions.FileVault.Tests/Domain/VaultEntryTests.cs`

**Step 1: Write failing test**

```csharp
[Fact]
public void SaveAndLoad_WithEmbeddedDimension_Roundtrips()
{
    var entry = CreateTestEntry();
    var hash = ContentHash.FromHex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    entry.MarkExtracted(hash);
    entry.MarkMemorized(10, embeddedDimension: 1024);
    entry.SaveMetadata();

    var loaded = VaultEntry.Load(entry.EntryPath, entry.VaultBasePath);

    loaded.Should().NotBeNull();
    loaded!.EmbeddedDimension.Should().Be(1024);
    loaded.ChunkCount.Should().Be(10);
}

[Fact]
public void Load_LegacyMetaWithoutDimension_ReturnsNullDimension()
{
    var entry = CreateTestEntry();
    var hash = ContentHash.FromHex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    entry.MarkExtracted(hash);
    entry.MarkMemorized(5); // no dimension
    entry.SaveMetadata();

    var loaded = VaultEntry.Load(entry.EntryPath, entry.VaultBasePath);

    loaded.Should().NotBeNull();
    loaded!.EmbeddedDimension.Should().BeNull();
}
```

**Step 2: Run test**

Run: `dotnet test tests/FluxIndex.Extensions.FileVault.Tests --filter "SaveAndLoad_WithEmbeddedDimension"`
Expected: PASS (Task 5에서 이미 구현됨)

**Step 3: Commit**

```bash
git add tests/FluxIndex.Extensions.FileVault.Tests/Domain/VaultEntryTests.cs
git commit -m "test: add roundtrip tests for VaultEntry.EmbeddedDimension"
```

---

### Task 7: VaultPipeline — embedding dimension 전달

**Files:**
- Modify: `src/FluxIndex.Extensions.FileVault/Services/VaultPipeline.cs:162,199,248`

**Step 1: Update MarkMemorized calls to pass dimension**

3곳의 `entry.MarkMemorized(...)` 호출에 dimension 전달.

Empty content (라인 162):
```csharp
var dimension = _embeddingService?.GetEmbeddingDimension();
entry.MarkMemorized(0, dimension);
```

MemorizeAsync (라인 199):
```csharp
var dimension = _embeddingService?.GetEmbeddingDimension();
entry.MarkMemorized(result.ChunkCount, dimension);
```

RefreshAsync (라인 248):
```csharp
var dimension = _embeddingService?.GetEmbeddingDimension();
entry.MarkMemorized(result.ChunkCount, dimension);
```

**Step 2: Run build and tests**

Run: `dotnet build && dotnet test tests/FluxIndex.Extensions.FileVault.Tests`
Expected: PASS

**Step 3: Commit**

```bash
git add src/FluxIndex.Extensions.FileVault/Services/VaultPipeline.cs
git commit -m "feat: VaultPipeline passes embedding dimension to MarkMemorized"
```

---

### Task 8: Qdrant — commit existing dynamic dimension code

**Files:**
- Already modified: `src/FluxIndex.Storage.Qdrant/QdrantVectorStore.cs`

**Step 1: Review current diff**

Run: `git diff src/FluxIndex.Storage.Qdrant/QdrantVectorStore.cs`
Expected: 동적 dimension 전환 로직이 보임

**Step 2: Run build**

Run: `dotnet build src/FluxIndex.Storage.Qdrant/FluxIndex.Storage.Qdrant.csproj`
Expected: PASS

**Step 3: Commit**

```bash
git add src/FluxIndex.Storage.Qdrant/QdrantVectorStore.cs
git commit -m "feat: QdrantVectorStore dynamic dimension adaptation on search"
```

---

### Task 9: Full integration test — build + test all

**Step 1: Build entire solution**

Run: `dotnet build`
Expected: PASS

**Step 2: Run all non-infra tests**

Run: `dotnet test tests/FluxIndex.Core.Tests tests/FluxIndex.SDK.Tests tests/FluxIndex.Storage.SQLite.Tests tests/FluxIndex.Extensions.FileVault.Tests`
Expected: PASS

**Step 3: Close issue**

Move `claudedocs/issues/ISSUE-20260303-dimension-aware-vault-sqlite-vec.md` to `claudedocs/issues/closed/`.

**Step 4: Final commit**

```bash
git mv claudedocs/issues/ISSUE-20260303-dimension-aware-vault-sqlite-vec.md claudedocs/issues/closed/
git commit -m "chore: close dimension-aware vault issue"
```
