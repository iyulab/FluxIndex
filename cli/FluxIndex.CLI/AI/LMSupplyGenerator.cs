using System.Text;
using Flux.Abstractions;
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

    protected override async Task<string> CompleteCoreAsync(
        string prompt, TextCompletionOptions options, CancellationToken cancellationToken)
    {
        var genOptions = new GenerationOptions
        {
            MaxTokens = options.MaxTokens,
            Temperature = options.Temperature
        };

        var sb = new StringBuilder();
        await foreach (var token in _model.GenerateAsync(prompt, genOptions, cancellationToken))
        {
            sb.Append(token);
        }
        return sb.ToString();
    }

    public ValueTask DisposeAsync() => _model.DisposeAsync();
}
