using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Application.Services.Quantization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Docker-free tests for the quantizer collaborator that the quantized store requires. Direct
/// registration used to leave <see cref="IVectorQuantizer"/> unregistered, so the store could not
/// even be activated; the SDK builder never registered one either, so no path supplied a default.
/// </summary>
public class PostgreSQLQuantizerRegistrationTests
{
    private const string ConnectionString = "Host=localhost;Database=flux;Username=u;Password=p";

    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    [Fact]
    public void AddPostgreSQLQuantizedVectorStore_RegistersADefaultQuantizer()
    {
        var services = NewServices();

        services.AddPostgreSQLQuantizedVectorStore(ConnectionString);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IVectorQuantizer>().Should().BeOfType<ScalarQuantizer>(
            "the library already declares ScalarInt8 as QuantizationOptions' default type");
    }

    [Fact]
    public void AddPostgreSQLQuantizedVectorStore_KeepsAQuantizerRegisteredBeforeIt()
    {
        var services = NewServices();
        services.AddVectorQuantization(options => options.Type = QuantizationType.Binary);

        services.AddPostgreSQLQuantizedVectorStore(ConnectionString);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IVectorQuantizer>().Should().BeOfType<BinaryQuantizer>();
    }

    [Fact]
    public void AddPostgreSQLQuantizedVectorStore_KeepsAQuantizerRegisteredAfterIt()
    {
        var services = NewServices();
        services.AddPostgreSQLQuantizedVectorStore(ConnectionString);

        services.AddVectorQuantization(options => options.Type = QuantizationType.Binary);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IVectorQuantizer>().Should().BeOfType<BinaryQuantizer>();
    }

    [Fact]
    public void AddPostgreSQLQuantizedVectorStore_ActivatesTheStoreWithoutAnyExtraRegistration()
    {
        var services = NewServices();

        services.AddPostgreSQLQuantizedVectorStore(ConnectionString);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IQuantizedVectorStore>().Should().NotBeNull();
    }
}
