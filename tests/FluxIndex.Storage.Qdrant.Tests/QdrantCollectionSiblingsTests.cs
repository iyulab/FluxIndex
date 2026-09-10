using AwesomeAssertions;
using FluxIndex.Storage.Qdrant;
using Xunit;

namespace FluxIndex.Storage.Qdrant.Tests;

/// <summary>
/// Pins the rule that decides whether an existing Qdrant collection could hold data this store
/// wrote under a previous naming outcome.
/// </summary>
/// <remarks>
/// <para>
/// The failure this guards against was observed in a consumer's production deployment: a naming
/// change resolved the same logical index to a new collection name, the store created it, reported
/// ready, and every search returned zero results while ~15k points sat in the old collection.
/// Nothing threw and no log line said anything was wrong.
/// </para>
/// <para>
/// These tests deliberately carry no <c>Category=Integration</c> trait and touch no container. That
/// is the whole point: every Docker-backed test in this project is excluded by CI, so a guard pinned
/// only there would be a guard nothing ever runs. The rules that decide whether the warning fires
/// live in pure functions so CI can actually hold them. What stays uncovered is the I/O loop between
/// them - listing collections and counting points - which is deliberately kept thin for that reason.
/// </para>
/// </remarks>
public class QdrantCollectionSiblingsTests
{
    private const string Base = "aims_chunks";

    [Fact]
    public void DimensionSuffixedCollection_IsSiblingOfAFingerprintName()
    {
        // The reported case: a deployment on the dimension fallback moves to a bound identity.
        var siblings = QdrantCollectionSiblings.Find(
            Base, $"{Base}_a1b2c3d4", [$"{Base}_1024", $"{Base}_a1b2c3d4"]);

        siblings.Should().ContainSingle().Which.Should().Be($"{Base}_1024");
    }

    [Fact]
    public void BareBaseName_IsSiblingOfAFingerprintName()
    {
        // Fixed -> ModelFingerprint: the data stays in the base name itself.
        var siblings = QdrantCollectionSiblings.Find(
            Base, $"{Base}_a1b2c3d4", [Base, $"{Base}_a1b2c3d4"]);

        siblings.Should().ContainSingle().Which.Should().Be(Base);
    }

    [Fact]
    public void FingerprintName_IsSiblingOfTheBareBaseName()
    {
        // ModelFingerprint -> Fixed: the reverse transition has to be caught too.
        var siblings = QdrantCollectionSiblings.Find(
            Base, Base, [Base, $"{Base}_a1b2c3d4"]);

        siblings.Should().ContainSingle().Which.Should().Be($"{Base}_a1b2c3d4");
    }

    [Fact]
    public void ResolvedCollection_IsNeverItsOwnSibling()
    {
        var siblings = QdrantCollectionSiblings.Find(
            Base, $"{Base}_a1b2c3d4", [$"{Base}_a1b2c3d4"]);

        siblings.Should().BeEmpty();
    }

    [Fact]
    public void CollectionSharingOnlyThePrefix_IsNotASibling()
    {
        // "aims_chunks_archive" is not a name this store can produce, so it must not be able to
        // trip the guard - under FailOnCollectionMismatch that would fail a boot over an unrelated
        // collection someone else owns.
        var siblings = QdrantCollectionSiblings.Find(
            Base, $"{Base}_a1b2c3d4", [$"{Base}_archive", $"{Base}_backup_2026"]);

        siblings.Should().BeEmpty();
    }

    [Fact]
    public void UppercaseHexSuffix_IsNotASibling()
    {
        // Fingerprints are emitted lowercase; an uppercase one came from somewhere else.
        var siblings = QdrantCollectionSiblings.Find(
            Base, $"{Base}_a1b2c3d4", [$"{Base}_A1B2C3D4"]);

        siblings.Should().BeEmpty();
    }

    [Fact]
    public void HexSuffixOfTheWrongLength_IsNotASibling()
    {
        var siblings = QdrantCollectionSiblings.Find(
            Base, $"{Base}_a1b2c3d4", [$"{Base}_a1b2c3", $"{Base}_a1b2c3d4e5"]);

        siblings.Should().BeEmpty();
    }

    [Fact]
    public void CollectionUnderADifferentBaseName_IsNotASibling()
    {
        var siblings = QdrantCollectionSiblings.Find(
            Base, $"{Base}_a1b2c3d4", ["other_chunks_1024", "aims_chunk_1024"]);

        siblings.Should().BeEmpty();
    }

    [Fact]
    public void SeveralFingerprintCollections_AreAllReported_InServerOrder()
    {
        // One collection per embedding model is a supported steady state, so the predicate does
        // report them. What keeps that from being noise is the caller: it asks this only when the
        // collection it is about to serve is itself empty.
        var siblings = QdrantCollectionSiblings.Find(
            Base,
            $"{Base}_ffffffff",
            [$"{Base}_a1b2c3d4", $"{Base}_ffffffff", $"{Base}_00112233"]);

        siblings.Should().Equal($"{Base}_a1b2c3d4", $"{Base}_00112233");
    }

    [Fact]
    public void NoExistingCollections_YieldsNoSiblings()
    {
        QdrantCollectionSiblings.Find(Base, $"{Base}_a1b2c3d4", []).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyBaseName_IsRejected(string? baseName)
    {
        var act = () => QdrantCollectionSiblings.Find(baseName!, "x", []);

        act.Should().Throw<ArgumentException>();
    }
}

/// <summary>
/// Pins what the guard actually reports once sibling point counts are in hand — the half that
/// decides whether the warning (or, under FailOnCollectionMismatch, the startup failure) happens.
/// </summary>
/// <remarks>
/// Kept as a pure function for the same reason as <see cref="QdrantCollectionSiblingsTests"/>: the
/// only path that exercises this against a live server carries <c>Category=Integration</c>, which CI
/// excludes, so the decision itself is pinned where CI runs it.
/// </remarks>
public class QdrantPopulatedSiblingDescriptionTests
{
    [Fact]
    public void EmptySiblings_ProduceNoReport()
    {
        // A sibling that exists but holds nothing cannot be where the missing data went, so it is
        // not worth reporting - an abandoned or freshly created collection is not a lost index.
        QdrantCollectionSiblings.DescribePopulated([("aims_chunks_1024", 0)]).Should().BeNull();
    }

    [Fact]
    public void NoSiblingsAtAll_ProduceNoReport()
    {
        QdrantCollectionSiblings.DescribePopulated([]).Should().BeNull();
    }

    [Fact]
    public void PopulatedSibling_IsReportedWithItsCount()
    {
        QdrantCollectionSiblings.DescribePopulated([("aims_chunks_1024", 14950)])
            .Should().Be("aims_chunks_1024 (14950 points)");
    }

    [Fact]
    public void UnknownCount_IsReportedRatherThanDropped()
    {
        // Silence here is indistinguishable from safety, so an unreadable sibling still surfaces.
        QdrantCollectionSiblings.DescribePopulated([("aims_chunks_1024", null)])
            .Should().Be("aims_chunks_1024 (count unknown)");
    }

    [Fact]
    public void TheReportedIncident_ProducesAReport()
    {
        // The exact shape observed in production: a deployment indexing into "aims_chunks_1024"
        // switched naming strategy, the store resolved "aims_chunks_<fingerprint>", and served zero
        // results for every query from that empty collection while 14,950 points sat next door.
        // Both halves of the rule have to agree for the warning to fire, so pin them as one chain.
        const string baseName = "aims_chunks";
        const string resolved = "aims_chunks_a1b2c3d4";

        var siblings = QdrantCollectionSiblings.Find(
            baseName, resolved, ["aims_chunks_1024", resolved]);

        var detail = QdrantCollectionSiblings.DescribePopulated(
            siblings.Select(name => (name, (long?)14950)));

        detail.Should().Be("aims_chunks_1024 (14950 points)");
    }

    [Fact]
    public void OnlyPopulatedAndUnknownSiblings_SurviveTheFilter()
    {
        var detail = QdrantCollectionSiblings.DescribePopulated(
        [
            ("aims_chunks_1024", 14950),
            ("aims_chunks_00112233", 0),
            ("aims_chunks_a1b2c3d4", null)
        ]);

        detail.Should().Be("aims_chunks_1024 (14950 points), aims_chunks_a1b2c3d4 (count unknown)");
    }
}
