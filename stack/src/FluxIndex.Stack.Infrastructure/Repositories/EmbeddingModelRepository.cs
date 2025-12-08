using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Stack.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for EmbeddingModel entity.
/// </summary>
public class EmbeddingModelRepository : IEmbeddingModelRepository
{
    private readonly ServiceDbContext _context;

    public EmbeddingModelRepository(ServiceDbContext context)
    {
        _context = context;
    }

    public async Task<EmbeddingModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.EmbeddingModels
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<EmbeddingModel?> GetByModelKeyAsync(string modelKey, CancellationToken cancellationToken = default)
    {
        return await _context.EmbeddingModels
            .FirstOrDefaultAsync(e => e.ModelKey == modelKey, cancellationToken);
    }

    public async Task<EmbeddingModel?> GetActiveModelAsync(CancellationToken cancellationToken = default)
    {
        return await _context.EmbeddingModels
            .FirstOrDefaultAsync(e => e.IsActive, cancellationToken);
    }

    public async Task<List<EmbeddingModel>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.EmbeddingModels
            .OrderByDescending(e => e.IsActive)
            .ThenBy(e => e.ProviderName)
            .ThenBy(e => e.ModelName)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(EmbeddingModel model, CancellationToken cancellationToken = default)
    {
        await _context.EmbeddingModels.AddAsync(model, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(EmbeddingModel model, CancellationToken cancellationToken = default)
    {
        _context.EmbeddingModels.Update(model);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var model = await _context.EmbeddingModels.FindAsync([id], cancellationToken);
        if (model != null)
        {
            _context.EmbeddingModels.Remove(model);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<EmbeddingModel> GetOrCreateAsync(
        string providerName,
        string modelName,
        int dimension,
        CancellationToken cancellationToken = default)
    {
        var modelKey = EmbeddingModel.GenerateModelKey(providerName, modelName);
        var existing = await GetByModelKeyAsync(modelKey, cancellationToken);

        if (existing != null)
        {
            return existing;
        }

        // Check if there are any existing models to determine if this should be active
        var hasExistingModels = await _context.EmbeddingModels.AnyAsync(cancellationToken);

        var newModel = EmbeddingModel.Create(providerName, modelName, dimension);

        // First model added becomes the active model
        if (!hasExistingModels)
        {
            newModel.SetActive(true);
        }

        await AddAsync(newModel, cancellationToken);
        return newModel;
    }

    public async Task SetActiveModelAsync(Guid modelId, CancellationToken cancellationToken = default)
    {
        // Deactivate all models
        var allModels = await _context.EmbeddingModels.ToListAsync(cancellationToken);
        foreach (var model in allModels)
        {
            model.SetActive(model.Id == modelId);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Dictionary<Guid, int>> GetEmbeddingCountsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ChunkEmbeddings
            .GroupBy(e => e.EmbeddingModelId)
            .Select(g => new { ModelId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ModelId, x => x.Count, cancellationToken);
    }
}
