using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Shared.DTOs.Collections;

namespace FluxIndex.Stack.Application.Mappings;

/// <summary>
/// Extension methods for mapping Collection entities to DTOs.
/// </summary>
public static class CollectionMappings
{
    public static CollectionDto ToDto(this Collection entity, int documentCount = 0)
    {
        return new CollectionDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            Settings = entity.Settings.ToDto(),
            DocumentCount = documentCount,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    public static CollectionSettingsDto ToDto(this CollectionSettings settings)
    {
        return new CollectionSettingsDto
        {
            ChunkSize = settings.ChunkSize,
            ChunkOverlap = settings.ChunkOverlap,
            ChunkingStrategy = settings.ChunkingStrategy,
            EnableQAGeneration = settings.EnableQAGeneration,
            EnableEnrichment = settings.EnableEnrichment,
            CustomSettings = settings.CustomSettings
        };
    }

    public static CollectionSettings ToEntity(this CollectionSettingsDto dto)
    {
        return new CollectionSettings
        {
            ChunkSize = dto.ChunkSize,
            ChunkOverlap = dto.ChunkOverlap,
            ChunkingStrategy = dto.ChunkingStrategy,
            EnableQAGeneration = dto.EnableQAGeneration,
            EnableEnrichment = dto.EnableEnrichment,
            CustomSettings = dto.CustomSettings
        };
    }
}
