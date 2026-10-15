using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Tests.Contract;

/// <summary>
/// Presents an <see cref="ILogger"/> a contract suite hands over as the <see cref="ILogger{TCategoryName}"/> a backend's
/// constructor asks for.
/// </summary>
public sealed class TypedLogger<T>(ILogger inner) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => inner.Log(logLevel, eventId, state, exception, formatter);
}
