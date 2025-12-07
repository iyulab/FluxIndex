using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Stack.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for AI provider settings.
/// </summary>
public class AiProviderSettingsRepository : IAiProviderSettingsRepository
{
    private readonly ServiceDbContext _context;

    public AiProviderSettingsRepository(ServiceDbContext context)
    {
        _context = context;
    }

    public async Task<AiProviderSettings?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.AiProviderSettings
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<AiProviderSettings?> GetByProviderNameAsync(string providerName, CancellationToken cancellationToken = default)
    {
        return await _context.AiProviderSettings
            .FirstOrDefaultAsync(s => s.ProviderName == providerName, cancellationToken);
    }

    public async Task<IReadOnlyList<AiProviderSettings>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.AiProviderSettings
            .OrderBy(s => s.ProviderName)
            .ToListAsync(cancellationToken);
    }

    public async Task<AiProviderSettings?> GetDefaultEmbeddingProviderAsync(CancellationToken cancellationToken = default)
    {
        return await _context.AiProviderSettings
            .FirstOrDefaultAsync(s => s.IsDefaultEmbedding && s.IsEnabled, cancellationToken);
    }

    public async Task<AiProviderSettings?> GetDefaultLlmProviderAsync(CancellationToken cancellationToken = default)
    {
        return await _context.AiProviderSettings
            .FirstOrDefaultAsync(s => s.IsDefaultLlm && s.IsEnabled, cancellationToken);
    }

    public async Task AddAsync(AiProviderSettings settings, CancellationToken cancellationToken = default)
    {
        await _context.AiProviderSettings.AddAsync(settings, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(AiProviderSettings settings, CancellationToken cancellationToken = default)
    {
        _context.AiProviderSettings.Update(settings);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var settings = await GetByIdAsync(id, cancellationToken);
        if (settings != null)
        {
            _context.AiProviderSettings.Remove(settings);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ClearDefaultEmbeddingAsync(CancellationToken cancellationToken = default)
    {
        var currentDefaults = await _context.AiProviderSettings
            .Where(s => s.IsDefaultEmbedding)
            .ToListAsync(cancellationToken);

        foreach (var settings in currentDefaults)
        {
            settings.SetAsDefaultEmbedding(false);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearDefaultLlmAsync(CancellationToken cancellationToken = default)
    {
        var currentDefaults = await _context.AiProviderSettings
            .Where(s => s.IsDefaultLlm)
            .ToListAsync(cancellationToken);

        foreach (var settings in currentDefaults)
        {
            settings.SetAsDefaultLlm(false);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
