using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FluxIndex.Extensions.FileFlux;
using Microsoft.Extensions.DependencyInjection;

namespace FileFluxIndexSample;

/// <summary>
/// BenchmarkDotNet을 사용한 종합 성능 벤치마크
/// </summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 10)]
public class FullBenchmark
{
    private IFileFluxIntegration _integration = null!;
    private string[] _testFiles = null!;
    private string[] _testQueries = null!;

    [GlobalSetup]
    public void Setup()
    {
        // DI 컨테이너 설정 (간소화된 버전)
        var services = new ServiceCollection();
        
        // 여기서는 Mock 서비스 사용 (실제 벤치마크에서는 실제 서비스 주입)
        services.AddSingleton<IFileFluxIntegration>(sp => 
            new MockFileFluxIntegration());
        
        var provider = services.BuildServiceProvider();
        _integration = provider.GetRequiredService<IFileFluxIntegration>();
        
        // 테스트 데이터 준비
        _testFiles = new[]
        {
            "TestDocuments/sample1.pdf",
            "TestDocuments/sample2.docx",
            "TestDocuments/sample3.txt"
        };
        
        _testQueries = new[]
        {
            "battery optimization",
            "security best practices",
            "performance tuning"
        };
    }

    [Benchmark(Baseline = true)]
    public async Task ProcessSingleDocument()
    {
        await _integration.ProcessAndIndexAsync(_testFiles[0]);
    }

    [Benchmark]
    public async Task ProcessMultipleDocuments()
    {
        var tasks = _testFiles.Select(file => 
            _integration.ProcessAndIndexAsync(file));
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task SearchWithSemanticReranking()
    {
        foreach (var query in _testQueries)
        {
            await _integration.SearchWithStrategyAsync(
                query, 
                RerankingStrategy.Semantic);
        }
    }

    [Benchmark]
    public async Task SearchWithAdaptiveReranking()
    {
        foreach (var query in _testQueries)
        {
            await _integration.SearchWithStrategyAsync(
                query, 
                RerankingStrategy.Adaptive);
        }
    }

    [Benchmark]
    public async Task StreamingProcessing()
    {
        await foreach (var progress in _integration.ProcessWithProgressAsync(_testFiles[0]))
        {
            // 스트리밍 처리 시뮬레이션
            if (progress.Status == ProcessingStatus.Completed)
                break;
        }
    }

    public async Task RunAsync()
    {
        // BenchmarkDotNet 실행
        var summary = BenchmarkRunner.Run<FullBenchmark>();
        
        // 결과를 콘솔에 출력 (BenchmarkDotNet이 자동으로 처리)
        await Task.CompletedTask;
    }
}

// 벤치마크용 Mock 구현
internal class MockFileFluxIntegration : IFileFluxIntegration
{
    public async Task<ProcessingResult> ProcessAndIndexAsync(
        string filePath, 
        ProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // 실제 처리 시뮬레이션
        await Task.Delay(100, cancellationToken);
        
        return new ProcessingResult
        {
            Success = true,
            DocumentId = Guid.NewGuid().ToString(),
            ChunkCount = Random.Shared.Next(10, 50),
            AverageQualityScore = Random.Shared.NextDouble(),
            MetadataCount = Random.Shared.Next(5, 20)
        };
    }

    public async IAsyncEnumerable<ProcessingProgress> ProcessWithProgressAsync(
        string filePath,
        ProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(10, cancellationToken);
            yield return new ProcessingProgress
            {
                ChunkIndex = i,
                QualityScore = Random.Shared.NextDouble(),
                TokenCount = Random.Shared.Next(100, 500),
                Status = ProcessingStatus.InProgress
            };
        }
        
        yield return new ProcessingProgress
        {
            ChunkIndex = 10,
            Status = ProcessingStatus.Completed
        };
    }

    public async Task<List<SearchResult>> SearchWithStrategyAsync(
        string query,
        RerankingStrategy strategy,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken);
        
        var results = new List<SearchResult>();
        for (int i = 0; i < topK; i++)
        {
            results.Add(new SearchResult
            {
                Score = Random.Shared.NextDouble()
            });
        }
        
        return results;
    }

    public async Task<MultimodalResult> ProcessMultimodalDocumentAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(150, cancellationToken);
        
        return new MultimodalResult
        {
            DocumentId = Guid.NewGuid().ToString(),
            TotalChunks = Random.Shared.Next(20, 100),
            TextChunks = Random.Shared.Next(15, 80),
            ImageChunks = Random.Shared.Next(5, 20),
            AverageQuality = Random.Shared.NextDouble()
        };
    }
}