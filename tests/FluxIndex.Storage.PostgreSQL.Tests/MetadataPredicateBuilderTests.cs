using System.Linq.Expressions;
using AwesomeAssertions;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// Unit tests for PostgreSQLVectorStore.BuildMetadataPredicate — expression shape and contract
/// enforcement. No Docker required (EF.Functions.JsonContains cannot be evaluated client-side,
/// so behavior against real jsonb is covered by the integration suite).
/// </summary>
public class MetadataPredicateBuilderTests
{
    [Fact]
    public void ScalarFilters_ProduceSingleContainmentCall()
    {
        var predicate = PostgreSQLVectorStore.BuildMetadataPredicate(new Dictionary<string, object>
        {
            ["workspace_id"] = "ws-1",
            ["flag"] = true
        });

        // Both scalars collapse into one serialized containment check — no boolean operators.
        CountNodes(predicate.Body, ExpressionType.OrElse).Should().Be(0);
        CountNodes(predicate.Body, ExpressionType.AndAlso).Should().Be(0);
        CountNodes(predicate.Body, ExpressionType.Call).Should().Be(1);
    }

    [Fact]
    public void CollectionFilter_ProducesOrOfPerElementContainment()
    {
        var predicate = PostgreSQLVectorStore.BuildMetadataPredicate(new Dictionary<string, object>
        {
            ["tag"] = new List<string> { "h1", "h2", "h3" }
        });

        // 3 alternatives → 2 OrElse nodes, 3 containment calls.
        CountNodes(predicate.Body, ExpressionType.OrElse).Should().Be(2);
        CountNodes(predicate.Body, ExpressionType.Call).Should().Be(3);
    }

    [Fact]
    public void MixedFilters_AndCombineScalarAndCollectionParts()
    {
        var predicate = PostgreSQLVectorStore.BuildMetadataPredicate(new Dictionary<string, object>
        {
            ["tag"] = new[] { "h1", "h2" },
            ["workspace_id"] = "ws-1"
        });

        CountNodes(predicate.Body, ExpressionType.AndAlso).Should().Be(1);
        CountNodes(predicate.Body, ExpressionType.OrElse).Should().Be(1);
        CountNodes(predicate.Body, ExpressionType.Call).Should().Be(3);
    }

    [Fact]
    public void DocumentIdFilter_ComparesTheDocumentIdColumn_NotTheMetadata()
    {
        // No JsonContains in the tree, so the predicate evaluates client-side: the entity with no
        // metadata copy of its document id is in scope, the one whose metadata claims it is not.
        var predicate = PostgreSQLVectorStore.BuildMetadataPredicate(new Dictionary<string, object>
        {
            ["document_id"] = new[] { "h1", "h2" }
        }).Compile();

        predicate(new VectorEntity { DocumentId = "h2" }).Should().BeTrue();
        predicate(new VectorEntity { DocumentId = "h9", Metadata = new() { ["document_id"] = "h1" } }).Should().BeFalse();
    }

    [Fact]
    public void DocumentIdFilterWithMetadataFilter_AndCombinesColumnAndContainment()
    {
        var predicate = PostgreSQLVectorStore.BuildMetadataPredicate(new Dictionary<string, object>
        {
            ["document_id"] = "h1",
            ["workspace_id"] = "ws-1"
        });

        CountNodes(predicate.Body, ExpressionType.AndAlso).Should().Be(1);
        CountNodes(predicate.Body, ExpressionType.OrElse).Should().Be(0);
        CountNodes(predicate.Body, ExpressionType.Call).Should().Be(2, "one column membership test and one jsonb containment");
    }

    [Fact]
    public void UnsupportedValue_Throws()
    {
        var act = () => PostgreSQLVectorStore.BuildMetadataPredicate(new Dictionary<string, object>
        {
            ["k"] = new object()
        });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EmptyCollection_Throws()
    {
        var act = () => PostgreSQLVectorStore.BuildMetadataPredicate(new Dictionary<string, object>
        {
            ["k"] = new List<string>()
        });

        act.Should().Throw<ArgumentException>().WithMessage("*empty collection*");
    }

    private static int CountNodes(Expression expression, ExpressionType type)
    {
        var counter = new NodeCounter(type);
        counter.Visit(expression);
        return counter.Count;
    }

    private sealed class NodeCounter(ExpressionType type) : ExpressionVisitor
    {
        public int Count { get; private set; }

        public override Expression? Visit(Expression? node)
        {
            if (node is not null && node.NodeType == type)
                Count++;
            return base.Visit(node);
        }
    }
}
