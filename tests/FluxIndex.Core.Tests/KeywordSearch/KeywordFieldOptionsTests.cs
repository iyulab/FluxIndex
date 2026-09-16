using AwesomeAssertions;
using FluxIndex.Core.Application.Services.KeywordSearch;
using Xunit;

namespace FluxIndex.Core.Tests.KeywordSearch;

/// <summary>
/// The field configuration rejects what the index could not act on: a blank key, the reserved body
/// name, a duplicate, a non-positive weight. The default is the two keys the SDK and FluxFeed write.
/// </summary>
public class KeywordFieldOptionsTests
{
    [Fact]
    public void Default_ScoresTitleAndFileName_AtWeightOne()
    {
        var options = new KeywordFieldOptions();

        options.Fields.Select(f => (f.MetadataKey, f.Weight))
            .Should().Equal([("title", 1.0), ("file_name", 1.0)]);
    }

    [Fact]
    public void None_HasNoFields()
    {
        KeywordFieldOptions.None.Fields.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("content")]
    public void AnUnusableKey_IsRejected(string key)
    {
        var act = () => new KeywordFieldOptions { Fields = [new KeywordField(key)] };

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ADuplicateKey_IsRejected()
    {
        var act = () => new KeywordFieldOptions { Fields = [new KeywordField("title"), new KeywordField("title", 2)] };

        act.Should().Throw<ArgumentException>().WithMessage("*title*more than once*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    public void ANonPositiveWeight_IsRejected(double weight)
    {
        var act = () => new KeywordFieldOptions { Fields = [new KeywordField("title", weight)] };

        act.Should().Throw<ArgumentException>();
    }
}
