using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.Entities;
using FluxIndex.Domain.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.SDK.Services;

/// <summary>
/// 메모리 기반 문서 리포지토리 구현 (Core 인터페이스)
/// </summary>
public class InMemoryDocumentRepository : IDocumentRepository
{
    private readonly ConcurrentDictionary<string, Document> _documents = new();

    public Task<Document?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        _documents.TryGetValue(id, out var document);
        return Task.FromResult(document);
    }

    public Task<Document?> GetByFilePathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var document = _documents.Values.FirstOrDefault(d => d.FilePath == filePath);
        return Task.FromResult(document);
    }

    public Task<IEnumerable<Document>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<Document>>(_documents.Values.ToList());
    }

    public Task<IEnumerable<Document>> GetByStatusAsync(DocumentStatus status, CancellationToken cancellationToken = default)
    {
        var documents = _documents.Values.Where(d => d.Status == status);
        return Task.FromResult<IEnumerable<Document>>(documents.ToList());
    }

    public Task<string> AddAsync(Document document, CancellationToken cancellationToken = default)
    {
        _documents.TryAdd(document.Id, document);
        return Task.FromResult(document.Id);
    }

    public Task UpdateAsync(Document document, CancellationToken cancellationToken = default)
    {
        _documents.AddOrUpdate(document.Id, document, (key, old) => document);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_documents.TryRemove(id, out _));
    }

    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_documents.ContainsKey(id));
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_documents.Count);
    }

    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_documents.Count);
    }

    public Task<IEnumerable<Document>> SearchByKeywordAsync(string keyword, int maxResults, CancellationToken cancellationToken = default)
    {
        var results = _documents.Values
            .Where(d => d.Content?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true ||
                       d.FileName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true)
            .Take(maxResults);
        
        return Task.FromResult<IEnumerable<Document>>(results.ToList());
    }
}