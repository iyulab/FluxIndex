using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.EntityExtraction;

/// <summary>
/// Two <see cref="EntityExtractionOptions"/> that were declared but never read by the default extractor
/// (found by <c>OptionsReachabilityRosterTests</c>): <c>Language</c> and <c>CustomPatterns</c>. Each fact
/// here is the observable effect the option's documentation now promises — not merely that the getter
/// is called.
/// </summary>
public class EntityExtractionOptionsHonouredTests
{
    private readonly ITextCompletionService _llm = Substitute.For<ITextCompletionService>();
    private readonly List<string> _prompts = [];

    public EntityExtractionOptionsHonouredTests()
    {
        _llm.CompleteAsync(Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                _prompts.Add(ci.Arg<string>());
                return Task.FromResult("[]");
            });
    }

    private EntityExtractionService Service(bool withLlm = false) =>
        new(NullLogger<EntityExtractionService>.Instance, withLlm ? _llm : null);

    // ---- Language ----

    [Fact]
    public async Task Language_ReachesBothPrompts_AndAsksForTextExactlyAsWritten()
    {
        var service = Service(withLlm: true);
        var entities = new List<ExtractedEntity>
        {
            new() { Text = "삼성전자", Type = NamedEntityType.Organization, Confidence = 0.9 },
            new() { Text = "이재용", Type = NamedEntityType.Person, Confidence = 0.9 }
        };

        await service.ExtractEntitiesAsync("삼성전자의 이재용 회장이 발표했다.", new EntityExtractionOptions { UseLlm = true, Language = "ko" }, TestContext.Current.CancellationToken);
        await service.ExtractRelationsAsync("삼성전자의 이재용 회장이 발표했다.", entities, new EntityExtractionOptions { UseLlm = true, Language = "ko" }, TestContext.Current.CancellationToken);

        Assert.Equal(2, _prompts.Count);
        Assert.All(_prompts, p =>
        {
            Assert.Contains("written in ko", p);
            Assert.Contains("exactly as it appears", p);
        });
    }

    [Fact]
    public async Task Language_Unset_LeavesThePromptWithoutAHint()
    {
        var service = Service(withLlm: true);

        await service.ExtractEntitiesAsync("Apple announced new products.", new EntityExtractionOptions { UseLlm = true }, TestContext.Current.CancellationToken);

        var prompt = Assert.Single(_prompts);
        Assert.DoesNotContain("written in", prompt);
        Assert.DoesNotContain("transliterate", prompt);
    }

    // ---- CustomPatterns ----

    [Fact]
    public async Task CustomPatterns_MatchesAreEmittedAsCustomEntities_WithTheKeyAsSubtype()
    {
        var service = Service();
        var options = new EntityExtractionOptions
        {
            UseLlm = false,
            CustomPatterns = new Dictionary<string, string> { ["ticket"] = @"\bT-\d{4}\b", ["desk"] = @"\bDESK-[A-Z]+\b" }
        };

        var result = await service.ExtractEntitiesAsync("Ticket T-0042 was routed to DESK-KR by T-0043.", options, TestContext.Current.CancellationToken);

        var tickets = result.Where(e => e.Type == NamedEntityType.Custom && e.Subtype == "ticket").Select(e => e.Text).Order().ToList();
        Assert.Equal(["T-0042", "T-0043"], tickets);
        var desk = Assert.Single(result, e => e.Subtype == "desk");
        Assert.Equal("DESK-KR", desk.Text);
        Assert.Equal(0.9, desk.Confidence);
        Assert.True(desk.StartPosition > 0 && desk.EndPosition == desk.StartPosition + "DESK-KR".Length);
    }

    [Fact]
    public async Task CustomPatterns_RespectTheEntityTypesFilter()
    {
        var service = Service();
        var content = "Ticket T-0042 for alice@example.com.";
        var patterns = new Dictionary<string, string> { ["ticket"] = @"\bT-\d{4}\b" };

        var withoutCustom = await service.ExtractEntitiesAsync(content,
            new EntityExtractionOptions { UseLlm = false, CustomPatterns = patterns, EntityTypes = [NamedEntityType.Email] }, TestContext.Current.CancellationToken);
        var withCustom = await service.ExtractEntitiesAsync(content,
            new EntityExtractionOptions { UseLlm = false, CustomPatterns = patterns, EntityTypes = [NamedEntityType.Email, NamedEntityType.Custom] }, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(withoutCustom, e => e.Type == NamedEntityType.Custom);
        Assert.Contains(withCustom, e => e.Type == NamedEntityType.Custom && e.Text == "T-0042");
    }

    [Fact]
    public async Task CustomPatterns_InvalidExpression_ThrowsNamingTheKey()
    {
        var service = Service();
        var options = new EntityExtractionOptions { UseLlm = false, CustomPatterns = new Dictionary<string, string> { ["broken"] = "(" } };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ExtractEntitiesAsync("anything", options, TestContext.Current.CancellationToken));

        Assert.Contains("broken", ex.Message);
    }
}
