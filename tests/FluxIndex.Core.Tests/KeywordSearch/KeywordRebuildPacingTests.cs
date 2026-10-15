using FluxIndex.Core.Application.Services.KeywordSearch;
using Xunit;

namespace FluxIndex.Core.Tests.KeywordSearch;

/// <summary>
/// How a keyword-index rebuild sizes its batches against the command timeout and waits after a failure.
/// </summary>
public class KeywordRebuildPacingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void StartsSmall_AndDoublesWhileBatchesFinishWellInsideTheTimeout_UpToTheMaximum()
    {
        var pacing = new KeywordRebuildPacing(Timeout);
        Assert.Equal(KeywordRebuildPacing.InitialBatchSize, pacing.BatchSize);

        var sizes = new List<int>();
        for (var i = 0; i < 6; i++)
        {
            pacing.Succeeded(TimeSpan.FromSeconds(1));
            sizes.Add(pacing.BatchSize);
        }

        Assert.Equal([100, 200, 400, 500, 500, 500], sizes);
    }

    [Fact]
    public void HoldsBetweenAQuarterAndHalfOfTheTimeout_AndHalvesAboveHalf()
    {
        var pacing = new KeywordRebuildPacing(Timeout);

        pacing.Succeeded(TimeSpan.FromSeconds(10));
        Assert.Equal(50, pacing.BatchSize);

        pacing.Succeeded(TimeSpan.FromSeconds(20));
        Assert.Equal(25, pacing.BatchSize);
    }

    [Fact]
    public void AFailure_HalvesTheSize_DownToOneChunk_AndWaitsLongerEachTime_UpToTheCap()
    {
        var pacing = new KeywordRebuildPacing(Timeout);

        var delays = new List<TimeSpan>();
        for (var i = 0; i < 12; i++)
            delays.Add(pacing.Failed());

        Assert.Equal(1, pacing.BatchSize);
        Assert.Equal(12, pacing.ConsecutiveFailures);
        Assert.Equal(TimeSpan.FromSeconds(1), delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(2), delays[1]);
        Assert.Equal(TimeSpan.FromSeconds(4), delays[2]);
        Assert.Equal(KeywordRebuildPacing.MaxRetryDelay, delays[^1]);
        Assert.All(delays, d => Assert.True(d <= KeywordRebuildPacing.MaxRetryDelay));
    }

    [Fact]
    public void ASuccessAfterFailures_ResetsTheCount_AndTheNextFailureWaitsTheFirstDelayAgain()
    {
        var pacing = new KeywordRebuildPacing(Timeout);
        pacing.Failed();
        pacing.Failed();

        pacing.Succeeded(TimeSpan.FromSeconds(1));

        Assert.Equal(0, pacing.ConsecutiveFailures);
        Assert.Equal(KeywordRebuildPacing.FirstRetryDelay, pacing.Failed());
    }

    [Fact]
    public void WithoutACommandTimeout_ItPacesAgainstThirtySeconds()
    {
        var pacing = new KeywordRebuildPacing(TimeSpan.Zero);

        pacing.Succeeded(TimeSpan.FromSeconds(5));
        Assert.Equal(100, pacing.BatchSize);

        pacing.Succeeded(TimeSpan.FromSeconds(20));
        Assert.Equal(50, pacing.BatchSize);
    }
}
