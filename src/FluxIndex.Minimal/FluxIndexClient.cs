using Microsoft.Extensions.Logging;

namespace FluxIndex;

/// <summary>
/// FluxIndex 클라이언트 - 최소 구현 버전
/// </summary>
public class FluxIndexClient
{
    private readonly ILogger<FluxIndexClient> _logger;

    public FluxIndexClient(ILogger<FluxIndexClient>? logger = null)
    {
        _logger = logger ?? new NullLogger<FluxIndexClient>();
    }

    public async Task<string> IndexDocumentAsync(string content, string documentId)
    {
        _logger.LogInformation("Indexing document: {DocumentId}", documentId);
        // Minimal implementation
        await Task.Delay(100);
        return documentId;
    }

    public async Task<IEnumerable<SearchResult>> SearchAsync(string query, int maxResults = 10)
    {
        _logger.LogInformation("Searching for: {Query}", query);
        // Minimal implementation
        await Task.Delay(100);
        return new List<SearchResult>
        {
            new SearchResult 
            { 
                DocumentId = "doc1",
                Content = "Sample content matching: " + query,
                Score = 0.95f
            }
        };
    }
}

public class SearchResult
{
    public string DocumentId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public float Score { get; set; }
}

public class NullLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}