using AwesomeAssertions;
using FluxIndex.Providers.LMSupply.Services;
using LMSupply;
using LMSupply.Embedder;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.Providers.LMSupply.Tests;

/// <summary>
/// When the model loads, the service says where it runs. LMSupply reports a CPU fallback only through Trace, which a host
/// logging through Microsoft.Extensions.Logging does not see — a GPU request that ended on the CPU stayed invisible.
/// </summary>
public sealed class LMSupplyEmbeddingProviderReportTests
{
    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static IEmbeddingModel Model(ExecutionProvider requested, bool gpu, params string[] providers)
    {
        var model = Substitute.For<IEmbeddingModel>();
        model.ModelId.Returns("bge-m3");
        model.RequestedProvider.Returns(requested);
        model.IsGpuActive.Returns(gpu);
        model.ActiveProviders.Returns(providers);
        return model;
    }

    [Fact]
    public void ACudaRequestThatRunsOnTheCpu_IsAWarning()
    {
        var logger = new RecordingLogger();

        LMSupplyEmbeddingService.ReportProviders(logger, Model(ExecutionProvider.Cuda, gpu: false, "CPUExecutionProvider"));

        logger.Entries.Should().ContainSingle().Which.Should().Match<(LogLevel Level, string Message)>(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("Cuda") && e.Message.Contains("CPUExecutionProvider"));
    }

    [Theory]
    [InlineData(ExecutionProvider.Auto, false, "CPUExecutionProvider")]
    [InlineData(ExecutionProvider.Cuda, true, "CUDAExecutionProvider")]
    [InlineData(ExecutionProvider.Cpu, false, "CPUExecutionProvider")]
    public void OtherwiseTheProvidersAreReported_AsInformation(ExecutionProvider requested, bool gpu, string provider)
    {
        var logger = new RecordingLogger();

        LMSupplyEmbeddingService.ReportProviders(logger, Model(requested, gpu, provider));

        logger.Entries.Should().ContainSingle().Which.Should().Match<(LogLevel Level, string Message)>(e =>
            e.Level == LogLevel.Information && e.Message.Contains(provider));
    }
}
