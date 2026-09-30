using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector.EntityFrameworkCore;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Two stores configured with different embedding dimensions in one process must each get a model whose vector column has
/// their own dimension. EF Core caches a model per context type unless told otherwise, and the first dimension used to win
/// for every later store — its table was created as the wrong <c>vector(N)</c> and every write failed.
/// </summary>
public class PostgreSQLModelDimensionTests
{
    private const string ConnectionString = "Host=localhost;Database=unused";

    [Fact]
    public void QuantizedContext_ModelFollowsEachInstancesDimension()
    {
        using var first = Quantized(8);
        using var second = Quantized(4);

        EmbeddingColumnType(first, typeof(QuantizedVectorEntity)).Should().Be("vector(8)");
        EmbeddingColumnType(second, typeof(QuantizedVectorEntity)).Should().Be("vector(4)");
    }

    [Fact]
    public void Context_ModelFollowsEachInstancesDimension()
    {
        using var first = Plain(8);
        using var second = Plain(4);

        EmbeddingColumnType(first, typeof(VectorEntity)).Should().Be("vector(8)");
        EmbeddingColumnType(second, typeof(VectorEntity)).Should().Be("vector(4)");
    }

    private static string? EmbeddingColumnType(DbContext context, Type entity)
        => context.Model.FindEntityType(entity)!.FindProperty("Embedding")!.GetColumnType();

    private static FluxIndexQuantizedDbContext Quantized(int dimensions) => new(
        new DbContextOptionsBuilder<FluxIndexQuantizedDbContext>().UseNpgsql(ConnectionString, o => o.UseVector()).Options,
        Options.Create(new PostgreSQLQuantizedOptions { ConnectionString = ConnectionString, EmbeddingDimensions = dimensions }));

    private static FluxIndexDbContext Plain(int dimensions) => new(
        new DbContextOptionsBuilder<FluxIndexDbContext>().UseNpgsql(ConnectionString, o => o.UseVector()).Options,
        Options.Create(new PostgreSQLOptions { ConnectionString = ConnectionString, EmbeddingDimensions = dimensions }));
}
