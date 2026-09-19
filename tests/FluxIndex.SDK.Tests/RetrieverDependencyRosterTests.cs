using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// The builder constructs <see cref="Retriever"/> by hand, so an optional dependency added to the constructor stays at its
/// default unless the builder's factory is updated too — that is how a registered <c>IRAGSecurityPipeline</c> was
/// silently ignored. Derived from the constructor, not from a list kept here: every optional interface dependency that
/// is registered in the container must reach the retriever.
/// </summary>
public class RetrieverDependencyRosterTests
{
    [Fact]
    public void Build_HandsEveryRegisteredOptionalDependencyToTheRetriever()
    {
        var optional = typeof(Retriever).GetConstructors().Single(c => c.GetParameters().Length > 3).GetParameters()
            .Where(p => p.HasDefaultValue && p.ParameterType.IsInterface
                        && !(p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(ILogger<>)))
            .Select(p => p.ParameterType)
            .ToList();
        optional.Should().HaveCountGreaterThan(5, "the retriever takes several optional services");

        var substitutes = optional.ToDictionary(t => t, t => Substitute.For([t], []));
        var context = FluxIndexContext.CreateBuilder()
            .UseInMemoryEmbedding()
            .SuppressStartupMessages()
            .ConfigureServices(services =>
            {
                foreach (var (type, instance) in substitutes)
                    services.AddSingleton(type, instance);
            })
            .Build();

        var fields = typeof(Retriever).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var type in optional)
        {
            var field = fields.Where(f => f.FieldType == type).ToList();
            field.Should().ContainSingle($"the retriever keeps its {type.Name} in exactly one field");
            field[0].GetValue(context.Retriever).Should().NotBeNull(
                $"{type.Name} is registered, so the builder must hand it to the retriever");
        }
    }
}
