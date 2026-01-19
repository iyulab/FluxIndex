using System.Text;
using FluxIndex.Core.Application.Services.Base;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Models;

namespace FluxIndex.CLI.AI;

/// <summary>
/// Simple LMSupply text completion wrapper.
/// Uses Core's TextCompletionServiceBase for common functionality.
/// </summary>
public sealed class LMSupplyGenerator : TextCompletionServiceBase, IAsyncDisposable
{
    private readonly IGeneratorModel _model;

    private LMSupplyGenerator(IGeneratorModel model) => _model = model;

    public static async Task<LMSupplyGenerator> CreateAsync(
        string modelId = "default",
        CancellationToken cancellationToken = default)
    {
        var model = await LocalGenerator.LoadAsync(modelId, cancellationToken: cancellationToken);
        return new LMSupplyGenerator(model);
    }

    protected override async Task<string> GenerateCoreAsync(
        string prompt, int maxTokens, float temperature, CancellationToken cancellationToken)
    {
        var options = new GenerationOptions
        {
            MaxTokens = maxTokens,
            Temperature = temperature
        };

        var sb = new StringBuilder();
        await foreach (var token in _model.GenerateAsync(prompt, options, cancellationToken))
        {
            sb.Append(token);
        }
        return sb.ToString();
    }

    public ValueTask DisposeAsync() => _model.DisposeAsync();
}
