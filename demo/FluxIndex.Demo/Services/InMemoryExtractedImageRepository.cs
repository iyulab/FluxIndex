using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using System.Collections.Concurrent;

namespace FluxIndex.Demo.Services;

/// <summary>
/// In-memory implementation of IExtractedImageRepository for the Demo application.
/// </summary>
public class InMemoryExtractedImageRepository : IExtractedImageRepository
{
    private readonly ConcurrentDictionary<string, ExtractedImage> _images = new();
    private readonly ILogger<InMemoryExtractedImageRepository>? _logger;

    public InMemoryExtractedImageRepository(ILogger<InMemoryExtractedImageRepository>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<ExtractedImage?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        _images.TryGetValue(id, out var image);
        return Task.FromResult(image);
    }

    /// <inheritdoc />
    public Task<IEnumerable<ExtractedImage>> GetByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var images = _images.Values
            .Where(img => img.DocumentId == documentId)
            .OrderBy(img => img.PositionIndex)
            .ToList();

        return Task.FromResult<IEnumerable<ExtractedImage>>(images);
    }

    /// <inheritdoc />
    public Task<IEnumerable<ExtractedImage>> GetByChunkIdAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        var images = _images.Values
            .Where(img => img.ChunkId == chunkId)
            .OrderBy(img => img.PositionIndex)
            .ToList();

        return Task.FromResult<IEnumerable<ExtractedImage>>(images);
    }

    /// <inheritdoc />
    public Task<string> AddAsync(ExtractedImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        _images[image.Id] = image;

        _logger?.LogDebug(
            "Added extracted image {ImageId} for document {DocumentId}",
            image.Id, image.DocumentId);

        return Task.FromResult(image.Id);
    }

    /// <inheritdoc />
    public Task<int> AddRangeAsync(
        IEnumerable<ExtractedImage> images,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(images);

        var count = 0;
        foreach (var image in images)
        {
            _images[image.Id] = image;
            count++;
        }

        _logger?.LogDebug("Added {Count} extracted images", count);

        return Task.FromResult(count);
    }

    /// <inheritdoc />
    public Task UpdateAsync(ExtractedImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (!_images.ContainsKey(image.Id))
        {
            throw new KeyNotFoundException($"Image with ID {image.Id} not found");
        }

        _images[image.Id] = image;

        _logger?.LogDebug("Updated extracted image {ImageId}", image.Id);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var removed = _images.TryRemove(id, out _);

        if (removed)
        {
            _logger?.LogDebug("Deleted extracted image {ImageId}", id);
        }

        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<int> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var keysToRemove = _images
            .Where(kvp => kvp.Value.DocumentId == documentId)
            .Select(kvp => kvp.Key)
            .ToList();

        var count = 0;
        foreach (var key in keysToRemove)
        {
            if (_images.TryRemove(key, out _))
            {
                count++;
            }
        }

        _logger?.LogDebug(
            "Deleted {Count} extracted images for document {DocumentId}",
            count, documentId);

        return Task.FromResult(count);
    }

    /// <inheritdoc />
    public Task<int> DeleteByChunkIdAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        var keysToRemove = _images
            .Where(kvp => kvp.Value.ChunkId == chunkId)
            .Select(kvp => kvp.Key)
            .ToList();

        var count = 0;
        foreach (var key in keysToRemove)
        {
            if (_images.TryRemove(key, out _))
            {
                count++;
            }
        }

        _logger?.LogDebug(
            "Deleted {Count} extracted images for chunk {ChunkId}",
            count, chunkId);

        return Task.FromResult(count);
    }

    /// <inheritdoc />
    public Task<ExtractedImage?> FindByHashAsync(
        string documentId,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        var image = _images.Values
            .FirstOrDefault(img =>
                img.DocumentId == documentId &&
                img.ContentHash == contentHash);

        return Task.FromResult(image);
    }

    /// <inheritdoc />
    public Task<int> CountByDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var count = _images.Values.Count(img => img.DocumentId == documentId);
        return Task.FromResult(count);
    }

    /// <inheritdoc />
    public Task<long> GetTotalSizeByDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var totalSize = _images.Values
            .Where(img => img.DocumentId == documentId)
            .Sum(img => img.SizeBytes);

        return Task.FromResult(totalSize);
    }

    /// <inheritdoc />
    public Task<IEnumerable<ExtractedImage>> GetWithoutDescriptionAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var images = _images.Values
            .Where(img => string.IsNullOrEmpty(img.Description))
            .Take(limit)
            .ToList();

        return Task.FromResult<IEnumerable<ExtractedImage>>(images);
    }

    /// <summary>
    /// Gets all images (for debugging/testing).
    /// </summary>
    public IEnumerable<ExtractedImage> GetAll()
    {
        return _images.Values.ToList();
    }

    /// <summary>
    /// Clears all images (for testing).
    /// </summary>
    public void Clear()
    {
        _images.Clear();
        _logger?.LogDebug("Cleared all extracted images");
    }
}
