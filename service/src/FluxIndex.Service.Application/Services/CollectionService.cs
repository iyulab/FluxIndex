using FluxIndex.Service.Application.Interfaces.Repositories;
using FluxIndex.Service.Application.Interfaces.Services;
using FluxIndex.Service.Application.Mappings;
using FluxIndex.Service.Domain.Entities;
using FluxIndex.Service.Shared.Common;
using FluxIndex.Service.Shared.DTOs.Collections;

namespace FluxIndex.Service.Application.Services;

/// <summary>
/// Service implementation for collection operations.
/// </summary>
public class CollectionService : ICollectionService
{
    private readonly ICollectionRepository _collectionRepository;

    public CollectionService(ICollectionRepository collectionRepository)
    {
        _collectionRepository = collectionRepository;
    }

    public async Task<CollectionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var collection = await _collectionRepository.GetByIdAsync(id, cancellationToken);
        if (collection == null) return null;

        var documentCount = await _collectionRepository.GetDocumentCountAsync(id, cancellationToken);
        return collection.ToDto(documentCount);
    }

    public async Task<CollectionDto?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var collection = await _collectionRepository.GetByNameAsync(name, cancellationToken);
        if (collection == null) return null;

        var documentCount = await _collectionRepository.GetDocumentCountAsync(collection.Id, cancellationToken);
        return collection.ToDto(documentCount);
    }

    public async Task<List<CollectionDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var collections = await _collectionRepository.GetAllAsync(cancellationToken);
        var result = new List<CollectionDto>();

        foreach (var collection in collections)
        {
            var documentCount = await _collectionRepository.GetDocumentCountAsync(collection.Id, cancellationToken);
            result.Add(collection.ToDto(documentCount));
        }

        return result;
    }

    public async Task<PagedResult<CollectionDto>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _collectionRepository.GetPagedAsync(page, pageSize, cancellationToken);
        var dtos = new List<CollectionDto>();

        foreach (var collection in items)
        {
            var documentCount = await _collectionRepository.GetDocumentCountAsync(collection.Id, cancellationToken);
            dtos.Add(collection.ToDto(documentCount));
        }

        return PagedResult<CollectionDto>.Create(dtos, page, pageSize, totalCount);
    }

    public async Task<CollectionDto> CreateAsync(CreateCollectionRequest request, CancellationToken cancellationToken = default)
    {
        if (await _collectionRepository.NameExistsAsync(request.Name, cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException($"Collection with name '{request.Name}' already exists.");
        }

        var collection = Collection.Create(request.Name, request.Description);

        if (request.Settings != null)
        {
            collection.UpdateSettings(request.Settings.ToEntity());
        }

        await _collectionRepository.AddAsync(collection, cancellationToken);
        return collection.ToDto(0);
    }

    public async Task<CollectionDto> UpdateAsync(Guid id, UpdateCollectionRequest request, CancellationToken cancellationToken = default)
    {
        var collection = await _collectionRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Collection with id '{id}' not found.");

        if (await _collectionRepository.NameExistsAsync(request.Name, id, cancellationToken))
        {
            throw new InvalidOperationException($"Collection with name '{request.Name}' already exists.");
        }

        collection.Update(request.Name, request.Description);

        if (request.Settings != null)
        {
            collection.UpdateSettings(request.Settings.ToEntity());
        }

        await _collectionRepository.UpdateAsync(collection, cancellationToken);

        var documentCount = await _collectionRepository.GetDocumentCountAsync(id, cancellationToken);
        return collection.ToDto(documentCount);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await _collectionRepository.ExistsAsync(id, cancellationToken))
        {
            throw new KeyNotFoundException($"Collection with id '{id}' not found.");
        }

        await _collectionRepository.DeleteAsync(id, cancellationToken);
    }
}
