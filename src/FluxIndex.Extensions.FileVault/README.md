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

// 파일 메모라이즈 (인덱싱)
await vault.MemorizeAsync("/documents/report.pdf");

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

## Dependencies

- `FluxIndex.Core` - 핵심 인터페이스 및 서비스
- `FluxIndex.Extensions.FileFlux` - 문서 처리 (추출, 청킹)
- `Microsoft.Data.Sqlite` - 작업 큐 영속성

## License

MIT License - See LICENSE file for details.
