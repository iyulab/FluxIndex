# FluxIndex.Extensions.FileVault

Git-like file tracking system for RAG indexing pipelines.

## Overview

FileVault는 RAG(Retrieval-Augmented Generation) 파이프라인을 위한 파일 추적 시스템입니다.
파일의 변경사항을 감지하고, 추출-청킹-임베딩 파이프라인을 관리합니다.

## Scope & Responsibility

### FileVault가 하는 것 (In Scope)

- **인덱싱 대상 파일 추적**: Vault에 등록된 파일의 상태 관리
- **변경 감지**: 소스 파일 및 Vault 콘텐츠의 변경 감지 (content hash 기반)
- **처리 파이프라인**: 추출 → 청킹 → 임베딩 → 벡터 스토어 저장
- **큐 기반 처리**: 백그라운드 작업 큐로 대용량 파일 처리
- **폴더 감시**: FileSystemWatcher를 통한 실시간 파일 변경 감지
- **Git 통합**: Vault 콘텐츠의 버전 관리 (diff, log, commit)

### FileVault가 하지 않는 것 (Out of Scope)

- **파일 시스템 브라우저**: 임의 경로의 파일/폴더 목록 제공
- **파일 CRUD**: 파일 생성, 이름 변경, 삭제 등의 파일 시스템 조작
- **UI 데이터 포맷팅**: 앱별 UI 요구사항에 맞춘 데이터 변환
- **파일 미리보기**: 썸네일, 미리보기 이미지 생성

> **설계 원칙**: FileVault는 "인덱싱 인프라"이지, "파일 관리자"가 아닙니다.
> 파일 탐색기 기능이 필요한 앱은 자체 파일 서비스를 구현하고,
> FileVault의 상태 정보와 병합하여 사용하세요.

## Installation

```bash
dotnet add package FluxIndex.Extensions.FileVault
```

## Quick Start

```csharp
// 1. DI 등록
services.AddFileVault(options =>
{
    options.VaultBasePath = "./data/.vault";
    options.EnableBackgroundProcessing = true;
    options.EnableRealTimeWatch = true;
});

// 2. Vault 사용
var vault = serviceProvider.GetRequiredService<IVault>();

// 폴더 감시 등록
await vault.AddWatchedFolderAsync("/documents",
    isRecursive: true,
    autoMemorize: true,
    includePatterns: ["*.pdf", "*.docx", "*.md"]);

// 파일 메모라이즈 (인덱싱) — background 모드에서는 큐에 적재 후 즉시 반환
await vault.MemorizeAsync("/documents/report.pdf");

// 완료까지 대기 (background 모드에서도 terminal-await; polling 불필요)
// 실패/취소는 예외로 표면화 — 미완료 entry를 "완료"로 오인하지 않음
var memorized = await vault.MemorizeAsync("/documents/report.pdf", waitForCompletion: true);

// 변경사항 동기화
var syncResult = await vault.SyncAsync();

// 상태 확인
var status = await vault.StatusAsync();
Console.WriteLine($"Total: {status.TotalEntries}, Memorized: {status.MemorizedCount}");
```

## Core Concepts

### Processing Stages

| Stage | Description |
|-------|-------------|
| `Source` | 파일이 등록됨, 아직 처리되지 않음 |
| `Extracted` | 콘텐츠 추출 완료 (refined.md 생성) |
| `Memorized` | 임베딩 및 벡터 스토어 저장 완료 |

### Sync Status

| Status | Description |
|--------|-------------|
| `InSync` | 소스와 Vault가 동기화됨 |
| `SourceModified` | 소스 파일이 변경됨 → Memorize 필요 |
| `VaultModified` | Vault 파일이 변경됨 → Refresh 필요 |
| `SourceDeleted` | 소스 파일이 삭제됨 → Remove 필요 |
| `RemovalPending` | 제거 작업 대기 중 |
| `RemovalPartial` | 제거 작업 부분 완료 (크래시 복구용) |
| `Error` | 처리 중 오류 발생 |

### Vault Directory Structure

```
.vault/{filepath-hash}/
├── meta.json           # 메타데이터 (git 추적 X)
├── images/             # 추출된 이미지 (git 추적 X)
│   └── manifest.json
└── vault/              # 콘텐츠 (git 추적 O)
    ├── .git/
    ├── refined.md      # 추출된 텍스트
    ├── append-text.md  # 사용자 추가 텍스트
    └── qa.md           # Q&A 쌍
```

## API Reference

### Core Commands

```csharp
// 전체 파이프라인 실행 (extract → chunk → embed → commit)
Task<VaultEntry> MemorizeAsync(string filePath);

// 위와 동일하되, waitForCompletion: true면 background 모드에서도 terminal(Memorized)까지
// signal 기반으로 대기(no polling)한 뒤 Memorized-stage entry 반환. 실패→InvalidOperationException,
// 취소→OperationCanceledException. 큐 레벨 빌딩블록: IVaultQueueService.WaitForJobAsync(jobId).
Task<VaultEntry> MemorizeAsync(string filePath, bool waitForCompletion);

// 부분 파이프라인 (chunk → embed → commit, 추출 스킵)
Task<VaultEntry> RefreshAsync(string filePath);

// 모든 감시 폴더 동기화
Task<SyncResult> SyncAsync();

// 변경 감지
Task<ChangeDetectionResult> DetectChangesAsync(string filePath);
```

### Entry Management

```csharp
// 조회
Task<VaultEntry?> GetAsync(string filePath);
Task<IReadOnlyList<VaultEntry>> ListAsync(ProcessingStage? stageFilter = null);

// 삭제
Task RemoveAsync(string filePath);
```

### Folder Watching

```csharp
// 폴더 감시 추가
Task<WatchedFolder> AddWatchedFolderAsync(
    string folderPath,
    bool isRecursive = true,
    bool autoMemorize = false,
    string[]? includePatterns = null,
    string[]? excludePatterns = null);

// 폴더 스캔
Task<ScanResult> ScanFolderAsync(string folderPath);
Task<ScanResult> ScanFolderAsync(Guid folderId);
```

### Status & History

```csharp
// 전체 상태
Task<VaultStatus> StatusAsync();

// Git 통합
Task<string> DiffAsync(string filePath);
Task<IReadOnlyList<GitCommit>> LogAsync(string filePath, int maxCount = 10);
```

### Search

```csharp
Task<VaultSearchResult> SearchAsync(
    string query,
    VaultSearchOptions? options = null,
    CancellationToken ct = default);
```

`VaultSearchOptions.SearchStrategy`로 검색 전략을 선택한다:

```csharp
public enum VaultSearchStrategy
{
    Vector,  // 밀집 벡터(의미) 검색 — 기본값
    Hybrid,  // 벡터 + 키워드(BM25) 융합 검색
}

var result = await vault.SearchAsync("query", new VaultSearchOptions
{
    SearchStrategy = VaultSearchStrategy.Hybrid,
    TopK = 10,
});
```

**Hybrid 라우팅 (store-native 우선).** `Hybrid` 요청은 다음 순서로 해소된다:
1. 등록된 `IVectorStore`가 **native hybrid**(`INativeHybridSearch`)를 노출하면 그것을 우선 사용한다 — 예:
   `SQLiteVecVectorStore`는 ingestion이 이미 채운 `chunk_fts`(BM25)와 벡터를 native 융합한다. **추가 인덱스나
   `IHybridSearchService` 등록 없이** 실제 hybrid가 동작한다.
2. native hybrid가 없고 `IHybridSearchService`가 등록돼 있으면 그것을 사용한다. (주의: `IHybridSearchService`의
   sparse 인덱스는 FileVault ingestion이 채우지 않으므로, 별도로 색인하지 않았다면 keyword side가 비어 vector와
   동일한 결과가 나올 수 있다.)
3. 둘 다 없으면 벡터 검색으로 degrade한다.

어느 경우든 **실제 실행된 전략이 결과에 정직하게 실린다** — 로그가 아니라 다음 필드로:

```csharp
result.RequestedStrategy;  // 호출자가 요청한 전략 (예: Hybrid)
result.ExecutedStrategy;   // 실제 실행된 전략 (서비스 미등록 시 Vector)
// 소비자는 ExecutedStrategy를 "유효 전략"으로 보고해야 한다 (RequestedStrategy 아님).
```

> Keyword-only(순수 BM25) 전략은 아직 노출하지 않는다. 현재 융합 엔진에 dedicated 키워드 경로가 없어
> "Keyword" 값이 degenerate weighted-hybrid로 동작할 위험이 있다 (추적: ISSUE-161).

**취소 전파:** `SearchAsync`/`SyncAsync`/`ScanFolderAsync`/`CleanupOrphanedEntriesAsync`는 호출자의
`CancellationToken`이 취소되면 `OperationCanceledException`을 전파한다 (취소를 빈 결과/부분 결과로
세탁하지 않음). 호출자 토큰과 무관한 내부 예외는 기존대로 처리된다.

### GraphRAG Indexing

FileVault memorize 경로는 SDK 직접 인덱싱 경로(`Indexer.IndexAsync`)와 **동등한 GraphRAG 의미**를 갖는다.
`IGraphRAGService`(예: `AddFullGraphRAG()`)가 DI에 등록돼 있으면, vault 경로도 vector-store ingestion 직후
엔티티 그래프를 빌드한다. per-call 제어는 `MemorizeOptions`로 한다:

```csharp
public sealed class MemorizeOptions
{
    // ... chunking/commit 옵션 ...

    // null(기본): IGraphRAGService 등록 시 자동 활성
    // true:       강제 활성 (서비스 미등록 시 memorize 실패)
    // false:      강제 비활성
    public bool? EnableGraphRAG { get; set; }

    // GraphRAG 활성 시 IGraphRAGService.BuildIndexAsync로 전달되는 빌드 옵션
    public GraphRAGBuildOptions? GraphRAGOptions { get; set; }
}

await pipeline.MemorizeAsync(entry, new MemorizeOptions
{
    EnableGraphRAG = true,                                   // 강제 활성
    GraphRAGOptions = new GraphRAGBuildOptions { /* ... */ },
}, ct);
```

> `EnableGraphRAG = true`인데 `IGraphRAGService`가 미등록이면 memorize는 `MemorizeResult.Failed`로 끝나며
> 오류 메시지에 등록 안내가 실린다 (예외를 던지지 않고 결과 객체로 전달). 자동 활성(`null`)에서 서비스가
> 없으면 GraphRAG 단계는 조용히 건너뛴다.

## Integration with Consumer Apps

### 권장 패턴: Vault 상태 + 파일 시스템 조합

FileVault는 "인덱싱 대상"만 관리합니다. 파일 탐색기 UI가 필요한 앱은 다음 패턴을 권장합니다:

```csharp
// 1. 앱에서 파일 시스템 서비스 구현
public interface IFileSystemService
{
    Task<IEnumerable<FileEntry>> ListDirectoryAsync(string path);
}

// 2. Vault 상태와 병합
public async Task<IEnumerable<UnifiedFileEntry>> GetUnifiedListAsync(string folderPath)
{
    // 파일 시스템 조회
    var files = await _fileSystemService.ListDirectoryAsync(folderPath);

    // Vault 상태 조회 (ScanResult 활용)
    var scanResult = await _vault.ScanFolderAsync(folderPath);
    var vaultMap = scanResult.DetectedChanges
        .ToDictionary(c => NormalizePath(c.FilePath));

    // 병합
    return files.Select(f => new UnifiedFileEntry
    {
        Path = f.Path,
        Name = f.Name,
        Size = f.Size,
        ModifiedAt = f.ModifiedAt,
        VaultStatus = vaultMap.TryGetValue(NormalizePath(f.Path), out var change)
            ? MapStatus(change)
            : UnifiedStatus.Untracked
    });
}
```

### ScanResult 활용

`ScanFolderAsync()`의 결과인 `ScanResult.DetectedChanges`를 활용하면 Vault 상태를 효율적으로 조회할 수 있습니다:

```csharp
var scanResult = await vault.ScanFolderAsync(watchedFolderId);

// DetectedChanges에서 Vault 상태 확인
foreach (var change in scanResult.DetectedChanges)
{
    Console.WriteLine($"{change.FilePath}: {change.RecommendedAction}");
    Console.WriteLine($"  EntryExists: {change.EntryExists}");
    Console.WriteLine($"  SourceChanged: {change.SourceChanged}");
    Console.WriteLine($"  VaultChanged: {change.VaultChanged}");
}
```

## Configuration

```csharp
services.AddFileVault(options =>
{
    // 디렉토리 설정
    options.VaultDirectoryName = ".vault";
    options.VaultBasePath = "./data";

    // 파일 필터
    options.MaxFileSizeMB = 100;
    options.DefaultIncludePatterns = ["*.pdf", "*.docx", "*.md", "*.txt"];
    options.DefaultExcludePatterns = ["~$*", "*.tmp", ".*"];

    // 감시 설정
    options.EnableRealTimeWatch = true;
    options.DebounceDelayMs = 500;

    // 백그라운드 처리
    options.EnableBackgroundProcessing = true;
    options.MaxConcurrentProcessing = 4;

    // 재시도
    options.EnableAutoRetry = true;
    options.MaxRetryCount = 3;

    // 청킹 기본값
    options.Chunking.MaxChunkSize = 1024;
    options.Chunking.OverlapSize = 128;
    options.Chunking.Strategy = "Intelligent";
});
```

## Multi-Tenant Support

`IVaultFactory`를 사용하면 테넌트별로 격리된 Vault 인스턴스를 생성할 수 있습니다.
SaaS 애플리케이션이나 사용자/조직별로 독립적인 인덱싱이 필요한 경우에 사용합니다.

### 디렉토리 구조

```
{BasePath}/
├── tenant-A/
│   └── .vault/                ← Tenant A 전용
│       ├── queue.db
│       └── {filepath-hash}/
│
└── tenant-B/
    └── .vault/                ← Tenant B 전용
        ├── queue.db
        └── {filepath-hash}/
```

### 테넌트별 격리 항목

| 항목 | 격리 | 설명 |
|------|------|------|
| `.vault/` 디렉토리 | ✅ | 메타데이터, 추출된 콘텐츠 |
| `queue.db` | ✅ | 처리 큐 영속성 |
| IContentHasher | 공유 | Stateless, 모든 테넌트 재사용 |
| IGitService | 공유 | Stateless, 모든 테넌트 재사용 |
| IVectorStore | 공유 | 벡터 DB 연결 재사용 |
| IEmbeddingService | 공유 | 임베딩 모델 재사용 |

### DI 등록

```csharp
// 기본 설정으로 Factory 등록
services.AddFileVaultFactory(options =>
{
    options.VaultBasePath = "./data";
    options.EnableBackgroundProcessing = true;
});

// FileFlux 통합
services.AddFileVaultFactoryWithFileFlux(options => ...);

// FluxIndex 풀 통합
services.AddFileVaultFactoryWithFluxIndex(options => ...);
```

### 사용법

```csharp
public class TenantService
{
    private readonly IVaultFactory _factory;

    public TenantService(IVaultFactory factory)
    {
        _factory = factory;
    }

    public async Task ProcessTenantFiles(string tenantId, string filePath)
    {
        // 테넌트별 Vault 인스턴스 획득 (없으면 생성)
        var vault = _factory.GetOrCreate(tenantId);

        // 일반 IVault와 동일하게 사용
        await vault.MemorizeAsync(filePath);
    }

    public async Task ProcessWithCustomOptions(string tenantId)
    {
        // 테넌트별 커스텀 옵션 적용
        var vault = _factory.GetOrCreate(tenantId, options =>
        {
            options.MaxFileSizeMB = 50;
            options.DefaultIncludePatterns = ["*.pdf"];
        });

        await vault.SyncAsync();
    }
}
```

### 백그라운드 서비스 구현

FileVault는 `VaultBackgroundService`를 기본 제공하지만, multi-tenant 환경에서는
앱에서 직접 백그라운드 서비스를 구현해야 합니다.

```csharp
public class MultiTenantVaultBackgroundService : BackgroundService
{
    private readonly IVaultFactory _factory;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // 모든 활성 테넌트 순회
            foreach (var context in _factory.GetAllContexts())
            {
                if (context.QueueService.IsPaused)
                    continue;

                // 작업 dequeue 및 처리
                var job = await context.QueueService.DequeueAsync(ct);
                if (job != null)
                {
                    await ProcessJobAsync(context, job, ct);
                }
            }

            await Task.Delay(1000, ct);
        }
    }

    private async Task ProcessJobAsync(VaultContext ctx, VaultJob job, CancellationToken ct)
    {
        var entry = VaultEntry.LoadByHash(job.FilepathHash, ctx.VaultBasePath)
                    ?? VaultEntry.Create(job.FilePath, ctx.VaultBasePath);

        switch (job.JobType)
        {
            case VaultJobType.Memorize:
                await ctx.Pipeline.MemorizeAsync(entry, null, ct);
                break;
            case VaultJobType.Refresh:
                await ctx.Pipeline.RefreshAsync(entry, null, ct);
                break;
            case VaultJobType.Remove:
                await ctx.Pipeline.RemoveAsync(entry, ct);
                break;
        }

        await ctx.QueueService.CompleteAsync(job.Id, ct);
    }
}
```

### 테넌트 라이프사이클

```csharp
// 테넌트 존재 여부 확인
if (_factory.Exists(tenantId))
{
    var vault = _factory.GetOrCreate(tenantId);
}

// 디스크에 있는 모든 테넌트 발견
foreach (var tenantId in _factory.DiscoverTenants())
{
    Console.WriteLine($"Found tenant: {tenantId}");
}

// 현재 메모리에 로드된 테넌트
var activeTenants = _factory.GetActiveTenants();

// 테넌트 정리
await _factory.DisposeAsync(tenantId);

// 모든 테넌트 정리
await _factory.DisposeAllAsync();
```

### 설계 원칙

> **앱 책임**: 테넌트 라이프사이클 관리 (생성, 삭제, cleanup)는 앱의 책임입니다.
> FileVault는 인프라만 제공하며, 비즈니스 로직은 포함하지 않습니다.

## Dependencies

- `FluxIndex.Core` - 핵심 인터페이스 및 서비스
- `FluxIndex.Extensions.FileFlux` - 문서 처리 (추출, 청킹)
- `Microsoft.Data.Sqlite` - 작업 큐 영속성

## License

MIT License - See LICENSE file for details.
