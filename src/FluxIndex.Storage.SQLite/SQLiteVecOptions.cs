using System.Runtime.InteropServices;

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
    public int VectorDimension { get; set; } = 1536;

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
    /// vec0 테이블 생성 시 추가 옵션
    /// 예: "metric=cosine,index=flat"
    /// </summary>
    public string VecTableOptions { get; set; } = "metric=cosine";

    /// <summary>
    /// 벡터 검색 시 기본 최소 유사도 점수
    /// </summary>
    public float DefaultMinScore { get; set; } = 0.0f;

    /// <summary>
    /// 배치 삽입 시 최대 배치 크기
    /// </summary>
    public int MaxBatchSize { get; set; } = 1000;

    /// <summary>
    /// 현재 플랫폼에 대한 기본 확장 파일 경로 반환
    /// </summary>
    public string GetDefaultExtensionPath()
    {
        if (!string.IsNullOrEmpty(CustomExtensionPath))
        {
            return CustomExtensionPath;
        }

        var runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;
        var baseDir = AppContext.BaseDirectory;

        return runtimeIdentifier switch
        {
            var rid when rid.StartsWith("win") => Path.Combine(baseDir, "runtimes", "win-x64", "native", "vec0.dll"),
            var rid when rid.StartsWith("linux") => Path.Combine(baseDir, "runtimes", "linux-x64", "native", "libvec0.so"),
            var rid when rid.StartsWith("osx") => Path.Combine(baseDir, "runtimes", "osx-x64", "native", "libvec0.dylib"),
            _ => throw new PlatformNotSupportedException($"지원되지 않는 플랫폼: {runtimeIdentifier}")
        };
    }

    /// <summary>
    /// 벡터 차원에 따른 vec0 테이블 스키마 반환
    /// </summary>
    public string GetVecTableSchema(string tableName = "chunk_embeddings")
    {
        return $"CREATE VIRTUAL TABLE {tableName} USING vec0(chunk_id TEXT PRIMARY KEY, embedding float[{VectorDimension}], {VecTableOptions})";
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
    public static SQLiteVecOptions CreateForProduction(string databasePath, int vectorDimension = 1536)
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