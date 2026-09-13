using System.Text.RegularExpressions;
using FluxIndex.Storage.SQLite;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// The sqlite-vec store writes and reads <c>vector_chunks</c> through raw SQL whose column lists are
/// typed by hand, while the table itself is provisioned from <see cref="VectorChunkEntity"/> by EF.
/// A column added to the entity (TotalChunks was) is not added to those lists by anything but a
/// person, and a missing one fails silently: the INSERT stores a default, the SELECT reads none.
/// These facts pin every hand-typed list to the entity's properties, naming the one intentional gap.
/// </summary>
public class SQLiteVecRawSqlColumnsTests
{
    private static readonly string StoreSource = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "FluxIndex.Storage.SQLite", "SQLiteVecVectorStore.cs"));

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FluxIndex.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("FluxIndex.slnx not found above the test assembly");
    }

    private static HashSet<string> EntityColumns() =>
        typeof(VectorChunkEntity).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

    // The store builds SQL from concatenated string literals; join the literals of each statement
    // before reading the column list, then split on the quoted or bare identifiers.
    private static readonly Regex InsertColumns = new(@"INSERT INTO \\""vector_chunks\\"" ""\s*\+\s*""\(((?:\\""\w+\\""(?:,\s*)?)+)\)", RegexOptions.Compiled);
    private static readonly Regex UpdateSet = new(@"DO UPDATE SET (.*?);", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex SelectColumns = new(@"SELECT\s+([\w,\s]+?)\s+FROM vector_chunks", RegexOptions.Compiled);
    private static readonly Regex Identifier = new(@"\\""(\w+)\\""", RegexOptions.Compiled);

    [Fact]
    public void InsertColumnList_IsExactlyTheEntitysProperties()
    {
        var match = InsertColumns.Match(StoreSource);
        Assert.True(match.Success, "the vector_chunks INSERT column list was not found; the statement shape changed");

        var columns = Identifier.Matches(match.Groups[1].Value).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(EntityColumns(), columns);
    }

    [Fact]
    public void OnConflictUpdateList_IsEveryPropertyButTheKeyAndCreatedAt()
    {
        // Re-storing an id follows the new write for every field except the key and the row's own
        // creation time — the same rule the identity contract suite asserts through the store.
        var match = UpdateSet.Match(StoreSource);
        Assert.True(match.Success, "the vector_chunks ON CONFLICT DO UPDATE list was not found");

        var assigned = Regex.Matches(match.Groups[1].Value, @"\\""(\w+)\\"" = excluded")
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var expected = EntityColumns();
        expected.Remove(nameof(VectorChunkEntity.Id));
        expected.Remove(nameof(VectorChunkEntity.CreatedAt));

        Assert.Equal(expected, assigned);
    }

    [Fact]
    public void SearchSelectColumnList_IsEveryPropertyButCreatedAt()
    {
        // The KNN metadata fetch maps rows back to DocumentChunk, which has no CreatedAt of the
        // store's own; every other column must be read or the chunk comes back with a default.
        var match = SelectColumns.Match(StoreSource);
        Assert.True(match.Success, "the vector_chunks metadata SELECT was not found");

        var columns = match.Groups[1].Value.Split(',').Select(c => c.Trim()).ToHashSet(StringComparer.Ordinal);
        var expected = EntityColumns();
        expected.Remove(nameof(VectorChunkEntity.CreatedAt));

        Assert.Equal(expected, columns);
    }
}
