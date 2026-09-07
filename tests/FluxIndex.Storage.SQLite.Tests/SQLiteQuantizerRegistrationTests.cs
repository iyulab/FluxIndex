using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Application.Services.Quantization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// The SQLite quantized store carries the same gap the PostgreSQL one did: it requires an
/// <see cref="IVectorQuantizer"/> but its registration extension never supplied one, so the store
/// could not be activated without the consumer knowing to register a quantizer itself.
/// </summary>
public class SQLiteQuantizerRegistrationTests
{
    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    [Fact]
    public void AddSQLiteQuantizedVectorStore_RegistersADefaultQuantizer()
    {
        var services = NewServices();

        services.AddSQLiteQuantizedVectorStore();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IVectorQuantizer>().Should().BeOfType<ScalarQuantizer>(
            "the library already declares ScalarInt8 as QuantizationOptions' default type");
    }

    [Fact]
    public void AddSQLiteQuantizedVectorStore_KeepsAQuantizerRegisteredBeforeIt()
    {
        var services = NewServices();
        services.AddVectorQuantization(options => options.Type = QuantizationType.Binary);

        services.AddSQLiteQuantizedVectorStore();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IVectorQuantizer>().Should().BeOfType<BinaryQuantizer>();
    }

    [Fact]
    public void AddSQLiteQuantizedVectorStore_ActivatesTheStoreWithoutAnyExtraRegistration()
    {
        var services = NewServices();

        services.AddSQLiteQuantizedVectorStore();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IQuantizedVectorStore>().Should().NotBeNull();
    }
}
