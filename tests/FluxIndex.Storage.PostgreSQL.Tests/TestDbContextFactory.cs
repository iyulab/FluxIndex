using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// An <see cref="IDbContextFactory{TContext}"/> over a creation delegate, for tests that build a store by
/// hand: the stores open a context per operation, so they take a factory rather than one context.
/// </summary>
internal sealed class TestDbContextFactory<TContext>(Func<TContext> create) : IDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext CreateDbContext() => create();
}
