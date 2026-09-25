using System.Collections.Generic;
using AwesomeAssertions;
using Xunit;
using static FluxIndex.Storage.PostgreSQL.RelationalSchemaProvisioner;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Docker-free teeth for the column half of provisioning. A database created by an earlier version
/// has every owned relation but not every column the current model declares; the decision of which
/// columns to add in place and which to refuse is pure logic, so it runs in CI while the DDL round
/// trip lives in the Integration-tagged tests.
/// </summary>
public class PostgreSQLSchemaColumnPlanTests
{
    private static IReadOnlySet<string> Existing(params string[] columns) =>
        new HashSet<string>(columns, System.StringComparer.Ordinal);

    [Fact]
    public void PlanColumns_AddsAMissingNullableColumn()
    {
        var (add, refuse) = PlanColumns(
            [new ModelColumn("id", false), new ModelColumn("chunk_ids", true)],
            Existing("id"));

        add.Should().Equal("chunk_ids");
        refuse.Should().BeEmpty();
    }

    [Fact]
    public void PlanColumns_RefusesAMissingRequiredColumnWithoutDefault()
    {
        // Existing rows would need a value nobody can invent — fail loud, like a missing relation.
        var (add, refuse) = PlanColumns(
            [new ModelColumn("id", false), new ModelColumn("name", false)],
            Existing("id"));

        add.Should().BeEmpty();
        refuse.Should().Equal("name");
    }

    [Fact]
    public void PlanColumns_LeavesExtraDatabaseColumnsAlone()
    {
        // A column the database has and the model does not is not a wrong schema.
        var (add, refuse) = PlanColumns(
            [new ModelColumn("id", false)],
            Existing("id", "legacy_notes"));

        add.Should().BeEmpty();
        refuse.Should().BeEmpty();
    }

    [Fact]
    public void PlanColumns_WhenUpToDate_DoesNothing()
    {
        var (add, refuse) = PlanColumns(
            [new ModelColumn("id", false), new ModelColumn("chunk_ids", true)],
            Existing("id", "chunk_ids"));

        add.Should().BeEmpty();
        refuse.Should().BeEmpty();
    }

    // ---- widening: a column an earlier version created narrower than the current model ----

    private static IReadOnlyDictionary<string, int?> Lengths(params (string Name, int? Length)[] columns)
    {
        var map = new Dictionary<string, int?>(System.StringComparer.Ordinal);
        foreach (var (name, length) in columns) map[name] = length;
        return map;
    }

    [Fact]
    public void PlanWidenings_WidensAVarcharTheModelNoLongerBounds()
    {
        // vectors.DocumentId was varchar(50) until 0.52.0; a longer id failed deep in the store with 22001.
        var widen = PlanWidenings(
            [new BoundedColumn("Id", null), new BoundedColumn("DocumentId", null)],
            Lengths(("Id", null), ("DocumentId", 50)));

        widen.Should().Equal("DocumentId");
    }

    [Fact]
    public void PlanWidenings_WidensToALargerModelBound()
    {
        PlanWidenings([new BoundedColumn("Type", 100)], Lengths(("Type", 50))).Should().Equal("Type");
    }

    [Fact]
    public void PlanWidenings_NeverNarrows_AndIgnoresMatchingAndMissingColumns()
    {
        // Narrowing could fail on existing rows or truncate them — that is a migration, not provisioning.
        var widen = PlanWidenings(
            [new BoundedColumn("Type", 50), new BoundedColumn("Kind", 20), new BoundedColumn("New", null)],
            Lengths(("Type", 50), ("Kind", 100)));

        widen.Should().BeEmpty();
    }
}
