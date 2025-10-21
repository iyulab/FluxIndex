using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using FluxIndex.Benchmarks.Benchmarks;

namespace FluxIndex.Benchmarks;

/// <summary>
/// FluxIndex 성능 벤치마크 실행 진입점
/// Week 2 최적화 검증을 위한 벤치마크 스위트
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        // API 설정 검증 모드
        if (args.Length > 0 && args[0].Equals("verify", StringComparison.OrdinalIgnoreCase))
        {
            await VerifyApiConfig.RunVerification();
            return;
        }

        // Quick Baseline 측정 모드
        if (args.Length > 0 && args[0].Equals("baseline", StringComparison.OrdinalIgnoreCase))
        {
            await QuickBaseline.RunQuickBaseline();
            return;
        }

        // Cache Effectiveness 측정 모드
        if (args.Length > 0 && args[0].Equals("cache", StringComparison.OrdinalIgnoreCase))
        {
            await CacheEffectiveness.RunCacheTest();
            return;
        }

        // BenchmarkDotNet configuration
        var config = DefaultConfig.Instance;

        // 명령줄 인자가 없으면 사용법 출력
        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        // 벤치마크 선택 및 실행
        var summary = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("FluxIndex Benchmarks - Week 2 최적화 검증");
        Console.WriteLine("=========================================");
        Console.WriteLine();
        Console.WriteLine("사용법:");
        Console.WriteLine("  dotnet run -c Release -- [options]");
        Console.WriteLine();
        Console.WriteLine("Quick Performance Tests:");
        Console.WriteLine("  dotnet run -- verify               # .env.local 및 OpenAI API 연결 테스트");
        Console.WriteLine("  dotnet run -- baseline             # 빠른 baseline 성능 측정 (5회 반복)");
        Console.WriteLine("  dotnet run -- cache                # 임베딩 캐시 효과 측정 (Phase 7.3)");
        Console.WriteLine();
        Console.WriteLine("벤치마크 선택:");
        Console.WriteLine("  --filter *SearchPerformance*   # 검색 성능 벤치마크만 실행");
        Console.WriteLine("  --filter *BatchIndexing*       # 배치 인덱싱 벤치마크만 실행");
        Console.WriteLine("  --list tree                    # 사용 가능한 모든 벤치마크 표시");
        Console.WriteLine();
        Console.WriteLine("예제:");
        Console.WriteLine("  # 모든 벤치마크 실행");
        Console.WriteLine("  dotnet run -c Release -- --filter *");
        Console.WriteLine();
        Console.WriteLine("  # 검색 성능만 테스트");
        Console.WriteLine("  dotnet run -c Release -- --filter *SearchPerformance*");
        Console.WriteLine();
        Console.WriteLine("  # 특정 메서드만 실행");
        Console.WriteLine("  dotnet run -c Release -- --filter *ComplexSemanticSearch*");
        Console.WriteLine();
        Console.WriteLine("주요 벤치마크:");
        Console.WriteLine("  - SearchPerformanceBenchmark");
        Console.WriteLine("    · SimpleKeywordSearch (10-30 tokens)");
        Console.WriteLine("    · ComplexSemanticSearch (50-150 tokens) - 목표: 510ms → 200-250ms");
        Console.WriteLine("    · HybridSearch (30-80 tokens)");
        Console.WriteLine("    · SearchWithVariousTopK (K=5,10,20,50)");
        Console.WriteLine("    · BatchSearch vs SequentialSearch");
        Console.WriteLine();
        Console.WriteLine("  - BatchIndexingBenchmark");
        Console.WriteLine("    · IndexSmallBatch_100Chunks - 목표: 500ms → 50-100ms");
        Console.WriteLine("    · IndexMediumBatch_1000Chunks - 목표: 5s → 500ms-1s");
        Console.WriteLine("    · IndexLargeBatch_10000Chunks - 목표: 50s → 5-10s");
        Console.WriteLine("    · IndexWithVaryingParallelism (1,4,8,16 threads)");
        Console.WriteLine();
        Console.WriteLine("BenchmarkDotNet 옵션:");
        Console.WriteLine("  --memory          # 메모리 프로파일링 포함");
        Console.WriteLine("  --runtimes        # 여러 런타임에서 실행 (예: net9.0)");
        Console.WriteLine("  --job short       # 빠른 실행 (정확도 낮음)");
        Console.WriteLine("  --exporters json  # JSON 형식으로 결과 내보내기");
        Console.WriteLine();
    }
}
