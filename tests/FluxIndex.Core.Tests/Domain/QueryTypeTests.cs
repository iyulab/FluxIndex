using FluxIndex.Core.Domain.Models;
using Xunit;

namespace FluxIndex.Core.Tests.Domain;

public class QueryTypeTests
{
    [Fact]
    public void QueryType_HasNineValues()
    {
        var values = Enum.GetValues<QueryType>();

        Assert.Equal(9, values.Length);
    }
}
