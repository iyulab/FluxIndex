using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Models;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary><c>SmallToBigOptions.MaxWindowSize</c> caps the window the search uses (it was declared and never read).</summary>
public class SmallToBigWindowSizeTests
{
    [Theory]
    [InlineData(true, 8, 10, 8)]   // adaptive, under the cap
    [InlineData(true, 8, 4, 4)]    // adaptive, capped
    [InlineData(false, 8, 2, 2)]   // fixed default 3, capped at 2
    [InlineData(false, 8, 10, 3)]  // fixed default 3
    public void WindowSize_IsTheRecommendationOrDefault_NeverAboveMaxWindowSize(bool adaptive, int recommended, int max, int expected)
    {
        var options = new SmallToBigOptions { EnableAdaptiveWindowing = adaptive, MaxWindowSize = max };
        Assert.Equal(expected, SmallToBigRetriever.WindowSize(options, recommended));
    }
}
