using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// An <see cref="IDbContextFactory{TContext}"/> over a creation delegate, for a registration that builds
/// its context by hand rather than through <c>AddDbContextFactory</c>.
/// </summary>
internal sealed class DelegateDbContextFactory<TContext>(Func<TContext> create) : IDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext CreateDbContext() => create();
}
