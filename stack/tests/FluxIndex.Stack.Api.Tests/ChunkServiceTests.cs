using FluentAssertions;
using FluxIndex.Core.Interfaces;
using FluxIndex.Core.Models;
using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Services;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Shared.DTOs.Chunks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.Stack.Api.Tests;

/// <summary>
/// Unit tests for ChunkService.EnrichAsync with metadata extraction.
/// </summary>
public class ChunkServiceTests
{
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IRuleBasedMetadataExtractor _metadataExtractor;
    private readonly ChunkService _serviceWithExtractor;
    private readonly ChunkService _serviceWithoutExtractor;

    public ChunkServiceTests()
    {
        _chunkRepository = Substitute.For<IDocumentChunkRepository>();
        _documentRepository = Substitute.For<IDocumentRepository>();
        _metadataExtractor = Substitute.For<IRuleBasedMetadataExtractor>();
        var logger = Substitute.For<ILogger<ChunkService>>();

        _serviceWithExtractor = new ChunkService(
            _chunkRepository, _documentRepository, logger,
            metadataExtractor: _metadataExtractor);

        _serviceWithoutExtractor = new ChunkService(
            _chunkRepository, _documentRepository, logger);
    }

    [Fact]
    public async Task Enrich_WithNoExtractor_ReturnsBasicMetadata()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var chunk = DocumentChunk.Create(Guid.NewGuid(), 0, "Sample content for testing", 0, 25);
        _chunkRepository.GetByIdAsync(chunkId, Arg.Any<CancellationToken>()).Returns(chunk);

        var request = new EnrichChunkRequest();

        // Act
        var result = await _serviceWithoutExtractor.EnrichAsync(chunkId, request);

        // Assert
        result.Success.Should().BeTrue();
        result.EnrichedMetadata.Should().ContainKey("word_count");
        result.EnrichedMetadata.Should().ContainKey("character_count");
        result.EnrichedMetadata.Should().NotContainKey("topics");
        result.EnrichedMetadata.Should().NotContainKey("keywords");
    }

    [Fact]
    public async Task Enrich_WithExtractor_IncludesExtractedMetadata()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var chunk = DocumentChunk.Create(Guid.NewGuid(), 0, "Sample content about machine learning and AI", 0, 45);
        _chunkRepository.GetByIdAsync(chunkId, Arg.Any<CancellationToken>()).Returns(chunk);

        var extracted = new ExtractedMetadata
        {
            Topics = ["machine learning", "AI"],
            Keywords = ["ML", "AI", "learning"],
            Description = "Content about ML and AI",
            DocumentType = "article",
            Language = "en",
            Categories = ["technology"],
            OverallConfidence = 0.75f,
            ExtractionMethod = "RuleBased"
        };

        _metadataExtractor.ExtractAsync(
                Arg.Any<string>(), Arg.Any<MetadataSchema>(), Arg.Any<CancellationToken>())
            .Returns(extracted);

        var request = new EnrichChunkRequest();

        // Act
        var result = await _serviceWithExtractor.EnrichAsync(chunkId, request);

        // Assert
        result.Success.Should().BeTrue();
        result.EnrichedMetadata.Should().ContainKey("topics");
        result.EnrichedMetadata.Should().ContainKey("keywords");
        result.EnrichedMetadata.Should().ContainKey("description");
        result.EnrichedMetadata.Should().ContainKey("document_type");
        result.EnrichedMetadata.Should().ContainKey("language");
        result.EnrichedMetadata.Should().ContainKey("categories");
        result.EnrichedMetadata.Should().ContainKey("extraction_method");
        result.EnrichedMetadata.Should().ContainKey("extraction_confidence");
        result.EnrichedMetadata["extraction_confidence"].Should().Be(0.75f);
    }

    [Fact]
    public async Task Enrich_WithSchemaName_ParsesCorrectSchema()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var chunk = DocumentChunk.Create(Guid.NewGuid(), 0, "Technical documentation content", 0, 30);
        _chunkRepository.GetByIdAsync(chunkId, Arg.Any<CancellationToken>()).Returns(chunk);

        var extracted = new ExtractedMetadata
        {
            Topics = ["docs"],
            Keywords = ["technical"],
            ExtractionMethod = "RuleBased",
            OverallConfidence = 0.6f
        };

        _metadataExtractor.ExtractAsync(
                Arg.Any<string>(), MetadataSchema.TechnicalDoc, Arg.Any<CancellationToken>())
            .Returns(extracted);

        var request = new EnrichChunkRequest { MetadataSchema = "technical_doc" };

        // Act
        var result = await _serviceWithExtractor.EnrichAsync(chunkId, request);

        // Assert
        result.Success.Should().BeTrue();
        await _metadataExtractor.Received(1).ExtractAsync(
            Arg.Any<string>(), MetadataSchema.TechnicalDoc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enrich_WithNullSchema_DefaultsToGeneral()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var chunk = DocumentChunk.Create(Guid.NewGuid(), 0, "Generic content here", 0, 20);
        _chunkRepository.GetByIdAsync(chunkId, Arg.Any<CancellationToken>()).Returns(chunk);

        var extracted = new ExtractedMetadata
        {
            ExtractionMethod = "RuleBased",
            OverallConfidence = 0.5f
        };

        _metadataExtractor.ExtractAsync(
                Arg.Any<string>(), MetadataSchema.General, Arg.Any<CancellationToken>())
            .Returns(extracted);

        var request = new EnrichChunkRequest();

        // Act
        var result = await _serviceWithExtractor.EnrichAsync(chunkId, request);

        // Assert
        result.Success.Should().BeTrue();
        await _metadataExtractor.Received(1).ExtractAsync(
            Arg.Any<string>(), MetadataSchema.General, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enrich_WithContext_IncludesContextInMetadata()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var chunk = DocumentChunk.Create(Guid.NewGuid(), 0, "Content with context", 0, 20);
        _chunkRepository.GetByIdAsync(chunkId, Arg.Any<CancellationToken>()).Returns(chunk);

        var request = new EnrichChunkRequest { Context = "This is a product manual" };

        // Act
        var result = await _serviceWithoutExtractor.EnrichAsync(chunkId, request);

        // Assert
        result.Success.Should().BeTrue();
        result.EnrichedMetadata.Should().ContainKey("context");
        result.EnrichedMetadata["context"].Should().Be("This is a product manual");
    }

    [Fact]
    public async Task Enrich_ChunkNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        _chunkRepository.GetByIdAsync(chunkId, Arg.Any<CancellationToken>())
            .Returns((DocumentChunk?)null);

        var request = new EnrichChunkRequest();

        // Act
        var act = () => _serviceWithExtractor.EnrichAsync(chunkId, request);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Enrich_SkipsEmptyExtractedFields()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var chunk = DocumentChunk.Create(Guid.NewGuid(), 0, "Minimal content", 0, 15);
        _chunkRepository.GetByIdAsync(chunkId, Arg.Any<CancellationToken>()).Returns(chunk);

        // Return mostly empty extraction result
        var extracted = new ExtractedMetadata
        {
            Topics = [],
            Keywords = [],
            Description = "",
            DocumentType = "",
            Language = "",
            Categories = [],
            SchemaSpecificData = new Dictionary<string, object>(),
            ExtractionMethod = "RuleBased",
            OverallConfidence = 0.3f
        };

        _metadataExtractor.ExtractAsync(
                Arg.Any<string>(), Arg.Any<MetadataSchema>(), Arg.Any<CancellationToken>())
            .Returns(extracted);

        var request = new EnrichChunkRequest();

        // Act
        var result = await _serviceWithExtractor.EnrichAsync(chunkId, request);

        // Assert
        result.Success.Should().BeTrue();
        result.EnrichedMetadata.Should().NotContainKey("topics");
        result.EnrichedMetadata.Should().NotContainKey("keywords");
        result.EnrichedMetadata.Should().NotContainKey("description");
        result.EnrichedMetadata.Should().ContainKey("extraction_method");
        result.EnrichedMetadata.Should().ContainKey("extraction_confidence");
    }

    [Theory]
    [InlineData("general", MetadataSchema.General)]
    [InlineData("article", MetadataSchema.Article)]
    [InlineData("productmanual", MetadataSchema.ProductManual)]
    [InlineData("product_manual", MetadataSchema.ProductManual)]
    [InlineData("technicaldoc", MetadataSchema.TechnicalDoc)]
    [InlineData("technical_doc", MetadataSchema.TechnicalDoc)]
    [InlineData("custom", MetadataSchema.Custom)]
    [InlineData("unknown_schema", MetadataSchema.General)]
    [InlineData(null, MetadataSchema.General)]
    public async Task Enrich_ParsesAllSchemaVariants(string? schemaName, MetadataSchema expectedSchema)
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var chunk = DocumentChunk.Create(Guid.NewGuid(), 0, "Content for schema test", 0, 22);
        _chunkRepository.GetByIdAsync(chunkId, Arg.Any<CancellationToken>()).Returns(chunk);

        var extracted = new ExtractedMetadata
        {
            ExtractionMethod = "RuleBased",
            OverallConfidence = 0.5f
        };

        _metadataExtractor.ExtractAsync(
                Arg.Any<string>(), expectedSchema, Arg.Any<CancellationToken>())
            .Returns(extracted);

        var request = new EnrichChunkRequest { MetadataSchema = schemaName };

        // Act
        var result = await _serviceWithExtractor.EnrichAsync(chunkId, request);

        // Assert
        result.Success.Should().BeTrue();
        await _metadataExtractor.Received(1).ExtractAsync(
            Arg.Any<string>(), expectedSchema, Arg.Any<CancellationToken>());
    }
}
