using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK.Configuration;
using FluxIndex.SDK.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// The three <c>AddFluxIndex</c> overloads used to disagree. Only the <see cref="IConfiguration"/> one registered the
/// keyword and hybrid services, and it did so with <c>Add*</c>, so it replaced a keyword index the caller had
/// registered first. The <c>Action</c> and parameterless overloads registered neither. All three now share one set of
/// defaults, registered with <c>TryAdd</c>.
/// </summary>
public class AddFluxIndexRegistrationTests
{
    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    public static TheoryData<string> Overloads => ["configuration", "action", "parameterless"];

    private static IServiceCollection Add(IServiceCollection services, string overload) => overload switch
    {
        "configuration" => services.AddFluxIndex(EmptyConfiguration()),
        "action" => services.AddFluxIndex(o => o.Cache.EnableSearchCache = false),
        _ => services.AddFluxIndex(),
    };

    [Theory]
    [MemberData(nameof(Overloads))]
    public void EveryOverload_RegistersTheSameSearchDefaults(string overload)
    {
        var services = Add(new ServiceCollection(), overload);

        services.Should().ContainSingle(d => d.ServiceType == typeof(IKeywordSearchService))
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton, "the default keyword index lives in process memory");
        services.Should().ContainSingle(d => d.ServiceType == typeof(IHybridSearchService));
        services.Should().ContainSingle(d => d.ServiceType == typeof(FluxIndexOptions));
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public void AKeywordIndexRegisteredFirst_IsKept(string overload)
    {
        var mine = Substitute.For<IKeywordSearchService>();
        var services = new ServiceCollection().AddSingleton(mine);

        Add(services, overload);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IKeywordSearchService>().Should().BeSameAs(mine);
    }

    [Fact]
    public void CallingItTwice_DoesNotDuplicateTheDefaults()
    {
        var services = new ServiceCollection();

        services.AddFluxIndex();
        services.AddFluxIndex(EmptyConfiguration());

        services.Count(d => d.ServiceType == typeof(IKeywordSearchService)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(IHybridSearchService)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(FluxIndexOptions)).Should().Be(1);
    }
}
