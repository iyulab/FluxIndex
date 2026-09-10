using AwesomeAssertions;
using FluxIndex.Core.Application.Services.KeywordSearch;
using Xunit;

namespace FluxIndex.Core.Tests.KeywordSearch;

/// <summary>
/// Pins the order in which an indexing transaction acquires term rows.
/// </summary>
/// <remarks>
/// <para>
/// Upserting a term row locks it for the rest of the transaction. Terms were upserted in whatever
/// order a chunk's token stream produced them, so two transactions indexing different documents that
/// share vocabulary took the same rows in opposite orders — a deadlock that needs no unusual input,
/// since any two documents in one language share common words. Reported from a deployment with four
/// indexing workers, where it fired continuously and killed the whole job each time (PostgreSQL
/// <c>40P01</c>).
/// </para>
/// <para>
/// The rule lives in a pure function precisely so these tests exist: the backend where the deadlock
/// happens is a container CI here does not run, so a guard placed there would never execute. What
/// stays uncovered is the transaction itself; what is covered is the property that removes the cycle.
/// </para>
/// </remarks>
public class TermAcquisitionOrderTests
{
    [Fact]
    public void TermsAreOrdered_SoTwoTransactionsCannotWaitInBothDirections()
    {
        // The reported shape: two documents sharing vocabulary, each tokenized in its own order.
        var documentA = new[] { "roof", "rent", "tenant" };
        var documentB = new[] { "tenant", "roof", "landlord" };

        var orderA = RelationalKeywordSearchService.TermAcquisitionOrder([documentA]);
        var orderB = RelationalKeywordSearchService.TermAcquisitionOrder([documentB]);

        // The shared terms have to appear in the same relative order in both, whatever else differs.
        var sharedInA = orderA.Where(t => documentB.Contains(t)).ToList();
        var sharedInB = orderB.Where(t => documentA.Contains(t)).ToList();

        sharedInA.Should().Equal(sharedInB,
            "a total order over the rows is what makes a wait cycle impossible");
    }

    [Fact]
    public void OrderSpansTheWholeBatch_NotEachChunkSeparately()
    {
        // Sorting within a chunk is not enough: the transaction covers the batch, so two
        // transactions could still interleave between chunks and re-create the cycle.
        var order = RelationalKeywordSearchService.TermAcquisitionOrder(
        [
            ["zebra", "apple"],
            ["mango", "banana"]
        ]);

        order.Should().Equal("apple", "banana", "mango", "zebra");
    }

    [Fact]
    public void TermsAppearOnce_EvenWhenSharedAcrossChunks()
    {
        var order = RelationalKeywordSearchService.TermAcquisitionOrder(
        [
            ["alpha", "beta"],
            ["beta", "gamma"],
            ["alpha", "gamma"]
        ]);

        order.Should().Equal("alpha", "beta", "gamma");
    }

    [Fact]
    public void TermsAreNormalizedBeforeOrdering_SoTwoSpellingsCannotTakeTwoLocks()
    {
        // The row is keyed on the normalized term. Ordering the raw spellings would put two forms of
        // one row in the sequence, and their relative position could differ between transactions -
        // which is the cycle again, arrived at through the back door.
        var order = RelationalKeywordSearchService.TermAcquisitionOrder([["Tenant", "tenant", "TENANT"]]);

        order.Should().ContainSingle("all three spellings address the same row");
    }

    [Fact]
    public void NoTerms_ProducesNoAcquisitions()
    {
        RelationalKeywordSearchService.TermAcquisitionOrder([]).Should().BeEmpty();
    }
}
