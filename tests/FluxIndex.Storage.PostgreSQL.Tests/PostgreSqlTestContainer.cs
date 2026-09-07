using Testcontainers.PostgreSql;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// The PostgreSQL container every integration test in this project runs against.
/// </summary>
/// <remarks>
/// One image for the whole suite: the vector tests need the pgvector extension, and running the
/// keyword tests against a second, plain postgres image would pull twice, pin two server versions
/// and let a dialect difference surface in one set of tests but not the other. The image name also
/// lives in one place rather than in nine constructors, so bumping the server version is one edit.
/// </remarks>
internal static class PostgreSqlTestContainer
{
    /// <summary>The image these tests run against. pgvector's image is postgres plus the extension.</summary>
    internal const string Image = "pgvector/pgvector:pg16";

    /// <summary>Builds a container on <see cref="Image"/>.</summary>
    internal static PostgreSqlContainer Create() => new PostgreSqlBuilder(Image).Build();
}
