using FluentAssertions;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Options;
using FluxIndex.Extensions.FileVault.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace FluxIndex.Extensions.FileVault.Tests.Services;

/// <summary>
/// MU-2 regression tests: <see cref="VaultQueueService.WaitForJobAsync"/> is the signal-driven
/// (no-poll) terminal-await primitive. It must resolve on Completed/Failed/Cancelled, resolve
/// immediately for an already-terminal job (race-free), and honor cancellation.
/// </summary>
public class VaultQueueWaitForJobTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileVaultOptions _options;

    public VaultQueueWaitForJobTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "VaultWaitForJobTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _options = new FileVaultOptions { VaultBasePath = _testDir };
    }

    public void Dispose()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch { /* best-effort */ }
    }

    private VaultQueueService CreateService() =>
        new(NullLogger<VaultQueueService>.Instance, MsOptions.Create(_options));

    [Fact]
    public async Task WaitForJobAsync_ResolvesOnComplete_WithoutPolling()
    {
        using var queue = CreateService();
        var job = await queue.EnqueueMemorizeAsync("hash1", Path.Combine(_testDir, "a.txt"));
        await queue.DequeueAsync(); // Queued → Processing

        // Begin waiting BEFORE completion (the production background-memorize ordering).
        var waitTask = queue.WaitForJobAsync(job.Id);
        waitTask.IsCompleted.Should().BeFalse("the job has not reached a terminal state yet");

        await queue.CompleteAsync(job.Id);

        var terminal = await waitTask.WaitAsync(TimeSpan.FromSeconds(2));
        terminal.Status.Should().Be(VaultJobStatus.Completed);
        terminal.Id.Should().Be(job.Id);
    }

    [Fact]
    public async Task WaitForJobAsync_ResolvesImmediately_WhenAlreadyTerminal()
    {
        using var queue = CreateService();
        var job = await queue.EnqueueMemorizeAsync("hash2", Path.Combine(_testDir, "b.txt"));
        await queue.DequeueAsync();
        await queue.CompleteAsync(job.Id); // terminal BEFORE anyone waits

        // Race-free: must resolve immediately, not hang waiting for a signal that already fired.
        var terminal = await queue.WaitForJobAsync(job.Id).WaitAsync(TimeSpan.FromSeconds(2));
        terminal.Status.Should().Be(VaultJobStatus.Completed);
    }

    [Fact]
    public async Task WaitForJobAsync_ResolvesOnFailure()
    {
        using var queue = CreateService();
        var job = await queue.EnqueueMemorizeAsync("hash3", Path.Combine(_testDir, "c.txt"));
        await queue.DequeueAsync();

        var waitTask = queue.WaitForJobAsync(job.Id);
        await queue.FailAsync(job.Id, "boom");

        var terminal = await waitTask.WaitAsync(TimeSpan.FromSeconds(2));
        terminal.Status.Should().Be(VaultJobStatus.Failed);
        terminal.ErrorMessage.Should().Be("boom");
    }

    [Fact]
    public async Task WaitForJobAsync_ResolvesOnCancel()
    {
        using var queue = CreateService();
        var job = await queue.EnqueueMemorizeAsync("hash4", Path.Combine(_testDir, "d.txt"));

        var waitTask = queue.WaitForJobAsync(job.Id);
        (await queue.CancelAsync(job.Id)).Should().BeTrue();

        var terminal = await waitTask.WaitAsync(TimeSpan.FromSeconds(2));
        terminal.Status.Should().Be(VaultJobStatus.Cancelled);
    }

    [Fact]
    public async Task WaitForJobAsync_Throws_WhenJobUnknown()
    {
        using var queue = CreateService();
        var act = async () => await queue.WaitForJobAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task WaitForJobAsync_HonorsCancellation()
    {
        using var queue = CreateService();
        var job = await queue.EnqueueMemorizeAsync("hash5", Path.Combine(_testDir, "e.txt"));
        await queue.DequeueAsync();

        using var cts = new CancellationTokenSource();
        var waitTask = queue.WaitForJobAsync(job.Id, cts.Token);
        cts.Cancel();

        var act = async () => await waitTask.WaitAsync(TimeSpan.FromSeconds(2));
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
