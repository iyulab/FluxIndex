using System.Collections.Concurrent;

namespace FluxIndex.Demo.Services;

/// <summary>
/// In-memory log storage for process tracking
/// </summary>
public class ProcessLogService
{
    private readonly ConcurrentQueue<LogEntry> _logs = new();
    private readonly int _maxEntries;

    public ProcessLogService(int maxEntries = 500)
    {
        _maxEntries = maxEntries;
    }

    public void Log(string level, string category, string message, string? details = null)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Category = category,
            Message = message,
            Details = details
        };

        _logs.Enqueue(entry);

        // Trim old entries if over limit
        while (_logs.Count > _maxEntries && _logs.TryDequeue(out _))
        {
        }
    }

    public void Info(string category, string message, string? details = null)
        => Log("info", category, message, details);

    public void Warning(string category, string message, string? details = null)
        => Log("warning", category, message, details);

    public void Error(string category, string message, string? details = null)
        => Log("error", category, message, details);

    public void Success(string category, string message, string? details = null)
        => Log("success", category, message, details);

    public IEnumerable<LogEntry> GetLogs(int? limit = null, string? category = null, string? level = null)
    {
        var query = _logs.AsEnumerable().Reverse();

        if (!string.IsNullOrEmpty(category))
            query = query.Where(l => l.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(level))
            query = query.Where(l => l.Level.Equals(level, StringComparison.OrdinalIgnoreCase));

        if (limit.HasValue)
            query = query.Take(limit.Value);

        return query.ToList();
    }

    public void Clear()
    {
        while (_logs.TryDequeue(out _)) { }
    }
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
}
