using FluxIndex.Service.Application.Interfaces.Repositories;
using FluxIndex.Service.Domain.Entities;
using FluxIndex.Service.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Service.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for ApiKey entity.
/// </summary>
public class ApiKeyRepository : IApiKeyRepository
{
    private readonly ServiceDbContext _context;

    public ApiKeyRepository(ServiceDbContext context)
    {
        _context = context;
    }

    public async Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == id, cancellationToken);
    }

    public async Task<ApiKey?> GetByKeyHashAsync(string keyHash, CancellationToken cancellationToken = default)
    {
        return await _context.ApiKeys
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash, cancellationToken);
    }

    public async Task<ApiKey?> GetByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return await _context.ApiKeys
            .FirstOrDefaultAsync(k => k.KeyPrefix == prefix, cancellationToken);
    }

    public async Task<List<ApiKey>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ApiKeys
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<ApiKey> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ApiKeys.OrderByDescending(k => k.CreatedAt);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<ApiKey> AddAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        await _context.ApiKeys.AddAsync(apiKey, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return apiKey;
    }

    public async Task UpdateAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        _context.ApiKeys.Update(apiKey);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var apiKey = await GetByIdAsync(id, cancellationToken);
        if (apiKey != null)
        {
            _context.ApiKeys.Remove(apiKey);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ApiKeys.AnyAsync(k => k.Id == id, cancellationToken);
    }

    public async Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.ApiKeys.Where(k => k.Name == name);
        if (excludeId.HasValue)
        {
            query = query.Where(k => k.Id != excludeId.Value);
        }
        return await query.AnyAsync(cancellationToken);
    }
}
