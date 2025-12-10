using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Shared.DTOs.Settings;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Application.Services;

/// <summary>
/// Service implementation for AI provider settings management.
/// </summary>
public class AiProviderSettingsService : IAiProviderSettingsService
{
    private readonly IAiProviderSettingsRepository _repository;
    private readonly ILogger<AiProviderSettingsService> _logger;

    // Known providers and their models (Updated: December 2025)
    private static readonly Dictionary<string, ProviderInfo> KnownProviders = new()
    {
        ["OpenAI"] = new ProviderInfo
        {
            DisplayName = "OpenAI",
            EmbeddingModels = new[]
            {
                new ModelInfoDto { Id = "text-embedding-3-small", Name = "text-embedding-3-small", Description = "Efficient embedding model (1536 dims)", Dimensions = 1536 },
                new ModelInfoDto { Id = "text-embedding-3-large", Name = "text-embedding-3-large", Description = "High-quality embedding model (3072 dims)", Dimensions = 3072 }
            },
            LlmModels = new[]
            {
                new ModelInfoDto { Id = "gpt-5.1", Name = "GPT-5.1", Description = "Best for coding and agentic tasks", MaxTokens = 200000 },
                new ModelInfoDto { Id = "gpt-5", Name = "GPT-5", Description = "Most capable flagship model", MaxTokens = 200000 },
                new ModelInfoDto { Id = "gpt-5-mini", Name = "GPT-5 Mini", Description = "Fast, cost-efficient GPT-5", MaxTokens = 200000 },
                new ModelInfoDto { Id = "gpt-5-nano", Name = "GPT-5 Nano", Description = "Fastest, most cost-efficient GPT-5", MaxTokens = 200000 },
                new ModelInfoDto { Id = "gpt-4.1", Name = "GPT-4.1", Description = "Enhanced GPT-4 model", MaxTokens = 128000 },
                new ModelInfoDto { Id = "gpt-4.1-mini", Name = "GPT-4.1 Mini", Description = "Cost-efficient GPT-4.1", MaxTokens = 128000 },
                new ModelInfoDto { Id = "gpt-4.1-nano", Name = "GPT-4.1 Nano", Description = "Lightweight GPT-4.1", MaxTokens = 128000 },
                new ModelInfoDto { Id = "gpt-4o", Name = "GPT-4o", Description = "Multimodal with audio support", MaxTokens = 128000 },
                new ModelInfoDto { Id = "gpt-4o-mini", Name = "GPT-4o Mini", Description = "Efficient multimodal model", MaxTokens = 128000 },
                new ModelInfoDto { Id = "o3", Name = "o3", Description = "Advanced reasoning model", MaxTokens = 128000 },
                new ModelInfoDto { Id = "o4-mini", Name = "o4-mini", Description = "Fast reasoning model", MaxTokens = 128000 }
            }
        },
        ["Anthropic"] = new ProviderInfo
        {
            DisplayName = "Anthropic",
            EmbeddingModels = Array.Empty<ModelInfoDto>(), // Anthropic doesn't have embedding models
            LlmModels = new[]
            {
                new ModelInfoDto { Id = "claude-opus-4-5-20251101", Name = "Claude Opus 4.5", Description = "Best for coding, agents, intelligence", MaxTokens = 200000 },
                new ModelInfoDto { Id = "claude-sonnet-4-5-20250929", Name = "Claude Sonnet 4.5", Description = "Balanced capability and efficiency", MaxTokens = 200000 },
                new ModelInfoDto { Id = "claude-sonnet-4-20250514", Name = "Claude Sonnet 4", Description = "Previous generation Sonnet", MaxTokens = 200000 },
                new ModelInfoDto { Id = "claude-3-5-sonnet-20241022", Name = "Claude 3.5 Sonnet", Description = "Efficient Claude 3.5", MaxTokens = 200000 },
                new ModelInfoDto { Id = "claude-3-5-haiku-20241022", Name = "Claude 3.5 Haiku", Description = "Fast and lightweight", MaxTokens = 200000 }
            }
        },
        ["Azure"] = new ProviderInfo
        {
            DisplayName = "Azure OpenAI",
            EmbeddingModels = new[]
            {
                new ModelInfoDto { Id = "text-embedding-3-small", Name = "text-embedding-3-small", Description = "Azure deployment name", Dimensions = 1536 },
                new ModelInfoDto { Id = "text-embedding-3-large", Name = "text-embedding-3-large", Description = "Azure deployment name", Dimensions = 3072 }
            },
            LlmModels = new[]
            {
                new ModelInfoDto { Id = "gpt-4o", Name = "GPT-4o", Description = "Azure deployment name", MaxTokens = 128000 },
                new ModelInfoDto { Id = "gpt-4o-mini", Name = "GPT-4o Mini", Description = "Azure deployment name", MaxTokens = 128000 },
                new ModelInfoDto { Id = "gpt-4", Name = "GPT-4", Description = "Azure deployment name", MaxTokens = 128000 }
            }
        },
        ["Cohere"] = new ProviderInfo
        {
            DisplayName = "Cohere",
            EmbeddingModels = new[]
            {
                new ModelInfoDto { Id = "embed-english-v3.0", Name = "Embed English v3", Description = "English embedding model", Dimensions = 1024 },
                new ModelInfoDto { Id = "embed-multilingual-v3.0", Name = "Embed Multilingual v3", Description = "Multilingual embedding model", Dimensions = 1024 },
                new ModelInfoDto { Id = "embed-english-light-v3.0", Name = "Embed English Light v3", Description = "Lightweight English embedding", Dimensions = 384 }
            },
            LlmModels = new[]
            {
                new ModelInfoDto { Id = "command-a-08-2025", Name = "Command A", Description = "Latest flagship Cohere model", MaxTokens = 128000 },
                new ModelInfoDto { Id = "command-a-reasoning-08-2025", Name = "Command A Reasoning", Description = "Enhanced reasoning capabilities", MaxTokens = 128000 },
                new ModelInfoDto { Id = "command-r-plus-08-2024", Name = "Command R+", Description = "Powerful conversational model", MaxTokens = 128000 },
                new ModelInfoDto { Id = "command-r-08-2024", Name = "Command R", Description = "Balanced performance", MaxTokens = 128000 }
            }
        },
        ["Google"] = new ProviderInfo
        {
            DisplayName = "Google (Gemini)",
            EmbeddingModels = new[]
            {
                new ModelInfoDto { Id = "text-embedding-004", Name = "Gemini Embedding", Description = "Google's text embedding model", Dimensions = 768 },
                new ModelInfoDto { Id = "text-multilingual-embedding-002", Name = "Multilingual Embedding", Description = "Multilingual support", Dimensions = 768 }
            },
            LlmModels = new[]
            {
                new ModelInfoDto { Id = "gemini-3-pro", Name = "Gemini 3 Pro", Description = "Most powerful multimodal model", MaxTokens = 2000000 },
                new ModelInfoDto { Id = "gemini-2.0-flash", Name = "Gemini 2.0 Flash", Description = "Fast and efficient model", MaxTokens = 1000000 },
                new ModelInfoDto { Id = "gemini-1.5-pro", Name = "Gemini 1.5 Pro", Description = "High capability model", MaxTokens = 2000000 },
                new ModelInfoDto { Id = "gemini-1.5-flash", Name = "Gemini 1.5 Flash", Description = "Cost-effective model", MaxTokens = 1000000 }
            }
        },
        ["Local"] = new ProviderInfo
        {
            DisplayName = "Local (Ollama)",
            EmbeddingModels = new[]
            {
                new ModelInfoDto { Id = "nomic-embed-text", Name = "Nomic Embed Text", Description = "Open source embedding", Dimensions = 768 },
                new ModelInfoDto { Id = "mxbai-embed-large", Name = "MxBai Embed Large", Description = "High quality embedding", Dimensions = 1024 },
                new ModelInfoDto { Id = "all-minilm", Name = "All-MiniLM", Description = "Lightweight embedding", Dimensions = 384 }
            },
            LlmModels = new[]
            {
                new ModelInfoDto { Id = "llama3.3", Name = "Llama 3.3", Description = "Meta's latest open model", MaxTokens = 128000 },
                new ModelInfoDto { Id = "qwen2.5", Name = "Qwen 2.5", Description = "Alibaba's capable model", MaxTokens = 128000 },
                new ModelInfoDto { Id = "mistral-large", Name = "Mistral Large", Description = "Mistral's flagship model", MaxTokens = 128000 },
                new ModelInfoDto { Id = "deepseek-r1", Name = "DeepSeek R1", Description = "Reasoning-focused model", MaxTokens = 64000 }
            }
        },
        ["GPUStack"] = new ProviderInfo
        {
            DisplayName = "GPUStack",
            RequiresEndpoint = true,
            EmbeddingModels = new[]
            {
                new ModelInfoDto { Id = "gpt-oss", Name = "GPT-OSS", Description = "OpenAI-compatible embedding model", Dimensions = 1536 },
                new ModelInfoDto { Id = "bge-m3", Name = "BGE-M3", Description = "BAAI multilingual embedding", Dimensions = 1024 },
                new ModelInfoDto { Id = "nomic-embed-text", Name = "Nomic Embed Text", Description = "Nomic embedding model", Dimensions = 768 }
            },
            LlmModels = new[]
            {
                new ModelInfoDto { Id = "gpt-oss", Name = "GPT-OSS", Description = "OpenAI-compatible LLM", MaxTokens = 128000 },
                new ModelInfoDto { Id = "qwen2.5-72b-instruct", Name = "Qwen 2.5 72B", Description = "Alibaba's large model", MaxTokens = 128000 },
                new ModelInfoDto { Id = "llama-3.3-70b-instruct", Name = "Llama 3.3 70B", Description = "Meta's large model", MaxTokens = 128000 }
            }
        }
    };

    public AiProviderSettingsService(
        IAiProviderSettingsRepository repository,
        ILogger<AiProviderSettingsService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AiProviderSettingsDto>> GetAllProvidersAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetAllAsync(cancellationToken);
        return settings.Select(s => ToDto(s)).ToList();
    }

    public async Task<AiProviderSettingsDto?> GetProviderAsync(string providerName, CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetByProviderNameAsync(providerName, cancellationToken);
        return settings != null ? ToDto(settings) : null;
    }

    public async Task<AiConfigurationStatusDto> GetConfigurationStatusAsync(CancellationToken cancellationToken = default)
    {
        var allSettings = await _repository.GetAllAsync(cancellationToken);
        var defaultEmbedding = allSettings.FirstOrDefault(s => s.IsDefaultEmbedding && s.IsEnabled);
        var defaultLlm = allSettings.FirstOrDefault(s => s.IsDefaultLlm && s.IsEnabled);

        return new AiConfigurationStatusDto
        {
            HasEmbeddingProvider = defaultEmbedding != null,
            HasLlmProvider = defaultLlm != null,
            DefaultEmbeddingProvider = defaultEmbedding?.DisplayName,
            DefaultEmbeddingModel = defaultEmbedding?.EmbeddingModel,
            DefaultLlmProvider = defaultLlm?.DisplayName,
            DefaultLlmModel = defaultLlm?.LlmModel,
            Providers = allSettings.Select(s => ToDto(s)).ToList()
        };
    }

    public async Task<AiProviderSettingsDto> UpdateProviderAsync(string providerName, UpdateAiProviderRequest request, CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetByProviderNameAsync(providerName, cancellationToken);

        if (settings == null)
        {
            // Create new settings for this provider
            if (!KnownProviders.TryGetValue(providerName, out var providerInfo))
            {
                throw new KeyNotFoundException($"Unknown provider: {providerName}");
            }

            settings = AiProviderSettings.Create(
                providerName,
                providerInfo.DisplayName,
                request.ApiKey,
                request.EmbeddingModel,
                request.LlmModel,
                request.EndpointUrl);

            await _repository.AddAsync(settings, cancellationToken);
        }
        else
        {
            // Update existing settings
            if (request.ApiKey != null)
            {
                settings.UpdateApiKey(request.ApiKey);
            }

            if (request.EmbeddingModel != null)
            {
                settings.SetEmbeddingModel(request.EmbeddingModel);
            }

            if (request.LlmModel != null)
            {
                settings.SetLlmModel(request.LlmModel);
            }

            if (request.EndpointUrl != null)
            {
                settings.SetEndpointUrl(request.EndpointUrl);
            }

            if (request.IsEnabled.HasValue)
            {
                settings.SetEnabled(request.IsEnabled.Value);
            }

            await _repository.UpdateAsync(settings, cancellationToken);
        }

        // Handle default provider settings
        if (request.IsDefaultEmbedding == true)
        {
            await _repository.ClearDefaultEmbeddingAsync(cancellationToken);
            settings.SetAsDefaultEmbedding(true);
            await _repository.UpdateAsync(settings, cancellationToken);
        }
        else if (request.IsDefaultEmbedding == false)
        {
            settings.SetAsDefaultEmbedding(false);
            await _repository.UpdateAsync(settings, cancellationToken);
        }

        if (request.IsDefaultLlm == true)
        {
            await _repository.ClearDefaultLlmAsync(cancellationToken);
            settings.SetAsDefaultLlm(true);
            await _repository.UpdateAsync(settings, cancellationToken);
        }
        else if (request.IsDefaultLlm == false)
        {
            settings.SetAsDefaultLlm(false);
            await _repository.UpdateAsync(settings, cancellationToken);
        }

        _logger.LogInformation("Updated AI provider settings for {Provider}", providerName);

        return ToDto(settings);
    }

    public async Task<bool> TestProviderConnectionAsync(string providerName, CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetByProviderNameAsync(providerName, cancellationToken);

        if (settings == null || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return false;
        }

        // TODO: Implement actual API testing for each provider
        // For now, just check if API key exists
        _logger.LogInformation("Testing connection for provider {Provider}", providerName);

        return true;
    }

    public Task<AvailableModelsDto> GetAvailableModelsAsync(string providerName, CancellationToken cancellationToken = default)
    {
        if (!KnownProviders.TryGetValue(providerName, out var providerInfo))
        {
            throw new KeyNotFoundException($"Unknown provider: {providerName}");
        }

        return Task.FromResult(new AvailableModelsDto
        {
            ProviderName = providerName,
            EmbeddingModels = providerInfo.EmbeddingModels.ToList(),
            LlmModels = providerInfo.LlmModels.ToList()
        });
    }

    public async Task InitializeDefaultProvidersAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetAllAsync(cancellationToken);

        foreach (var (providerName, providerInfo) in KnownProviders)
        {
            if (existing.All(e => e.ProviderName != providerName))
            {
                var settings = AiProviderSettings.Create(
                    providerName,
                    providerInfo.DisplayName);

                await _repository.AddAsync(settings, cancellationToken);
                _logger.LogInformation("Initialized default settings for provider {Provider}", providerName);
            }
        }
    }

    private AiProviderSettingsDto ToDto(AiProviderSettings settings)
    {
        KnownProviders.TryGetValue(settings.ProviderName, out var providerInfo);

        return new AiProviderSettingsDto
        {
            Id = settings.Id,
            ProviderName = settings.ProviderName,
            DisplayName = settings.DisplayName,
            HasApiKey = !string.IsNullOrWhiteSpace(settings.ApiKey),
            IsEnabled = settings.IsEnabled,
            IsDefaultEmbedding = settings.IsDefaultEmbedding,
            IsDefaultLlm = settings.IsDefaultLlm,
            EmbeddingModel = settings.EmbeddingModel,
            LlmModel = settings.LlmModel,
            EndpointUrl = settings.EndpointUrl,
            AvailableEmbeddingModels = providerInfo?.EmbeddingModels.Select(m => m.Id).ToList() ?? new List<string>(),
            AvailableLlmModels = providerInfo?.LlmModels.Select(m => m.Id).ToList() ?? new List<string>(),
            CreatedAt = settings.CreatedAt,
            UpdatedAt = settings.UpdatedAt
        };
    }

    private class ProviderInfo
    {
        public string DisplayName { get; set; } = string.Empty;
        public bool RequiresEndpoint { get; set; } = false;
        public ModelInfoDto[] EmbeddingModels { get; set; } = Array.Empty<ModelInfoDto>();
        public ModelInfoDto[] LlmModels { get; set; } = Array.Empty<ModelInfoDto>();
    }
}
