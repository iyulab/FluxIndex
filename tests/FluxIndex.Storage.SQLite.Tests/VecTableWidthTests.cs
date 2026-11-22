using System.Globalization;
using AwesomeAssertions;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// The vec0 table name carries the embedding fingerprint but not the width: the same provider and model configured at a
/// new width must not keep a table of the old width (every insert would fail «Dimension mismatch»).
/// </summary>
[Collection("SQLite Tests")]
public sealed class VecTableWidthTests : IDisposable
{
    private const string Table = "chunk_embeddings_abcd1234";
    private readonly SqliteConnection _connection;
    private readonly SQLiteVecExtensionLoader _loader;

    public VecTableWidthTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _loader = new SQLiteVecExtensionLoader(
            NullLogger<SQLiteVecExtensionLoader>.Instance,
            Options.Create(new SQLiteVecOptions { UseSQLiteVec = true }));
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task An_empty_table_of_another_width_is_recreated_at_the_configured_width()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var ct = TestContext.Current.CancellationToken;
        (await _loader.LoadExtensionAsync(_connection, ct)).Should().BeTrue();
        (await _loader.CreateVecTableAsync(_connection, Table, 768, cancellationToken: ct)).Should().BeTrue();

        (await _loader.CreateVecTableAsync(_connection, Table, 1024, cancellationToken: ct)).Should().BeTrue();

        DeclaredDdl().Should().Contain("float[1024]");
        Insert("a", 1024);
        Count().Should().Be(1);
    }

    [Fact]
    public async Task A_table_of_another_width_that_holds_vectors_is_refused_and_kept()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var ct = TestContext.Current.CancellationToken;
        (await _loader.LoadExtensionAsync(_connection, ct)).Should().BeTrue();
        (await _loader.CreateVecTableAsync(_connection, Table, 768, cancellationToken: ct)).Should().BeTrue();
        Insert("kept", 768);

        var act = () => _loader.CreateVecTableAsync(_connection, Table, 1024, cancellationToken: ct);

        var thrown = (await act.Should().ThrowAsync<SQLiteVecDimensionMismatchException>()).Which;
        thrown.TableName.Should().Be(Table);
        thrown.TableDimension.Should().Be(768);
        thrown.ConfiguredDimension.Should().Be(1024);
        thrown.StoredVectorCount.Should().Be(1);
        DeclaredDdl().Should().Contain("float[768]");
        Count().Should().Be(1);
    }

    [Fact]
    public async Task A_table_of_the_configured_width_is_kept_with_its_vectors()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();
        var ct = TestContext.Current.CancellationToken;
        (await _loader.LoadExtensionAsync(_connection, ct)).Should().BeTrue();
        (await _loader.CreateVecTableAsync(_connection, Table, 768, cancellationToken: ct)).Should().BeTrue();
        Insert("kept", 768);

        (await _loader.CreateVecTableAsync(_connection, Table, 768, cancellationToken: ct)).Should().BeTrue();

        DeclaredDdl().Should().Contain("float[768]");
        Count().Should().Be(1);
    }

    private string DeclaredDdl()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=@name";
        command.Parameters.AddWithValue("@name", Table);
        return (string)command.ExecuteScalar()!;
    }

    private void Insert(string id, int width)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"INSERT INTO {Table} (chunk_id, embedding) VALUES (@id, @v)";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@v", "[" + string.Join(",", Enumerable.Repeat(0.5f.ToString(CultureInfo.InvariantCulture), width)) + "]");
        command.ExecuteNonQuery();
    }

    private long Count()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {Table}";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
