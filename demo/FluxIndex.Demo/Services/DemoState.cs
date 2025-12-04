using System.Collections.Concurrent;

namespace FluxIndex.Demo.Services;

/// <summary>
/// Shared state for the demo application
/// </summary>
public class DemoState
{
    private readonly ConcurrentDictionary<string, DocumentInfo> _documents = new();

    public int TotalDocuments => _documents.Count;
    public int TotalChunks { get; private set; }
    public DateTime? LastIndexed { get; private set; }

    public void AddDocument(string id, string title, int chunkCount)
    {
        _documents[id] = new DocumentInfo
        {
            Id = id,
            Title = title,
            ChunkCount = chunkCount,
            CreatedAt = DateTime.UtcNow
        };
        TotalChunks += chunkCount;
        LastIndexed = DateTime.UtcNow;
    }

    public void RemoveDocument(string id)
    {
        if (_documents.TryRemove(id, out var doc))
        {
            TotalChunks -= doc.ChunkCount;
        }
    }

    public void UpdateStats(int documents, int chunks)
    {
        TotalChunks = chunks;
        LastIndexed = DateTime.UtcNow;
    }

    public IEnumerable<DocumentInfo> GetDocumentList()
    {
        return _documents.Values.OrderByDescending(d => d.CreatedAt);
    }
}

public class DocumentInfo
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public int ChunkCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
