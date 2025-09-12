using FluxIndex.Core.Application.Configuration;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.QueryOrchestration;
using FluxIndex.SDK.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace FluxIndex.SDK.Extensions;

/// <summary>
/// Extension methods for configuring Query Orchestrator in FluxIndexClientBuilder
/// </summary>
public static class QueryOrchestratorExtensions
{
    /// <summary>
    /// Adds Semantic Kernel-based Query Orchestrator with Azure OpenAI
    /// </summary>
    public static FluxIndexClientBuilder WithQueryOrchestration(
        this FluxIndexClientBuilder builder,
        string azureEndpoint,
        string apiKey,
        string deploymentName = "gpt-5-nano",
        Action<QueryOrchestratorOptions>? configure = null)
    {
        var options = new QueryOrchestratorOptions
        {
            AzureOpenAIEndpoint = azureEndpoint,
            AzureOpenAIApiKey = apiKey,
            DeploymentName = deploymentName
        };

        configure?.Invoke(options);

        builder.ConfigureServices(services =>
        {
            services.AddSingleton(options);
            services.AddSingleton<IQueryOrchestrator>(sp =>
                new SemanticKernelOrchestrator(
                    options.AzureOpenAIEndpoint,
                    options.AzureOpenAIApiKey,
                    options.DeploymentName));
        });

        return builder;
    }

    /// <summary>
    /// Enables HyDE (Hypothetical Document Embeddings) transformation
    /// </summary>
    public static FluxIndexClientBuilder WithHyDE(
        this FluxIndexClientBuilder builder,
        int maxTokens = 300)
    {
        builder.ConfigureOptions(options =>
        {
            if (options is FluxIndexOptions fluxOptions)
            {
                fluxOptions.Search.DefaultStrategy = QueryStrategy.HyDE;
            }
        });

        return builder;
    }

    /// <summary>
    /// Enables Multi-Query decomposition for complex queries
    /// </summary>
    public static FluxIndexClientBuilder WithMultiQuery(
        this FluxIndexClientBuilder builder,
        int maxSubQueries = 3)
    {
        builder.ConfigureOptions(options =>
        {
            if (options is FluxIndexOptions fluxOptions)
            {
                fluxOptions.Search.DefaultStrategy = QueryStrategy.MultiQuery;
                fluxOptions.Search.MaxSubQueries = maxSubQueries;
            }
        });

        return builder;
    }

    /// <summary>
    /// Enables adaptive query strategy selection
    /// </summary>
    public static FluxIndexClientBuilder WithAdaptiveQueryStrategy(
        this FluxIndexClientBuilder builder,
        float minConfidence = 0.7f)
    {
        builder.ConfigureOptions(options =>
        {
            if (options is FluxIndexOptions fluxOptions)
            {
                fluxOptions.Search.DefaultStrategy = QueryStrategy.Adaptive;
                fluxOptions.Search.MinConfidenceThreshold = minConfidence;
            }
        });

        return builder;
    }
}

/// <summary>
/// Extensions to FluxIndexOptions for query orchestration
/// </summary>
public static class FluxIndexOptionsExtensions
{
    public static QueryStrategy DefaultStrategy { get; set; } = QueryStrategy.Direct;
    public static int MaxSubQueries { get; set; } = 3;
    public static float MinConfidenceThreshold { get; set; } = 0.7f;
}