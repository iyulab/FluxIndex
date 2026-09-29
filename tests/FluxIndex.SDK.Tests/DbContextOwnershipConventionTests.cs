using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using FluxIndex.Storage.PostgreSQL;
using FluxIndex.Storage.SQLite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// A storage type must not hold an EF Core <see cref="DbContext"/> in a field. A context is not safe for
/// concurrent use, and the SDK's context keeps its stores for its whole lifetime, so a store that holds
/// one fails the moment two callers overlap. Stores take an <see cref="IDbContextFactory{TContext}"/> and
/// open a context per operation instead.
/// </summary>
public class DbContextOwnershipConventionTests
{
    /// <summary>
    /// Stores not yet moved to a context factory. This list only shrinks: an entry that no longer holds a
    /// context fails the test below until it is removed from here.
    /// </summary>
    private static readonly HashSet<string> Pending = new(StringComparer.Ordinal)
    {
        "FluxIndex.Storage.PostgreSQL.Cache.PostgresSemanticCache",
        "FluxIndex.Storage.PostgreSQL.EntityGraph.PostgresEntityGraphStore",
        "FluxIndex.Storage.PostgreSQL.Graph.PostgresGraphStore",
        "FluxIndex.Storage.PostgreSQL.PostgreSQLQuantizedVectorStore",
        "FluxIndex.Storage.SQLite.Cache.SQLiteSemanticCache",
        "FluxIndex.Storage.SQLite.Graph.SQLiteEntityGraphStore",
        "FluxIndex.Storage.SQLite.Graph.SQLiteGraphStore",
        "FluxIndex.Storage.SQLite.SQLiteQuantizedVectorStore",
        "FluxIndex.Storage.SQLite.SQLiteVecVectorStore",
    };

    private static readonly Assembly[] StorageAssemblies =
    [
        typeof(SQLiteVectorStore).Assembly,
        typeof(PostgreSQLVectorStore).Assembly,
    ];

    private static IEnumerable<string> TypesHoldingADbContext() =>
        StorageAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => !typeof(DbContext).IsAssignableFrom(t))
            // Async state machines and closures hoist a method's local context into a field; that
            // context lives for one call, which is exactly the shape this convention asks for.
            .Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() is null && !t.Name.Contains('<'))
            .Where(t => t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Any(f => typeof(DbContext).IsAssignableFrom(f.FieldType)))
            .Select(t => t.FullName!)
            .OrderBy(n => n, StringComparer.Ordinal);

    [Fact]
    public void NoStorageTypeHoldsADbContext_OutsideThePendingList()
    {
        TypesHoldingADbContext().Where(n => !Pending.Contains(n)).Should().BeEmpty(
            "a store that keeps one DbContext fails under concurrent callers — take an IDbContextFactory<T> and open a context per operation");
    }

    [Fact]
    public void EveryPendingEntry_StillHoldsADbContext()
    {
        Pending.Except(TypesHoldingADbContext()).Should().BeEmpty(
            "a store that no longer holds a DbContext must leave the pending list, so the list only shrinks");
    }
}
