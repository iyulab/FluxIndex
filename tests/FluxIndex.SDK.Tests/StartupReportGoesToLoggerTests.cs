using System.Collections.Concurrent;
using AwesomeAssertions;
using FluxIndex.SDK;
using FluxIndex.SDK.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluxIndex.SDK.Tests;

[CollectionDefinition(nameof(StartupReportCollection), DisableParallelization = true)]
public sealed class StartupReportCollection;

/// <summary>
/// Building a context reports its AI services through the logger and writes nothing to standard output. Until 0.66.0
/// the report was printed to the console by default, which corrupts a host that speaks a protocol on stdout.
/// The report is once per process (a static flag), so this runs outside the parallel collections.
/// </summary>
[Collection(nameof(StartupReportCollection))]
public sealed class StartupReportGoesToLoggerTests
{
    [Fact]
    public async Task Build_LogsTheServiceReport_AndWritesNothingToStandardOutput()
    {
        var captured = new CapturingLoggerProvider();
        var stdout = new StringWriter();
        var original = Console.Out;
        StartupMessageService.Reset();
        try
        {
            Console.SetOut(stdout);
            await using var context = (FluxIndexContext)FluxIndexContext.CreateBuilder()
                .ConfigureServices(s => s.AddLogging(b => b.SetMinimumLevel(LogLevel.Information).AddProvider(captured)))
                .Build();
        }
        finally
        {
            Console.SetOut(original);
            StartupMessageService.Suppress();
        }

        stdout.ToString().Should().BeEmpty();
        captured.Messages.Should().Contain(m => m.Contains("FluxIndex AI services", StringComparison.Ordinal));
        captured.Messages.Should().Contain(m => m.Contains("in-memory storage", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SuppressStartupMessages_LogsNothing()
    {
        var captured = new CapturingLoggerProvider();
        StartupMessageService.Reset();
        try
        {
            await using var context = (FluxIndexContext)FluxIndexContext.CreateBuilder()
                .ConfigureServices(s => s.AddLogging(b => b.SetMinimumLevel(LogLevel.Information).AddProvider(captured)))
                .SuppressStartupMessages()
                .Build();
        }
        finally
        {
            StartupMessageService.Suppress();
        }

        captured.Messages.Should().NotContain(m => m.Contains("FluxIndex AI services", StringComparison.Ordinal));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Enqueue(formatter(state, exception));
        }
    }
}
