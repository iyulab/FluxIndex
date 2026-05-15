using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace FluxIndex.Core.Tests.Services.EntityExtraction;

/// <summary>
/// Unit tests for EntityExtractionService covering entity extraction, relation detection, and graph building.
/// </summary>
public class EntityExtractionServiceTests
{
    private readonly ILogger<EntityExtractionService> _loggerMock;
    private readonly ITextCompletionService _llmServiceMock;

    public EntityExtractionServiceTests()
    {
        _loggerMock = Substitute.For<ILogger<EntityExtractionService>>();
        _llmServiceMock = Substitute.For<ITextCompletionService>();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithLogger_Succeeds()
    {
        // Act
        var service = new EntityExtractionService(_loggerMock);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithLoggerAndLlmService_Succeeds()
    {
        // Act
        var service = new EntityExtractionService(_loggerMock, _llmServiceMock);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new EntityExtractionService(null!));
    }

    #endregion

    #region ExtractEntitiesAsync Tests

    [Fact]
    public async Task ExtractEntitiesAsync_EmptyContent_ReturnsEmptyList()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);

        // Act
        var result = await service.ExtractEntitiesAsync("");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_NullContent_ReturnsEmptyList()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);

        // Act
        var result = await service.ExtractEntitiesAsync(null!);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_ExtractsEmailAddresses()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Contact us at support@example.com for more information.";

        // Act
        var result = await service.ExtractEntitiesAsync(content);

        // Assert
        Assert.Contains(result, e => e.Type == NamedEntityType.Email && e.Text.Contains("support@example.com"));
    }

    [Fact]
    public async Task ExtractEntitiesAsync_ExtractsUrls()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Visit https://www.example.com for more details.";

        // Act
        var result = await service.ExtractEntitiesAsync(content);

        // Assert
        Assert.Contains(result, e => e.Type == NamedEntityType.Url && e.Text.Contains("https://www.example.com"));
    }

    [Fact]
    public async Task ExtractEntitiesAsync_ExtractsPhoneNumbers()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Call us at +1-800-555-1234 for support.";

        // Act
        var result = await service.ExtractEntitiesAsync(content);

        // Assert
        Assert.Contains(result, e => e.Type == NamedEntityType.PhoneNumber);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_ExtractsDates()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "The event was held on January 15, 2024 at the convention center.";

        // Act
        var result = await service.ExtractEntitiesAsync(content);

        // Assert
        Assert.Contains(result, e => e.Type == NamedEntityType.DateTime);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_ExtractsMoney()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "The product costs $99.99 plus tax.";

        // Act
        var result = await service.ExtractEntitiesAsync(content);

        // Assert
        Assert.Contains(result, e => e.Type == NamedEntityType.Money && e.Text.Contains("$99.99"));
    }

    [Fact]
    public async Task ExtractEntitiesAsync_ExtractsPercentages()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Sales increased by 25.5% last quarter.";

        // Act
        var result = await service.ExtractEntitiesAsync(content);

        // Assert
        Assert.Contains(result, e => e.Type == NamedEntityType.Percentage && e.Text.Contains("25.5%"));
    }

    [Fact]
    public async Task ExtractEntitiesAsync_ExtractsTechnologies()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "We use Python and React for our development stack.";

        // Act
        var result = await service.ExtractEntitiesAsync(content);

        // Assert
        Assert.Contains(result, e => e.Type == NamedEntityType.Technology && e.Text == "Python");
        Assert.Contains(result, e => e.Type == NamedEntityType.Technology && e.Text == "React");
    }

    [Fact]
    public async Task ExtractEntitiesAsync_ExtractsQuantities()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "The file size is 100MB and transfer speed is 10Mbps.";

        // Act
        var result = await service.ExtractEntitiesAsync(content);

        // Assert
        Assert.Contains(result, e => e.Type == NamedEntityType.Quantity);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_ExtractsOrganizations()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Microsoft Corp announced new products today.";

        // Act
        var result = await service.ExtractEntitiesAsync(content);

        // Assert
        Assert.Contains(result, e => e.Type == NamedEntityType.Organization && e.Text.Contains("Microsoft"));
    }

    [Fact]
    public async Task ExtractEntitiesAsync_WithOptions_FiltersEntityTypes()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Contact us at support@example.com. Visit https://example.com.";
        var options = new EntityExtractionOptions
        {
            EntityTypes = new List<NamedEntityType> { NamedEntityType.Email }
        };

        // Act
        var result = await service.ExtractEntitiesAsync(content, options);

        // Assert
        Assert.All(result, e => Assert.Equal(NamedEntityType.Email, e.Type));
    }

    [Fact]
    public async Task ExtractEntitiesAsync_WithMinConfidence_FiltersLowConfidenceEntities()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "John Smith works at Microsoft Corp.";
        var options = new EntityExtractionOptions
        {
            MinConfidence = 0.8
        };

        // Act
        var result = await service.ExtractEntitiesAsync(content, options);

        // Assert
        Assert.All(result, e => Assert.True(e.Confidence >= 0.8));
    }

    [Fact]
    public async Task ExtractEntitiesAsync_WithMaxEntities_LimitsResults()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Python, JavaScript, TypeScript, Java, C#, Go, Rust are popular languages.";
        var options = new EntityExtractionOptions
        {
            MaxEntities = 3
        };

        // Act
        var result = await service.ExtractEntitiesAsync(content, options);

        // Assert
        Assert.True(result.Count <= 3);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_WithContext_IncludesContextInResults()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Microsoft Corp announced new products today at the annual conference.";
        var options = new EntityExtractionOptions
        {
            IncludeContext = true,
            ContextWindowSize = 20
        };

        // Act
        var result = await service.ExtractEntitiesAsync(content, options);

        // Assert
        var orgEntity = result.FirstOrDefault(e => e.Type == NamedEntityType.Organization);
        Assert.NotNull(orgEntity?.Context);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_DeduplicatesEntities()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Python is great. I love Python. We use Python daily.";

        // Act
        var result = await service.ExtractEntitiesAsync(content);

        // Assert
        var pythonEntities = result.Where(e => e.Text == "Python").ToList();
        Assert.Single(pythonEntities);
        Assert.True(pythonEntities[0].OccurrenceCount >= 3);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_WithLlm_IncludesLlmExtractedEntities()
    {
        // Arrange
        _llmServiceMock.CompleteAsync(
                Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("[{\"text\": \"Apple\", \"type\": \"Organization\", \"confidence\": 0.95}]");

        var service = new EntityExtractionService(_loggerMock, _llmServiceMock);
        var content = "Apple announced new products today.";
        var options = new EntityExtractionOptions { UseLlm = true };

        // Act
        var result = await service.ExtractEntitiesAsync(content, options);

        // Assert
        await _llmServiceMock.Received(1).CompleteAsync(
            Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractEntitiesAsync_CancellationToken_IsPassed()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var service = new EntityExtractionService(_loggerMock);
        var content = "Test content";

        // Act
        var result = await service.ExtractEntitiesAsync(content, cancellationToken: cts.Token);

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region ExtractRelationsAsync Tests

    [Fact]
    public async Task ExtractRelationsAsync_EmptyContent_ReturnsEmptyList()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);

        // Act
        var result = await service.ExtractRelationsAsync("");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractRelationsAsync_SingleEntity_ReturnsEmptyList()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Python is popular.";

        // Act
        var result = await service.ExtractRelationsAsync(content);

        // Assert - may have 0 or 1 entity, so no relations
        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractRelationsAsync_WithProvidedEntities_UsesThoseEntities()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "John works for Microsoft.";
        var entities = new List<ExtractedEntity>
        {
            new() { Id = "1", Text = "John", Type = NamedEntityType.Person, Confidence = 0.9, NormalizedText = "John" },
            new() { Id = "2", Text = "Microsoft", Type = NamedEntityType.Organization, Confidence = 0.9, NormalizedText = "Microsoft" }
        };

        // Act
        var result = await service.ExtractRelationsAsync(content, entities);

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task ExtractRelationsAsync_DetectsWorksForRelation()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "John works for Microsoft Corp.";
        var entities = new List<ExtractedEntity>
        {
            new() { Id = "1", Text = "John", Type = NamedEntityType.Person, Confidence = 0.9, NormalizedText = "John" },
            new() { Id = "2", Text = "Microsoft Corp", Type = NamedEntityType.Organization, Confidence = 0.9, NormalizedText = "Microsoft Corp" }
        };

        // Act
        var result = await service.ExtractRelationsAsync(content, entities);

        // Assert
        Assert.Contains(result, r => r.Type == RelationType.WorksFor);
    }

    [Fact]
    public async Task ExtractRelationsAsync_DetectsLocatedInRelation()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Microsoft is located in Seattle.";
        var entities = new List<ExtractedEntity>
        {
            new() { Id = "1", Text = "Microsoft", Type = NamedEntityType.Organization, Confidence = 0.9, NormalizedText = "Microsoft" },
            new() { Id = "2", Text = "Seattle", Type = NamedEntityType.Location, Confidence = 0.9, NormalizedText = "Seattle" }
        };

        // Act
        var result = await service.ExtractRelationsAsync(content, entities);

        // Assert
        Assert.Contains(result, r => r.Type == RelationType.LocatedIn);
    }

    [Fact]
    public async Task ExtractRelationsAsync_DetectsFoundedByRelation()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Microsoft was founded by Bill Gates.";
        var entities = new List<ExtractedEntity>
        {
            new() { Id = "1", Text = "Microsoft", Type = NamedEntityType.Organization, Confidence = 0.9, NormalizedText = "Microsoft" },
            new() { Id = "2", Text = "Bill Gates", Type = NamedEntityType.Person, Confidence = 0.9, NormalizedText = "Bill Gates" }
        };

        // Act
        var result = await service.ExtractRelationsAsync(content, entities);

        // Assert
        Assert.Contains(result, r => r.Type == RelationType.FoundedBy);
    }

    [Fact]
    public async Task ExtractRelationsAsync_DetectsUsesRelation()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Our company uses Python for development.";
        var entities = new List<ExtractedEntity>
        {
            new() { Id = "1", Text = "company", Type = NamedEntityType.Organization, Confidence = 0.7, NormalizedText = "company" },
            new() { Id = "2", Text = "Python", Type = NamedEntityType.Technology, Confidence = 0.9, NormalizedText = "Python" }
        };

        // Act
        var result = await service.ExtractRelationsAsync(content, entities);

        // Assert
        Assert.Contains(result, r => r.Type == RelationType.Uses);
    }

    [Fact]
    public async Task ExtractRelationsAsync_DeduplicatesRelations()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "John works for Microsoft. John works for Microsoft.";
        var entities = new List<ExtractedEntity>
        {
            new() { Id = "1", Text = "John", Type = NamedEntityType.Person, Confidence = 0.9, NormalizedText = "John" },
            new() { Id = "2", Text = "Microsoft", Type = NamedEntityType.Organization, Confidence = 0.9, NormalizedText = "Microsoft" }
        };

        // Act
        var result = await service.ExtractRelationsAsync(content, entities);

        // Assert - should deduplicate same relations
        var worksForRelations = result.Where(r => r.Type == RelationType.WorksFor).ToList();
        Assert.Single(worksForRelations);
    }

    #endregion

    #region ExtractEntityGraphAsync Tests

    [Fact]
    public async Task ExtractEntityGraphAsync_EmptyContent_ReturnsEmptyGraph()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var options = new EntityExtractionOptions();

        // Act
        var result = await service.ExtractEntityGraphAsync("", options);

        // Assert
        Assert.Empty(result.Entities);
        Assert.Empty(result.Relations);
    }

    [Fact]
    public async Task ExtractEntityGraphAsync_ReturnsEntitiesAndRelations()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Microsoft Corp uses Python. Microsoft is located in Seattle.";
        var options = new EntityExtractionOptions { ExtractRelations = true };

        // Act
        var result = await service.ExtractEntityGraphAsync(content, options);

        // Assert
        Assert.NotEmpty(result.Entities);
        Assert.NotNull(result.Stats);
    }

    [Fact]
    public async Task ExtractEntityGraphAsync_IncludesStatistics()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Python and JavaScript are popular programming languages.";
        var options = new EntityExtractionOptions();

        // Act
        var result = await service.ExtractEntityGraphAsync(content, options);

        // Assert
        Assert.NotNull(result.Stats);
        Assert.True(result.Stats.ProcessingTimeMs >= 0);
        Assert.Equal(result.Entities.Count, result.Stats.TotalEntities);
    }

    [Fact]
    public async Task ExtractEntityGraphAsync_WithoutRelations_ReturnsEmptyRelations()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Python and JavaScript are popular.";
        var options = new EntityExtractionOptions { ExtractRelations = false };

        // Act
        var result = await service.ExtractEntityGraphAsync(content, options);

        // Assert
        Assert.Empty(result.Relations);
    }

    [Fact]
    public async Task ExtractEntityGraphAsync_GeneratesUniqueSourceId()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Test content";

        // Act
        var result1 = await service.ExtractEntityGraphAsync(content);
        var result2 = await service.ExtractEntityGraphAsync(content);

        // Assert
        Assert.NotEqual(result1.SourceId, result2.SourceId);
    }

    #endregion

    #region ExtractBatchAsync Tests

    [Fact]
    public async Task ExtractBatchAsync_EmptyInput_ReturnsEmptyList()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);

        // Act
        var result = await service.ExtractBatchAsync(Enumerable.Empty<string>());

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractBatchAsync_MultipleContents_ReturnsMultipleGraphs()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var contents = new[] { "Python is great.", "JavaScript is popular.", "C# is powerful." };

        // Act
        var result = await service.ExtractBatchAsync(contents);

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ExtractBatchAsync_RespectsCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = new EntityExtractionService(_loggerMock);
        var contents = new[] { "Test 1", "Test 2", "Test 3" };

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ExtractBatchAsync(contents, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ExtractBatchAsync_PassesOptions()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var contents = new[] { "support@example.com", "test@domain.com" };
        var options = new EntityExtractionOptions
        {
            EntityTypes = new List<NamedEntityType> { NamedEntityType.Email }
        };

        // Act
        var result = await service.ExtractBatchAsync(contents, options);

        // Assert
        Assert.All(result, graph =>
            Assert.All(graph.Entities, e => Assert.Equal(NamedEntityType.Email, e.Type)));
    }

    #endregion

    #region LinkEntitiesAsync Tests

    [Fact]
    public async Task LinkEntitiesAsync_EmptyGraphs_ReturnsEmptyLinkedGraph()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var graphs = Enumerable.Empty<EntityGraph>();

        // Act
        var result = await service.LinkEntitiesAsync(graphs);

        // Assert
        Assert.Empty(result.Entities);
        Assert.Empty(result.Relations);
    }

    [Fact]
    public async Task LinkEntitiesAsync_MergesIdenticalEntities()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var graphs = new List<EntityGraph>
        {
            new()
            {
                SourceId = "1",
                Entities = new List<ExtractedEntity>
                {
                    new() { Id = "e1", Text = "Python", NormalizedText = "Python", Type = NamedEntityType.Technology, Confidence = 0.9 }
                },
                Relations = new List<EntityRelation>()
            },
            new()
            {
                SourceId = "2",
                Entities = new List<ExtractedEntity>
                {
                    new() { Id = "e2", Text = "Python", NormalizedText = "Python", Type = NamedEntityType.Technology, Confidence = 0.85 }
                },
                Relations = new List<EntityRelation>()
            }
        };

        // Act
        var result = await service.LinkEntitiesAsync(graphs);

        // Assert
        Assert.Single(result.Entities);
        Assert.Contains(result.Entities, e => e.CanonicalText == "Python");
    }

    [Fact]
    public async Task LinkEntitiesAsync_CombinesSurfaceForms()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var graphs = new List<EntityGraph>
        {
            new()
            {
                SourceId = "1",
                Entities = new List<ExtractedEntity>
                {
                    new() { Id = "e1", Text = "Microsoft", NormalizedText = "Microsoft", Type = NamedEntityType.Organization, Confidence = 0.9 }
                },
                Relations = new List<EntityRelation>()
            },
            new()
            {
                SourceId = "2",
                Entities = new List<ExtractedEntity>
                {
                    new() { Id = "e2", Text = "microsoft", NormalizedText = "Microsoft", Type = NamedEntityType.Organization, Confidence = 0.85 }
                },
                Relations = new List<EntityRelation>()
            }
        };

        // Act
        var result = await service.LinkEntitiesAsync(graphs);

        // Assert
        var linkedEntity = Assert.Single(result.Entities);
        Assert.Equal(2, linkedEntity.SurfaceForms.Count);
    }

    [Fact]
    public async Task LinkEntitiesAsync_UpdatesRelationIds()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var graphs = new List<EntityGraph>
        {
            new()
            {
                SourceId = "1",
                Entities = new List<ExtractedEntity>
                {
                    new() { Id = "e1", Text = "John", NormalizedText = "John", Type = NamedEntityType.Person, Confidence = 0.9, OccurrenceCount = 1 },
                    new() { Id = "e2", Text = "Microsoft", NormalizedText = "Microsoft", Type = NamedEntityType.Organization, Confidence = 0.9, OccurrenceCount = 1 }
                },
                Relations = new List<EntityRelation>
                {
                    new() { Id = "r1", SourceEntityId = "e1", TargetEntityId = "e2", Type = RelationType.WorksFor, Confidence = 0.8 }
                }
            }
        };

        // Act
        var result = await service.LinkEntitiesAsync(graphs);

        // Assert
        Assert.NotEmpty(result.Relations);
        var relation = result.Relations.First();
        Assert.NotEqual("e1", relation.SourceEntityId);
        Assert.NotEqual("e2", relation.TargetEntityId);
    }

    [Fact]
    public async Task LinkEntitiesAsync_CalculatesImportanceScore()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var graphs = new List<EntityGraph>
        {
            new()
            {
                SourceId = "1",
                Entities = new List<ExtractedEntity>
                {
                    new() { Id = "e1", Text = "Python", NormalizedText = "Python", Type = NamedEntityType.Technology, Confidence = 0.9, OccurrenceCount = 5 }
                },
                Relations = new List<EntityRelation>()
            }
        };

        // Act
        var result = await service.LinkEntitiesAsync(graphs);

        // Assert
        var linkedEntity = Assert.Single(result.Entities);
        Assert.True(linkedEntity.ImportanceScore > 0);
    }

    [Fact]
    public async Task LinkEntitiesAsync_IncludesStatistics()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var graphs = new List<EntityGraph>
        {
            new()
            {
                SourceId = "1",
                Entities = new List<ExtractedEntity>
                {
                    new() { Id = "e1", Text = "Python", NormalizedText = "Python", Type = NamedEntityType.Technology, Confidence = 0.9, OccurrenceCount = 1 }
                },
                Relations = new List<EntityRelation>()
            }
        };

        // Act
        var result = await service.LinkEntitiesAsync(graphs);

        // Assert
        Assert.NotNull(result.Stats);
        Assert.True(result.Stats.ProcessingTimeMs >= 0);
    }

    [Fact]
    public async Task LinkEntitiesAsync_WithRequireSameType_SeparatesEntitiesByType()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var graphs = new List<EntityGraph>
        {
            new()
            {
                SourceId = "1",
                Entities = new List<ExtractedEntity>
                {
                    new() { Id = "e1", Text = "Apple", NormalizedText = "Apple", Type = NamedEntityType.Organization, Confidence = 0.9, OccurrenceCount = 1 },
                    new() { Id = "e2", Text = "Apple", NormalizedText = "Apple", Type = NamedEntityType.Product, Confidence = 0.85, OccurrenceCount = 1 }
                },
                Relations = new List<EntityRelation>()
            }
        };
        var options = new EntityLinkingOptions { RequireSameType = true };

        // Act
        var result = await service.LinkEntitiesAsync(graphs, options);

        // Assert
        Assert.Equal(2, result.Entities.Count);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ExtractEntitiesAsync_SpecialCharactersInContent_HandlesGracefully()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = "Test <script>alert('xss')</script> content with 特殊字符 and €100.";

        // Act
        var result = await service.ExtractEntitiesAsync(content);

        // Assert
        Assert.NotNull(result);
        // Should find €100 as money
        Assert.Contains(result, e => e.Type == NamedEntityType.Money);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_VeryLongContent_ProcessesSuccessfully()
    {
        // Arrange
        var service = new EntityExtractionService(_loggerMock);
        var content = string.Join(" ", Enumerable.Repeat("Python JavaScript C# Go Rust", 100));

        // Act
        var result = await service.ExtractEntitiesAsync(content);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_LlmFailure_FallsBackToPatterns()
    {
        // Arrange
        _llmServiceMock.CompleteAsync(
                Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Throws(new Exception("LLM service unavailable"));

        var service = new EntityExtractionService(_loggerMock, _llmServiceMock);
        var content = "Python is a great programming language.";
        var options = new EntityExtractionOptions { UseLlm = true };

        // Act - should not throw
        var result = await service.ExtractEntitiesAsync(content, options);

        // Assert - should still return pattern-based results
        Assert.NotEmpty(result);
        Assert.Contains(result, e => e.Type == NamedEntityType.Technology);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_InvalidLlmResponse_HandlesGracefully()
    {
        // Arrange
        _llmServiceMock.CompleteAsync(
                Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("This is not valid JSON");

        var service = new EntityExtractionService(_loggerMock, _llmServiceMock);
        var content = "Python is great.";
        var options = new EntityExtractionOptions { UseLlm = true };

        // Act - should not throw
        var result = await service.ExtractEntitiesAsync(content, options);

        // Assert
        Assert.NotNull(result);
    }

    #endregion
}
