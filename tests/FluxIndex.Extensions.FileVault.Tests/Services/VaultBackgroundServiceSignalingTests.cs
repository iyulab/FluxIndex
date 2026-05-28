using FluentAssertions;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Enums;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using FluxIndex.Extensions.FileVault.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace FluxIndex.Extensions.FileVault.Tests.Services;

/// <summary>
/// Regression tests for the VaultBackgroundService wake-signal mechanism.
/// Verifies that JobEnqueued triggers near-immediate DequeueAsync — no polling delay.
/// </summary>
public class VaultBackgroundServiceSignalingTests
{
    private static VaultJob MakeJob() =>
        VaultJob.Create("/tmp/test.txt", "hash1", VaultJobType.Memorize, VaultJobPriority.Normal);

    [Fact]
    [Trait("Category", "Performance")]
    public async Task JobEnqueued_TriggersDequeue_WithinHalfSecond()
    {
        // Arrange
        using var fake = new FakeQueueService();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var storage = Substitute.For<IVaultStorageService>();
        var options = MsOptions.Create(new FileVaultOptions { EnableBackgroundProcessing = true });

        // BasePath points to non-existent dir → RecoverPartialRemovalsAsync exits early
        storage.BasePath.Returns(Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid()));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var svc = new VaultBackgroundService(
            NullLogger<VaultBackgroundService>.Instance,
            fake, scopeFactory, storage, options);

        _ = svc.StartAsync(cts.Token);

        // Allow service to complete its first DequeueAsync and enter _jobSignal.WaitAsync
        await Task.Delay(200, cts.Token);

        // Act — simulate enqueue: fire the event and record the timestamp
        fake.SignalTime = DateTimeOffset.UtcNow;
        fake.FireJobEnqueued(MakeJob());

        var elapsed = await fake.DequeueAfterSignal.Task.WaitAsync(TimeSpan.FromMilliseconds(500));

        // Assert
        elapsed.TotalMilliseconds.Should().BeLessThan(500,
            "JobEnqueued should wake the consumer immediately, not after a polling delay");

        await svc.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WhenPaused_ServiceDoesNotCallDequeue()
    {
        // Arrange
        using var fake = new FakeQueueService { IsPaused = true };
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var storage = Substitute.For<IVaultStorageService>();
        var options = MsOptions.Create(new FileVaultOptions { EnableBackgroundProcessing = true });
        storage.BasePath.Returns(Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid()));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        using var svc = new VaultBackgroundService(
            NullLogger<VaultBackgroundService>.Instance,
            fake, scopeFactory, storage, options);

        // Act
        _ = svc.StartAsync(cts.Token);
        await Task.Delay(250);

        // Assert — paused service must not call DequeueAsync
        fake.DequeueCount.Should().Be(0, "IsPaused=true means no dequeue attempts");

        await svc.StopAsync(CancellationToken.None);
    }

    // Minimal fake that gives us control over events and timing
    private sealed class FakeQueueService : IVaultQueueService, IDisposable
    {
        public bool IsPaused { get; set; }
        public event EventHandler<VaultJob>? JobEnqueued;
        public event EventHandler<VaultJob>? JobCompleted;

        private int _dequeueCount;
        public int DequeueCount => _dequeueCount;

        public DateTimeOffset SignalTime;
        public readonly TaskCompletionSource<TimeSpan> DequeueAfterSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void FireJobEnqueued(VaultJob job) => JobEnqueued?.Invoke(this, job);

        public Task<VaultJob?> DequeueAsync(CancellationToken ct = default)
        {
            var n = Interlocked.Increment(ref _dequeueCount);
            if (n >= 2 && SignalTime != DateTimeOffset.MinValue)
                DequeueAfterSignal.TrySetResult(DateTimeOffset.UtcNow - SignalTime);
            return Task.FromResult<VaultJob?>(null);
        }

        public Task<int> RecoverStuckJobsAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task UpdateCheckpointAsync(Guid jobId, int lastCompletedChunkIndex, CancellationToken ct = default) => Task.CompletedTask;

        // The remaining interface members are unused by VaultBackgroundService in these tests
        public Task<VaultJob> EnqueueMemorizeAsync(string h, string p, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<VaultJob> EnqueueMemorizeAsync(string h, string p, VaultJobPriority pr, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<VaultJob> EnqueueRefreshAsync(string h, string p, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<VaultJob> EnqueueRefreshAsync(string h, string p, VaultJobPriority pr, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<VaultJob> EnqueueRemoveAsync(string h, string p, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<VaultJob> EnqueueRemoveAsync(string h, string p, VaultJobPriority pr, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<VaultJob>> EnqueueBatchAsync(IEnumerable<(string, string)> files, VaultJobType jobType = VaultJobType.Memorize, VaultJobPriority priority = VaultJobPriority.Normal, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CompleteAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask;
        public Task FailAsync(Guid jobId, string errorMessage, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> RetryAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> CancelAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<VaultJob?> GetJobAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult<VaultJob?>(null);
        public Task<IReadOnlyList<VaultJob>> GetJobsAsync(VaultJobStatus? s = null, VaultJobType? t = null, int? limit = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<VaultJob>>(Array.Empty<VaultJob>());
        public Task<QueueStatistics> GetStatisticsAsync(CancellationToken ct = default) => Task.FromResult(new QueueStatistics());
        public Task<int> ClearCompletedAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> ClearFailedAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task ClearAllAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Pause() => IsPaused = true;
        public void ResumeProcessing() => IsPaused = false;

        public void Dispose() { }
    }
}
