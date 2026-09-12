using Flux.Abstractions;
using NSubstitute;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluxIndex.Core.Tests.Services.EntityExtraction;

/// <summary>
/// Without an LLM the extractor is pattern-only, and its named-entity pattern recognises Latin
/// capitalised sequences only. That is a silent degradation on any non-Latin corpus (zero
/// organisations and people, no error), so the service must say so — once per instance, as a
/// warning an operator can act on.
/// </summary>
public class EntityExtractionPatternOnlyWarningTests
{
    private sealed class ListLogger : ILogger<EntityExtractionService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private const string KoreanCorpus = "주요 파트너사는 한빛소프트와 누리텔레콤이고, 고객사는 대한제강입니다.";

    [Fact]
    public async Task PatternOnlyExtraction_OnANonLatinCorpus_YieldsNoNamedEntities_AndWarnsOnce()
    {
        var logger = new ListLogger();
        var service = new EntityExtractionService(logger, llmService: null);
        var ct = TestContext.Current.CancellationToken;

        var first = await service.ExtractEntitiesAsync(KoreanCorpus, new EntityExtractionOptions { UseLlm = false }, ct);
        var second = await service.ExtractEntitiesAsync(KoreanCorpus, new EntityExtractionOptions { UseLlm = false }, ct);

        // The degradation itself: organisations and people in Korean are invisible to the pattern.
        Assert.DoesNotContain(first, e => e.Type is NamedEntityType.Organization or NamedEntityType.Person);
        Assert.DoesNotContain(second, e => e.Type is NamedEntityType.Organization or NamedEntityType.Person);

        // ...and it is announced once, not per chunk.
        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning && e.Message.Contains("pattern-only", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(warnings);
        Assert.Contains("Latin", warnings[0].Message);
    }

    [Fact]
    public async Task WithAnLlmInUse_NoPatternOnlyWarning()
    {
        var logger = new ListLogger();
        var llm = NSubstitute.Substitute.For<ITextCompletionService>();
        llm.CompleteAsync(NSubstitute.Arg.Any<string>(), NSubstitute.Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), NSubstitute.Arg.Any<CancellationToken>()).Returns("[]");
        var service = new EntityExtractionService(logger, llm);

        await service.ExtractEntitiesAsync(KoreanCorpus, new EntityExtractionOptions { UseLlm = true }, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("pattern-only", StringComparison.OrdinalIgnoreCase));
    }
}
