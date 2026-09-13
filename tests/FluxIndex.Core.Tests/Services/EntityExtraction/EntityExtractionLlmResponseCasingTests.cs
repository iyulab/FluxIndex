using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.EntityExtraction;

/// <summary>
/// The extraction prompts ask the model for lowercase keys (<c>"text"</c>, <c>"source"</c>, ...) — the
/// exact shape the prompt's own example shows. The result records are PascalCase, and
/// <c>JsonSerializer.Deserialize</c> matches case-sensitively by default, so a response that follows
/// the prompt to the letter used to bind nothing: every property stayed at its default, every item was
/// dropped as empty, and nothing threw or logged. A consumer with <c>UseLlm = true</c> got a GraphRAG
/// index with zero LLM-derived entities and no signal. These facts pin the binding and the signal.
/// </summary>
public class EntityExtractionLlmResponseCasingTests
{
    private sealed class ListLogger : ILogger<EntityExtractionService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static ITextCompletionService LlmReturning(string response)
    {
        var llm = Substitute.For<ITextCompletionService>();
        llm.CompleteAsync(Arg.Any<string>(), Arg.Any<TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns(response);
        return llm;
    }

    // Lowercase on purpose: the pattern path recognises Latin capitalised sequences only, so the only way
    // this entity reaches the result is through the LLM response.
    private const string Content = "the company acme announced new products today.";

    [Fact]
    public async Task EntityResponse_WithThePromptsOwnLowercaseKeys_BindsEveryField()
    {
        var logger = new ListLogger();
        var llm = LlmReturning("""[{"text": "acme", "type": "Organization", "confidence": 0.95}]""");
        var service = new EntityExtractionService(logger, llm);

        var result = await service.ExtractEntitiesAsync(Content, new EntityExtractionOptions { UseLlm = true }, TestContext.Current.CancellationToken);

        var acme = Assert.Single(result, e => e.Text == "acme");
        Assert.Equal(NamedEntityType.Organization, acme.Type);
        Assert.Equal(0.95, acme.Confidence, precision: 6);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task RelationResponse_WithThePromptsOwnLowercaseKeys_BindsEveryField()
    {
        var logger = new ListLogger();
        var llm = LlmReturning("""[{"source": "acme", "target": "jane doe", "type": "FoundedBy", "confidence": 0.9}]""");
        var service = new EntityExtractionService(logger, llm);
        var entities = new List<ExtractedEntity>
        {
            new() { Id = "1", Text = "acme", NormalizedText = "acme", Type = NamedEntityType.Organization, Confidence = 0.9 },
            new() { Id = "2", Text = "jane doe", NormalizedText = "jane doe", Type = NamedEntityType.Person, Confidence = 0.9 }
        };

        var result = await service.ExtractRelationsAsync(
            "acme was founded by jane doe.", entities, new EntityExtractionOptions { UseLlm = true }, TestContext.Current.CancellationToken);

        var founded = Assert.Single(result, r => r.Type == RelationType.FoundedBy);
        Assert.Equal("1", founded.SourceEntityId);
        Assert.Equal("2", founded.TargetEntityId);
        Assert.Equal(0.9, founded.Confidence, precision: 6);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task EntityResponse_ThatParsesButCarriesNoUsableItem_IsAnnouncedAsAWarning()
    {
        // Well-formed JSON, wrong keys: deserialisation succeeds and yields items with every field at its
        // default. That is the same outcome as a parse failure (zero usable results) and must not be silent.
        var logger = new ListLogger();
        var llm = LlmReturning("""[{"name": "acme", "kind": "Organization"}, {"name": "jane doe", "kind": "Person"}]""");
        var service = new EntityExtractionService(logger, llm);

        var result = await service.ExtractEntitiesAsync(Content, new EntityExtractionOptions { UseLlm = true }, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(result, e => e.Text == "acme");
        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("2 item(s)", warning.Message);
        Assert.Contains("'text'", warning.Message);
    }

    [Fact]
    public async Task RelationResponse_ThatParsesButCarriesNoUsableItem_IsAnnouncedAsAWarning()
    {
        var logger = new ListLogger();
        var llm = LlmReturning("""[{"from": "acme", "to": "jane doe", "type": "FoundedBy"}]""");
        var service = new EntityExtractionService(logger, llm);
        var entities = new List<ExtractedEntity>
        {
            new() { Id = "1", Text = "acme", NormalizedText = "acme", Type = NamedEntityType.Organization, Confidence = 0.9 },
            new() { Id = "2", Text = "jane doe", NormalizedText = "jane doe", Type = NamedEntityType.Person, Confidence = 0.9 }
        };

        await service.ExtractRelationsAsync(
            "acme was founded by jane doe.", entities, new EntityExtractionOptions { UseLlm = true }, TestContext.Current.CancellationToken);

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("1 item(s)", warning.Message);
        Assert.Contains("'source'", warning.Message);
    }

    [Fact]
    public async Task EmptyResponse_IsNotAWarning()
    {
        // "[]" is the model saying "nothing here" — a legitimate answer, not a shape mismatch.
        var logger = new ListLogger();
        var service = new EntityExtractionService(logger, LlmReturning("[]"));

        await service.ExtractEntitiesAsync(Content, new EntityExtractionOptions { UseLlm = true }, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }
}
