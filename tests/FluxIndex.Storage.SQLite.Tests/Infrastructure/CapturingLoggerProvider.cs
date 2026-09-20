using Microsoft.Extensions.Logging;

namespace FluxIndex.Storage.SQLite.Tests.Infrastructure;

/// <summary>
/// Captures messages so a test can assert on what an operator would see — and, separately, on
/// what was recorded but kept below the operator's attention. A fact the store still notes at
/// Debug is a different outcome from one it never records, so both are captured and the level
/// is part of the assertion.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<(LogLevel Level, string Message)> _messages = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<string> Warnings => At(l => l >= LogLevel.Warning);

    public IReadOnlyList<string> Debugs => At(l => l == LogLevel.Debug);

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose() { }

    private IReadOnlyList<string> At(Func<LogLevel, bool> predicate)
    {
        lock (_gate) return [.. _messages.Where(m => predicate(m.Level)).Select(m => m.Message)];
    }

    private void Add(LogLevel level, string message)
    {
        lock (_gate) _messages.Add((level, message));
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Debug)
                owner.Add(logLevel, formatter(state, exception));
        }
    }
}
