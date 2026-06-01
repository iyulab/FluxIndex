using System.Runtime.InteropServices;
using FluxIndex.Core.Constants;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// SQLite-vec 확장을 사용하는 벡터 저장소 옵션
/// </summary>
public class SQLiteVecOptions : SQLiteOptions
{
    /// <summary>
    /// sqlite-vec 확장 사용 여부 (기본값: true)
    /// </summary>
    public bool UseSQLiteVec { get; set; } = true;

    /// <summary>
    /// 벡터 차원 수 (임베딩 모델에 따라 설정)
    /// OpenAI ada-002: 1536, text-embedding-3-small: 1536, text-embedding-3-large: 3072
    /// </summary>
    public int VectorDimension { get; set; } = EmbeddingDefaults.DefaultVectorDimension;

    /// <summary>
    /// 임베딩 모델의 fingerprint (EmbeddingIdentity.Fingerprint).
    /// 설정되면 vec0 테이블 이름이 chunk_embeddings_{fingerprint}로 생성된다.
    /// null이면 기존 방식(chunk_embeddings_{dimension})으로 폴백.
    /// </summary>
    public string? EmbeddingFingerprint { get; set; }

    /// <summary>
    /// 벡터 인덱스 타입 (현재는 'flat'만 지원)
    /// </summary>
    public string IndexType { get; set; } = "flat";

    /// <summary>
    /// 기존 JSON 저장 방식에서 sqlite-vec로 자동 마이그레이션 여부
    /// </summary>
    public bool AutoMigrateFromLegacy { get; set; } = true;

    /// <summary>
    /// sqlite-vec 확장 로드 실패 시 기존 in-memory 방식으로 폴백 여부
    /// </summary>
    public bool FallbackToInMemoryOnError { get; set; } = true;

    /// <summary>
    /// sqlite-vec 확장 파일의 수동 경로 지정 (null이면 자동 탐지)
    /// </summary>
    public string? CustomExtensionPath { get; set; }

    /// <summary>
    /// vec0 embedding 컬럼의 distance_metric 옵션.
    /// sqlite-vec 공식 문법: "cosine", "l2", "l1"
    /// </summary>
    public string VecTableOptions { get; set; } = "distance_metric=cosine";

    /// <summary>
    /// 벡터 검색 시 기본 최소 유사도 점수
    /// </summary>
    public float DefaultMinScore { get; set; }

    /// <summary>
    /// At host startup, run a one-time sweep that removes orphan vec0 rows
    /// (rows whose chunk_id has no matching row in vector_chunks) across every
    /// fingerprint vec0 table. Default: true.
    /// </summary>
    /// <remarks>
    /// The sweep is idempotent — a marker row in <c>__fluxindex_migrations</c> guards
    /// against re-execution. Set this to <c>false</c> to skip the sweep entirely (e.g.
    /// to defer cleanup, or for tests that need to observe orphans).
    /// Introduced in 0.13.7 to clean up orphans accumulated by past fingerprint
    /// migrations under the pre-0.13.7 single-table delete behavior.
    /// </remarks>
    public bool EnableStartupOrphanSweep { get; set; } = true;

    /// <summary>
    /// At host startup, scan for cross-fingerprint orphan vec0 tables — tables whose fingerprint
    /// diverges from the bound effective fingerprint yet still hold committed vectors that vector
    /// search never queries. Each is surfaced as a WARN (non-destructive; no rows are deleted).
    /// Default: true.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="EnableStartupOrphanSweep"/>, this scan carries no migration marker and runs
    /// on every startup so the warning persists until the orphan is reconciled (via reindex). Set to
    /// <c>false</c> to suppress the scan (e.g. for tests that observe orphans, or to silence the
    /// warning once a reconciliation plan is in place).
    /// </remarks>
    public bool EnableStartupCrossFingerprintOrphanReport { get; set; } = true;

    /// <summary>
    /// 배치 삽입 시 최대 배치 크기.
    /// 1K-10K 범위 권장. 너무 크면 메모리 사용 증가, 너무 작으면 성능 저하.
    /// </summary>
    public int MaxBatchSize { get; set; } = 1000;

    /// <summary>
    /// 대용량 배치 작업에서 진행 상황 로깅 간격 (청크 수).
    /// 0이면 진행 로깅 비활성화.
    /// </summary>
    public int BatchProgressLogInterval { get; set; } = 1000;

    /// <summary>
    /// 배치 작업 중 명시적 트랜잭션 커밋 간격 (청크 수).
    /// 0이면 전체 배치를 단일 트랜잭션으로 처리.
    /// 대용량 작업 시 5000-10000 권장 (메모리 효율성).
    /// </summary>
    public int BatchTransactionCommitInterval { get; set; } = 5000;

    // ============================================================
    // FTS5 하이브리드 검색 옵션
    // ============================================================

    /// <summary>
    /// FTS5 전문 검색 테이블 사용 여부.
    /// 활성화 시 텍스트 + 벡터 하이브리드 검색 가능.
    /// </summary>
    public bool UseFts5 { get; set; } = true;

    /// <summary>
    /// FTS5 토크나이저 설정.
    /// 기본값: "unicode61" (유니코드 지원).
    /// 한국어: "unicode61 remove_diacritics 0" 또는 "trigram"
    /// </summary>
    public string Fts5Tokenizer { get; set; } = "unicode61";

    /// <summary>
    /// 하이브리드 검색 시 벡터 점수 가중치 (0.0 ~ 1.0).
    /// 기본값: 0.5 (벡터 50%, 텍스트 50%)
    /// </summary>
    public float HybridVectorWeight { get; set; } = 0.5f;

    /// <summary>
    /// RRF (Reciprocal Rank Fusion) 알고리즘의 k 파라미터.
    /// 기본값: 60 (일반적으로 좋은 성능)
    /// </summary>
    public int RrfK { get; set; } = 60;

    /// <summary>
    /// FTS5 검색 시 BM25 가중치 설정.
    /// 형식: "b, k1" (기본값: "0.75, 1.2")
    /// </summary>
    public string Fts5Bm25Weights { get; set; } = "0.75, 1.2";

    /// <summary>
    /// 현재 플랫폼에 대한 기본 확장 파일 경로 반환
    /// NuGet 패키지 sqlite-vec에서 제공하는 네이티브 파일 자동 탐지
    /// </summary>
    public string GetDefaultExtensionPath()
    {
        if (!string.IsNullOrEmpty(CustomExtensionPath))
        {
            return CustomExtensionPath;
        }

        var baseDir = AppContext.BaseDirectory;

        // Try output directory paths first
        var possiblePaths = GetPossibleExtensionPaths(baseDir);
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Try .NET native DLL search directories
        // (handles single-file publish extraction, DOTNET_BUNDLE_EXTRACT_BASE_DIR, etc.)
        var nativeSearchPath = FindInNativeSearchDirectories();
        if (nativeSearchPath is not null)
        {
            return nativeSearchPath;
        }

        // Try NuGet global packages cache (native files aren't auto-copied during build)
        var nugetCachePath = GetNuGetCacheExtensionPath();
        if (!string.IsNullOrEmpty(nugetCachePath) && File.Exists(nugetCachePath))
        {
            return nugetCachePath;
        }

        // Return the first expected path even if it doesn't exist (for error reporting)
        return nugetCachePath ?? possiblePaths.FirstOrDefault() ??
            throw new PlatformNotSupportedException($"지원되지 않는 플랫폼: {RuntimeInformation.OSDescription}");
    }

    /// <summary>
    /// .NET 런타임의 네이티브 DLL 검색 디렉토리에서 sqlite-vec 확장 파일 탐색.
    /// single-file publish 시 추출 디렉토리, DOTNET_BUNDLE_EXTRACT_BASE_DIR 등을 커버한다.
    /// </summary>
    private static string? FindInNativeSearchDirectories()
    {
        try
        {
            var searchPaths = AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string;
            if (string.IsNullOrEmpty(searchPaths))
            {
                return null;
            }

            var fileName = GetPlatformFileName();
            foreach (var dir in searchPaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var filePath = Path.Combine(trimmed, fileName);
                if (File.Exists(filePath))
                {
                    return filePath;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 현재 플랫폼에 해당하는 sqlite-vec 네이티브 파일명 반환
    /// </summary>
    internal static string GetPlatformFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "vec0.dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "vec0.so";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "vec0.dylib";

        var rid = RuntimeInformation.RuntimeIdentifier;
        if (rid.StartsWith("win", StringComparison.OrdinalIgnoreCase)) return "vec0.dll";
        if (rid.StartsWith("linux", StringComparison.OrdinalIgnoreCase)) return "vec0.so";
        if (rid.StartsWith("osx", StringComparison.OrdinalIgnoreCase)) return "vec0.dylib";

        return "vec0.dll";
    }

    /// <summary>
    /// NuGet 글로벌 패키지 캐시에서 sqlite-vec 확장 경로 찾기
    /// </summary>
    private static string? GetNuGetCacheExtensionPath()
    {
        try
        {
            // Standard NuGet global packages folder locations
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var nugetGlobalPackages = Path.Combine(userProfile, ".nuget", "packages", "sqlite-vec");

            if (!Directory.Exists(nugetGlobalPackages))
            {
                return null;
            }

            // Find the latest version folder
            var versionDirs = Directory.GetDirectories(nugetGlobalPackages)
                .OrderByDescending(d => d)
                .ToList();

            foreach (var versionDir in versionDirs)
            {
                var nativePath = GetPlatformSpecificNativePath(versionDir);
                if (!string.IsNullOrEmpty(nativePath) && File.Exists(nativePath))
                {
                    return nativePath;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 플랫폼별 네이티브 파일 경로 반환
    /// </summary>
    private static string? GetPlatformSpecificNativePath(string packageDir)
    {
        string runtimeFolder;
        string fileName;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            runtimeFolder = "win-x64";
            fileName = "vec0.dll";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            runtimeFolder = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "linux-arm64"
                : "linux-x64";
            fileName = "vec0.so";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            runtimeFolder = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "osx-arm64"
                : "osx-x64";
            fileName = "vec0.dylib";
        }
        else
        {
            // Fallback: try to detect from RuntimeIdentifier
            var rid = RuntimeInformation.RuntimeIdentifier;
            if (rid.StartsWith("win", StringComparison.OrdinalIgnoreCase))
            {
                runtimeFolder = "win-x64";
                fileName = "vec0.dll";
            }
            else if (rid.StartsWith("linux", StringComparison.OrdinalIgnoreCase))
            {
                runtimeFolder = "linux-x64";
                fileName = "vec0.so";
            }
            else if (rid.StartsWith("osx", StringComparison.OrdinalIgnoreCase))
            {
                runtimeFolder = "osx-x64";
                fileName = "vec0.dylib";
            }
            else
            {
                return null;
            }
        }

        return Path.Combine(packageDir, "runtimes", runtimeFolder, "native", fileName);
    }

    /// <summary>
    /// 플랫폼별 가능한 확장 파일 경로 목록 반환 (출력 디렉토리용)
    /// Note: sqlite-vec NuGet 패키지의 파일명은 vec0.dll/vec0.so/vec0.dylib 형식
    /// </summary>
    private static string[] GetPossibleExtensionPaths(string baseDir)
    {
        // Try explicit platform check first
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new[]
            {
                Path.Combine(baseDir, "runtimes", "win-x64", "native", "vec0.dll"),
                Path.Combine(baseDir, "vec0.dll"),
                Path.Combine(baseDir, "native", "vec0.dll")
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
            return new[]
            {
                Path.Combine(baseDir, "runtimes", arch, "native", "vec0.so"),
                Path.Combine(baseDir, "vec0.so"),
                Path.Combine(baseDir, "native", "vec0.so")
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
            return new[]
            {
                Path.Combine(baseDir, "runtimes", arch, "native", "vec0.dylib"),
                Path.Combine(baseDir, "vec0.dylib"),
                Path.Combine(baseDir, "native", "vec0.dylib")
            };
        }

        // Fallback: use RuntimeIdentifier for detection
        var rid = RuntimeInformation.RuntimeIdentifier;
        if (rid.StartsWith("win", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                Path.Combine(baseDir, "runtimes", "win-x64", "native", "vec0.dll"),
                Path.Combine(baseDir, "vec0.dll"),
                Path.Combine(baseDir, "native", "vec0.dll")
            };
        }

        if (rid.StartsWith("linux", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                Path.Combine(baseDir, "runtimes", "linux-x64", "native", "vec0.so"),
                Path.Combine(baseDir, "vec0.so"),
                Path.Combine(baseDir, "native", "vec0.so")
            };
        }

        if (rid.StartsWith("osx", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                Path.Combine(baseDir, "runtimes", "osx-x64", "native", "vec0.dylib"),
                Path.Combine(baseDir, "vec0.dylib"),
                Path.Combine(baseDir, "native", "vec0.dylib")
            };
        }

        // Last resort: assume Windows if all detection fails
        return new[]
        {
            Path.Combine(baseDir, "runtimes", "win-x64", "native", "vec0.dll"),
            Path.Combine(baseDir, "vec0.dll")
        };
    }

    /// <summary>
    /// vec0 테이블 이름 반환.
    /// EmbeddingFingerprint가 반드시 설정되어야 한다.
    /// BindIdentity() 호출 후 사용할 것.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// EmbeddingFingerprint가 null인 경우. BindIdentity()를 먼저 호출해야 한다.
    /// </exception>
    public string GetVecTableName() => EmbeddingFingerprint is not null
        ? $"chunk_embeddings_{EmbeddingFingerprint}"
        : throw new InvalidOperationException(
            "EmbeddingFingerprint is required for vec table naming. " +
            "Call BindIdentity() before accessing the vector store.");

    /// <summary>
    /// 벡터 차원에 따른 vec0 테이블 스키마 반환
    /// </summary>
    public string GetVecTableSchema()
    {
        var tableName = GetVecTableName();
        return $"CREATE VIRTUAL TABLE {tableName} USING vec0(chunk_id TEXT PRIMARY KEY, embedding float[{VectorDimension}] {VecTableOptions})";
    }

    /// <summary>
    /// 옵션 유효성 검증
    /// </summary>
    public void Validate()
    {
        if (VectorDimension <= 0)
        {
            throw new ArgumentException("벡터 차원은 0보다 커야 합니다.", nameof(VectorDimension));
        }

        if (VectorDimension > 10000)
        {
            throw new ArgumentException("벡터 차원이 너무 큽니다. 일반적으로 4096 이하를 권장합니다.", nameof(VectorDimension));
        }

        if (MaxBatchSize <= 0)
        {
            throw new ArgumentException("배치 크기는 0보다 커야 합니다.", nameof(MaxBatchSize));
        }

        if (DefaultMinScore < -1.0f || DefaultMinScore > 1.0f)
        {
            throw new ArgumentException("최소 유사도 점수는 -1.0에서 1.0 사이여야 합니다.", nameof(DefaultMinScore));
        }

        if (UseSQLiteVec && !string.IsNullOrEmpty(CustomExtensionPath) && !File.Exists(CustomExtensionPath))
        {
            if (!FallbackToInMemoryOnError)
            {
                throw new FileNotFoundException($"지정된 sqlite-vec 확장 파일을 찾을 수 없습니다: {CustomExtensionPath}");
            }
        }
    }

    /// <summary>
    /// 개발용 in-memory 설정
    /// </summary>
    public static SQLiteVecOptions CreateInMemoryForDevelopment()
    {
        return new SQLiteVecOptions
        {
            UseInMemory = true,
            UseSQLiteVec = false, // In-memory에서는 확장 로드 불필요
            AutoMigrate = true,
            FallbackToInMemoryOnError = true
        };
    }

    /// <summary>
    /// 프로덕션용 파일 기반 설정
    /// </summary>
    public static SQLiteVecOptions CreateForProduction(string databasePath, int vectorDimension = EmbeddingDefaults.DefaultVectorDimension)
    {
        return new SQLiteVecOptions
        {
            DatabasePath = databasePath,
            UseSQLiteVec = true,
            VectorDimension = vectorDimension,
            AutoMigrate = true,
            FallbackToInMemoryOnError = false, // 프로덕션에서는 엄격하게
            CommandTimeout = 60 // 대용량 데이터 처리를 위해 타임아웃 증가
        };
    }

    /// <summary>
    /// 테스트용 설정
    /// </summary>
    public static SQLiteVecOptions CreateForTesting(bool useSqliteVec = true)
    {
        return new SQLiteVecOptions
        {
            UseInMemory = true,
            UseSQLiteVec = useSqliteVec,
            AutoMigrate = true,
            FallbackToInMemoryOnError = true,
            VectorDimension = 384, // 테스트용 작은 차원
            MaxBatchSize = 100
        };
    }
}