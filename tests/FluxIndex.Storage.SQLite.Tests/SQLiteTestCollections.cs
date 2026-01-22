using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// SQLite 테스트 컬렉션 정의.
/// SQLite는 파일 기반 데이터베이스로 동시 접근 시 잠금 충돌이 발생할 수 있으므로,
/// 모든 SQLite 테스트를 같은 컬렉션에 배치하여 순차 실행합니다.
/// </summary>
[CollectionDefinition("SQLite Tests", DisableParallelization = true)]
public class SQLiteTestCollection : ICollectionFixture<SQLiteTestFixture>
{
}

/// <summary>
/// SQLite 테스트용 공유 픽스처.
/// 테스트 간 공유되는 리소스가 필요할 경우 여기에 추가합니다.
/// </summary>
public class SQLiteTestFixture : IDisposable
{
    public SQLiteTestFixture()
    {
        // 테스트 시작 시 초기화 (필요한 경우)
    }

    public void Dispose()
    {
        // 모든 테스트 완료 후 정리 (필요한 경우)
        GC.SuppressFinalize(this);
    }
}
