using AwesomeAssertions;
using FluxIndex.Storage.PostgreSQL.EntityGraph;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// <see cref="EntityGraphOptions.AutoMigrate"/> decides whether host start provisions the entity graph schema. Before
/// 0.72.0 the option was copied by the registration and read by nothing, so «true» (the default) provisioned nothing.
/// Docker-free: the host is unreachable, so an attempt to provision shows as a failure to connect — the
/// positive control that makes «off touches nothing» mean something.
/// </summary>
public class PostgresEntityGraphAutoMigrateTests
{
    private const string Unreachable = "Host=192.0.2.1;Port=1;Database=flux;Username=u;Password=p;Timeout=1";

    private static IHostedService[] HostedServices(bool autoMigrate, out ServiceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgresEntityGraph(Unreachable, o => o.AutoMigrate = autoMigrate);
        provider = services.BuildServiceProvider();
        return provider.GetServices<IHostedService>().ToArray();
    }

    [Fact]
    public async Task HostStart_WithAutoMigrateOff_TouchesNothing_NotEvenAConnection()
    {
        var hosted = HostedServices(autoMigrate: false, out var provider);
        await using var _ = provider;

        hosted.Should().ContainSingle("the entity graph registers its schema provisioning as a hosted service");
        var act = () => hosted[0].StartAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task HostStart_WithAutoMigrateOn_Provisions_SoAnUnreachableServerFailsTheStart()
    {
        var hosted = HostedServices(autoMigrate: true, out var provider);
        await using var _ = provider;

        var service = hosted.Should().ContainSingle().Subject;
        var act = () => service.StartAsync(TestContext.Current.CancellationToken);

        // A connection failure, not any exception: the assertion must fail if provisioning is skipped for another reason.
        (await act.Should().ThrowAsync<Exception>("AutoMigrate on means host start provisions the schema, which needs the server"))
            .Which.ToString().Should().MatchRegex("(?i)npgsql|connect|timeout|socket");
    }
}
