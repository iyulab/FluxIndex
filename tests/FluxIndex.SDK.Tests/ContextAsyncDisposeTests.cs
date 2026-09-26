using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// A service registered through <c>ConfigureServices</c> that implements only <see cref="IAsyncDisposable"/> cannot be
/// released by a synchronous container dispose: the container throws on it and skips the registrations after it.
/// <see cref="FluxIndexContext.Dispose()"/> catches and logs that throw, so the abort was invisible. <see cref="FluxIndexContext.DisposeAsync"/> releases both.
/// </summary>
public class ContextAsyncDisposeTests
{
    private sealed class AsyncOnlyService : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class SyncService : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private static (IFluxIndexContext Context, AsyncOnlyService AsyncOnly, SyncService Sync) Build()
    {
        // Registration order matters for the control: the container disposes in reverse creation order, so the sync
        // service (created first) is disposed after the async-only one — the one a synchronous dispose never reaches.
        var context = FluxIndexContext.CreateBuilder()
            .UseInMemoryEmbedding()
            .SuppressStartupMessages()
            .ConfigureServices(s =>
            {
                s.AddSingleton<SyncService>();
                s.AddSingleton<AsyncOnlyService>();
            })
            .Build();

        var sync = context.ServiceProvider.GetRequiredService<SyncService>();
        var asyncOnly = context.ServiceProvider.GetRequiredService<AsyncOnlyService>();
        return (context, asyncOnly, sync);
    }

    [Fact]
    public async Task DisposeAsync_ReleasesAnAsyncOnlyService_AndTheRegistrationsAfterIt()
    {
        var (context, asyncOnly, sync) = Build();

        await context.DisposeAsync();

        asyncOnly.Disposed.Should().BeTrue("DisposeAsync disposes the container asynchronously");
        sync.Disposed.Should().BeTrue("the dispose is not aborted halfway");
    }

    [Fact]
    public void SyncDispose_CannotReleaseAnAsyncOnlyService_AndStopsThere()
    {
        // The control: this is why DisposeAsync exists. If this ever turns green-for-release, the container changed.
        var (context, asyncOnly, sync) = Build();

        context.Dispose();

        asyncOnly.Disposed.Should().BeFalse();
        sync.Disposed.Should().BeFalse("the synchronous container dispose threw at the async-only service first");
    }

    [Fact]
    public async Task DisposeAsync_Twice_IsANoOp()
    {
        var (context, _, _) = Build();

        await context.DisposeAsync();
        await context.DisposeAsync();
    }
}
