using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Shared.DTOs.Jobs;

namespace FluxIndex.Stack.Application.Mappings;

/// <summary>
/// Extension methods for mapping IndexingJob entities to DTOs.
/// </summary>
public static class IndexingJobMappings
{
    public static IndexingJobDto ToDto(this IndexingJob entity, string? documentTitle = null)
    {
        double? durationMs = null;
        if (entity.StartedAt.HasValue && entity.CompletedAt.HasValue)
        {
            durationMs = (entity.CompletedAt.Value - entity.StartedAt.Value).TotalMilliseconds;
        }

        return new IndexingJobDto
        {
            Id = entity.Id,
            DocumentId = entity.DocumentId,
            DocumentTitle = documentTitle ?? string.Empty,
            Status = entity.Status.ToString(),
            TotalChunks = entity.TotalChunks,
            ProcessedChunks = entity.ProcessedChunks,
            ProgressPercentage = entity.GetProgressPercentage(),
            ErrorMessage = entity.ErrorMessage,
            CreatedAt = entity.CreatedAt,
            StartedAt = entity.StartedAt,
            CompletedAt = entity.CompletedAt,
            DurationMs = durationMs
        };
    }

    public static IndexingJobStatus ParseStatus(string status)
    {
        return Enum.TryParse<IndexingJobStatus>(status, true, out var result)
            ? result
            : IndexingJobStatus.Queued;
    }
}
