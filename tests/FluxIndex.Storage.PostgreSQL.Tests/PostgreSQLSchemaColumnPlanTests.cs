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
}
