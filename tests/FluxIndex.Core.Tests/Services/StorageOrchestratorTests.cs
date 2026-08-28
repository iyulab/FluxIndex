using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Storage;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// Unit tests for StorageOrchestrator.
/// </summary>
public class StorageOrchestratorTests
{
    #region Test Providers

    private class TestGeneralProvider : IStorageProvider, IVectorCapable, IGraphCapable, ISemanticCacheCapable
    {
        public string ProviderName { get; set; } = "TestGeneral";
        public StorageCapabilities Capabilities => StorageCapabilities.Vector | StorageCapabilities.Graph | StorageCapabilities.SemanticCache;
        public bool IsSpecialized => false;
        public IVectorStore VectorStore { get; set; } = Substitute.For<IVectorStore>();
        public IGraphStore GraphStore { get; set; } = Substitute.For<IGraphStore>();
        public ISemanticCacheService SemanticCache { get; set; } = Substitute.For<ISemanticCacheService>();
    }

    private class TestSpecializedVectorProvider : IStorageProvider, IVectorCapable
    {
        public string ProviderName { get; set; } = "TestSpecializedVector";
        public StorageCapabilities Capabilities => StorageCapabilities.Vector;
        public bool IsSpecialized => true;
        public IVectorStore VectorStore { get; set; } = Substitute.For<IVectorStore>();
    }

    private class TestSpecializedGraphProvider : IStorageProvider, IGraphCapable
    {
        public string ProviderName { get; set; } = "TestSpecializedGraph";
        public StorageCapabilities Capabilities => StorageCapabilities.Graph;
        public bool IsSpecialized => true;
        public IGraphStore GraphStore { get; set; } = Substitute.For<IGraphStore>();
    }

    #endregion

    [Fact]
    public void Constructor_WithNoProviders_ReturnsNullServices()
    {
        // Arrange
        var providers = Enumerable.Empty<IStorageProvider>();

        // Act
        var orchestrator = new StorageOrchestrator(providers);

        // Assert
        orchestrator.VectorStore.Should().BeNull();
        orchestrator.GraphStore.Should().BeNull();
        orchestrator.SemanticCache.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithGeneralProvider_ReturnsAllServices()
    {
        // Arrange
        var generalProvider = new TestGeneralProvider();
        var providers = new IStorageProvider[] { generalProvider };

        // Act
        var orchestrator = new StorageOrchestrator(providers);

        // Assert
        orchestrator.VectorStore.Should().NotBeNull();
        orchestrator.GraphStore.Should().NotBeNull();
        orchestrator.SemanticCache.Should().NotBeNull();

        var config = orchestrator.GetConfiguration();
        config.VectorProvider.Should().Be("TestGeneral");
        config.GraphProvider.Should().Be("TestGeneral");
        config.SemanticCacheProvider.Should().Be("TestGeneral");
    }

    [Fact]
    public void Constructor_WithSpecializedAndGeneralProvider_SpecializedTakesPriority()
    {
        // Arrange
        var generalProvider = new TestGeneralProvider { ProviderName = "SQLite" };
        var specializedVector = new TestSpecializedVectorProvider { ProviderName = "Qdrant" };
        var providers = new IStorageProvider[] { generalProvider, specializedVector };

        // Act
        var orchestrator = new StorageOrchestrator(providers);

        // Assert
        var config = orchestrator.GetConfiguration();

        // Qdrant takes priority for Vector (specialized)
        config.VectorProvider.Should().Be("Qdrant");

        // SQLite still handles Graph and Cache
        config.GraphProvider.Should().Be("SQLite");
        config.SemanticCacheProvider.Should().Be("SQLite");
    }

    [Fact]
    public void Constructor_WithMultipleSpecializedProviders_LastRegisteredWins()
    {
        // Arrange
        var specializedVector1 = new TestSpecializedVectorProvider { ProviderName = "Qdrant1" };
        var specializedVector2 = new TestSpecializedVectorProvider { ProviderName = "Qdrant2" };
        var providers = new IStorageProvider[] { specializedVector1, specializedVector2 };

        // Act
        var orchestrator = new StorageOrchestrator(providers);

        // Assert
        var config = orchestrator.GetConfiguration();

        // Last registered specialized provider wins
        config.VectorProvider.Should().Be("Qdrant2");
    }

    [Fact]
    public void Constructor_BestInClassConfiguration_CorrectlyDistributes()
    {
        // Arrange
        var postgres = new TestGeneralProvider { ProviderName = "PostgreSQL" };
        var qdrant = new TestSpecializedVectorProvider { ProviderName = "Qdrant" };
        var neo4j = new TestSpecializedGraphProvider { ProviderName = "Neo4j" };
        var providers = new IStorageProvider[] { postgres, qdrant, neo4j };

        // Act
        var orchestrator = new StorageOrchestrator(providers);

        // Assert
        var config = orchestrator.GetConfiguration();

        // Qdrant handles Vector (specialized)
        config.VectorProvider.Should().Be("Qdrant");

        // Neo4j handles Graph (specialized)
        config.GraphProvider.Should().Be("Neo4j");

        // PostgreSQL handles SemanticCache (general-purpose, no specialized available)
        config.SemanticCacheProvider.Should().Be("PostgreSQL");
    }

    [Fact]
    public void GetConfiguration_ReturnsCorrectCapabilities()
    {
        // Arrange
        var generalProvider = new TestGeneralProvider();
        var providers = new IStorageProvider[] { generalProvider };
        var orchestrator = new StorageOrchestrator(providers);

        // Act
        var config = orchestrator.GetConfiguration();

        // Assert
        config.HasVector.Should().BeTrue();
        config.HasGraph.Should().BeTrue();
        config.HasSemanticCache.Should().BeTrue();
        config.HasRdb.Should().BeFalse(); // No RDB provider registered
    }

    [Fact]
    public void GetProviders_ReturnsAllRegisteredProviders()
    {
        // Arrange
        var generalProvider = new TestGeneralProvider();
        var specializedVector = new TestSpecializedVectorProvider();
        var providers = new IStorageProvider[] { generalProvider, specializedVector };

        // Act
        var orchestrator = new StorageOrchestrator(providers);
        var registeredProviders = orchestrator.GetProviders();

        // Assert
        registeredProviders.Should().HaveCount(2);
        registeredProviders.Should().Contain(generalProvider);
        registeredProviders.Should().Contain(specializedVector);
    }

    [Fact]
    public void StorageConfiguration_ToString_ReturnsReadableSummary()
    {
        // Arrange
        var generalProvider = new TestGeneralProvider { ProviderName = "SQLite" };
        var providers = new IStorageProvider[] { generalProvider };
        var orchestrator = new StorageOrchestrator(providers);

        // Act
        var config = orchestrator.GetConfiguration();
        var summary = config.ToString();

        // Assert
        summary.Should().Contain("Vector=SQLite");
        summary.Should().Contain("Graph=SQLite");
        summary.Should().Contain("Cache=SQLite");
    }

    [Fact]
    public void StorageConfiguration_EmptyConfiguration_ReturnsNoStorageMessage()
    {
        // Arrange
        var config = new StorageConfiguration();

        // Act
        var summary = config.ToString();

        // Assert
        summary.Should().Be("No storage configured");
    }
}
