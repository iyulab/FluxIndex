using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Docker-free teeth for the shared-database fix. The schema decision itself — create everything,
/// do nothing, or refuse a half-built schema — is pure logic, so it runs in CI even though the DDL
/// round-trip it guards lives in the Integration-tagged tests that CI filters out.
/// </summary>
public class PostgreSQLSchemaInitializationPlanTests
{
    private static IReadOnlySet<string> Existing(params string[] relations) =>
        new HashSet<string>(relations, System.StringComparer.Ordinal);

    [Fact]
    public void Plan_WhenNoOwnedRelationExists_CreatesAll()
    {
        // The shared-database case: the database is full of the consumer's own tables, but none of
        // ours. EnsureCreated() used to read "database has tables" and skip; we must still create.
        var plan = RelationalSchemaProvisioner.Plan(
            new[] { "public.vectors" },
            Existing("public.any_app_table", "public.users"));

        plan.Should().Be(SchemaInitializationPlan.CreateAll);
    }

    [Fact]
    public void Plan_OnEmptyDatabase_CreatesAll()
    {
        var plan = RelationalSchemaProvisioner.Plan(new[] { "public.vectors" }, Existing());

        plan.Should().Be(SchemaInitializationPlan.CreateAll);
    }

    [Fact]
    public void Plan_WhenEveryOwnedRelationExists_IsUpToDate()
    {
        var plan = RelationalSchemaProvisioner.Plan(
            new[] { "public.vectors" },
            Existing("public.vectors", "public.any_app_table"));

        plan.Should().Be(SchemaInitializationPlan.UpToDate);
    }

    [Fact]
    public void Plan_WhenOnlySomeOwnedRelationsExist_IsPartiallyPresent()
    {
        // Fail-loud rather than half-repair: auto-migration cannot know whether the surviving
        // relation matches the current model.
        var plan = RelationalSchemaProvisioner.Plan(
            new[] { "public.vectors", "public.quantized_vectors" },
            Existing("public.vectors"));

        plan.Should().Be(SchemaInitializationPlan.PartiallyPresent);
    }

    [Fact]
    public void Plan_WithNoOwnedRelations_IsUpToDate()
    {
        var plan = RelationalSchemaProvisioner.Plan(new string[0], Existing("public.any_app_table"));

        plan.Should().Be(SchemaInitializationPlan.UpToDate);
    }
}
