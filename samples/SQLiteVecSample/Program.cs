using FluxIndex.SDK;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Diagnostics;

namespace SQLiteVecSample;

/// <summary>
/// SQLite-vec 확장을 사용하는 FluxIndex 샘플 애플리케이션
/// sqlite-vec 확장의 성능과 기능을 데모하고 벤치마킹합니다.
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        AnsiConsole.Write(
            new FigletText("SQLite-vec Demo")
                .LeftJustified()
                .Color(Color.Blue));

        AnsiConsole.MarkupLine("[cyan]FluxIndex SQLite-vec 확장 데모 애플리케이션[/]");
        AnsiConsole.MarkupLine("[grey]sqlite-vec 기반 벡터 검색의 성능과 기능을 확인합니다.[/]");
        AnsiConsole.WriteLine();

        try
        {
            // 메뉴 선택
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[green]실행할 데모를 선택하세요:[/]")
                    .AddChoices(
                        "기본 벡터 검색 데모",
                        "성능 벤치마크 (SQLite-vec vs Legacy)",
                        "대용량 데이터 처리 테스트",
                        "실시간 문서 인덱싱 데모",
                        "하이브리드 검색 데모"));

            switch (choice)
            {
                case "기본 벡터 검색 데모":
                    await RunBasicVectorSearchDemo();
                    break;
                case "성능 벤치마크 (SQLite-vec vs Legacy)":
                    await RunPerformanceBenchmark();
                    break;
                case "대용량 데이터 처리 테스트":
                    await RunLargeDatasetTest();
                    break;
                case "실시간 문서 인덱싱 데모":
                    await RunRealtimeIndexingDemo();
                    break;
                case "하이브리드 검색 데모":
                    await RunHybridSearchDemo();
                    break;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
        }

        AnsiConsole.MarkupLine("\n[grey]아무 키나 누르면 종료됩니다...[/]");
        Console.ReadKey();
    }

    /// <summary>
    /// 기본 벡터 검색 데모
    /// </summary>
    static async Task RunBasicVectorSearchDemo()
    {
        AnsiConsole.MarkupLine("[yellow]🔍 기본 벡터 검색 데모[/]");

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var initTask = ctx.AddTask("[green]FluxIndex 초기화[/]");
                var indexTask = ctx.AddTask("[blue]문서 인덱싱[/]");
                var searchTask = ctx.AddTask("[cyan]벡터 검색 실행[/]");

                // 1. FluxIndex 클라이언트 초기화 (SQLite-vec 사용)
                var clientBuilder = new FluxIndexClientBuilder()
                    .UseInMemoryVectorStore() // 데모용 인메모리 사용
                    .UseMockEmbeddingService(); // 모의 임베딩 서비스

                using var client = await clientBuilder.BuildAsync();
                initTask.Value = 100;

                // 2. 샘플 문서들 인덱싱
                var sampleDocs = new[]
                {
                    new { Title = "Python Programming", Content = "Python is a high-level programming language known for its simplicity and readability. It supports multiple programming paradigms." },
                    new { Title = "Machine Learning", Content = "Machine learning is a subset of artificial intelligence that enables computers to learn and improve from experience without being explicitly programmed." },
                    new { Title = "Data Science", Content = "Data science combines statistical analysis, machine learning, and domain expertise to extract insights from structured and unstructured data." },
                    new { Title = "Web Development", Content = "Web development involves creating and maintaining websites using technologies like HTML, CSS, JavaScript, and various frameworks." },
                    new { Title = "Database Systems", Content = "Database systems are organized collections of data that can be easily accessed, managed, and updated using specialized software." }
                };

                foreach (var (doc, index) in sampleDocs.Select((doc, i) => (doc, i)))
                {
                    await client.Indexer.IndexTextAsync(doc.Content, $"doc_{index}", new Dictionary<string, object>
                    {
                        ["title"] = doc.Title,
                        ["category"] = "technology"
                    });

                    indexTask.Value = (index + 1) * 100.0 / sampleDocs.Length;
                }

                // 3. 벡터 검색 실행
                var queries = new[]
                {
                    "programming languages and coding",
                    "artificial intelligence and ML",
                    "websites and frontend development",
                    "data storage and retrieval"
                };

                foreach (var (query, index) in queries.Select((q, i) => (q, i)))
                {
                    var results = await client.Retriever.SearchAsync(query, topK: 3);

                    AnsiConsole.MarkupLine($"\n[bold]쿼리:[/] [italic]{query}[/]");

                    var table = new Table()
                        .AddColumn("순위")
                        .AddColumn("제목")
                        .AddColumn("내용 (일부)")
                        .AddColumn("유사도");

                    foreach (var (result, rank) in results.Select((r, i) => (r, i + 1)))
                    {
                        var title = result.Metadata.TryGetValue("title", out var titleObj) ? titleObj.ToString() : "Unknown";
                        var preview = result.Content.Length > 50 ? result.Content[..47] + "..." : result.Content;

                        table.AddRow(
                            rank.ToString(),
                            title ?? "Unknown",
                            preview,
                            "N/A"); // 실제로는 유사도 점수가 있을 것
                    }

                    AnsiConsole.Write(table);
                    searchTask.Value = (index + 1) * 100.0 / queries.Length;
                }
            });

        AnsiConsole.MarkupLine("\n[green]✅ 기본 벡터 검색 데모 완료[/]");
    }

    /// <summary>
    /// 성능 벤치마크 (SQLite-vec vs Legacy)
    /// </summary>
    static async Task RunPerformanceBenchmark()
    {
        AnsiConsole.MarkupLine("[yellow]⚡ 성능 벤치마크 데모[/]");

        var datasetSizes = new[] { 100, 500, 1000, 2000 };
        var results = new List<BenchmarkResult>();

        foreach (var size in datasetSizes)
        {
            AnsiConsole.MarkupLine($"\n[blue]데이터셋 크기: {size}개 문서[/]");

            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var setupTask = ctx.AddTask("[green]환경 설정[/]");
                    var legacyTask = ctx.AddTask("[red]Legacy 테스트[/]");
                    var sqliteVecTask = ctx.AddTask("[blue]SQLite-vec 테스트[/]");

                    // 환경 설정
                    var testData = GenerateTestData(size);
                    setupTask.Value = 100;

                    // Legacy SQLite 테스트
                    var legacyTime = await BenchmarkVectorStore("Legacy", testData, useSQLiteVec: false);
                    legacyTask.Value = 100;

                    // SQLite-vec 테스트 (폴백 모드)
                    var sqliteVecTime = await BenchmarkVectorStore("SQLite-vec", testData, useSQLiteVec: true);
                    sqliteVecTask.Value = 100;

                    results.Add(new BenchmarkResult
                    {
                        DatasetSize = size,
                        LegacyTime = legacyTime,
                        SQLiteVecTime = sqliteVecTime
                    });
                });
        }

        // 결과 표시
        var table = new Table()
            .AddColumn("데이터셋")
            .AddColumn("Legacy (ms)")
            .AddColumn("SQLite-vec (ms)")
            .AddColumn("개선율 (%)")
            .AddColumn("상태");

        foreach (var result in results)
        {
            var improvement = (result.LegacyTime - result.SQLiteVecTime) / (double)result.LegacyTime * 100;
            var status = improvement > 0 ? "[green]개선[/]" : improvement < -10 ? "[red]저하[/]" : "[yellow]유사[/]";

            table.AddRow(
                $"{result.DatasetSize}개",
                $"{result.LegacyTime}",
                $"{result.SQLiteVecTime}",
                $"{improvement:F1}%",
                status);
        }

        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine("\n[grey]💡 현재는 sqlite-vec 확장이 없어 폴백 모드로 실행됩니다.[/]");
        AnsiConsole.MarkupLine("[grey]   실제 확장이 설치되면 더 큰 성능 향상을 기대할 수 있습니다.[/]");
    }

    /// <summary>
    /// 대용량 데이터 처리 테스트
    /// </summary>
    static async Task RunLargeDatasetTest()
    {
        AnsiConsole.MarkupLine("[yellow]📊 대용량 데이터 처리 테스트[/]");

        const int largeDatasetSize = 10000;
        const int searchQueries = 100;

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var dataGenTask = ctx.AddTask("[green]테스트 데이터 생성[/]");
                var indexingTask = ctx.AddTask("[blue]대용량 인덱싱[/]");
                var searchTask = ctx.AddTask("[cyan]연속 검색 테스트[/]");
                var memoryTask = ctx.AddTask("[orange1]메모리 사용량 모니터링[/]");

                // 테스트 데이터 생성
                var testData = GenerateTestData(largeDatasetSize);
                dataGenTask.Value = 100;

                // 메모리 사용량 모니터링 시작
                var initialMemory = GC.GetTotalMemory(true);

                // FluxIndex 클라이언트 생성
                var clientBuilder = new FluxIndexClientBuilder()
                    .UseInMemoryVectorStore()
                    .UseMockEmbeddingService();

                using var client = await clientBuilder.BuildAsync();

                // 대용량 인덱싱
                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < testData.Count; i++)
                {
                    var doc = testData[i];
                    await client.Indexer.IndexTextAsync(doc.Content, doc.Id, doc.Metadata);

                    indexingTask.Value = (i + 1) * 100.0 / testData.Count;

                    // 주기적으로 메모리 체크
                    if (i % 1000 == 0)
                    {
                        var currentMemory = GC.GetTotalMemory(false);
                        var memoryUsageMB = (currentMemory - initialMemory) / 1024.0 / 1024.0;
                        memoryTask.Value = Math.Min(100, (i * 100.0 / testData.Count));
                    }
                }

                var indexingTime = stopwatch.ElapsedMilliseconds;

                // 연속 검색 테스트
                var searchTimes = new List<long>();
                var random = new Random();

                for (int i = 0; i < searchQueries; i++)
                {
                    var queryTerms = new[] { "technology", "science", "business", "education", "health" };
                    var query = queryTerms[random.Next(queryTerms.Length)] + " " + random.Next(1000, 9999);

                    stopwatch.Restart();
                    var results = await client.Retriever.SearchAsync(query, topK: 10);
                    stopwatch.Stop();

                    searchTimes.Add(stopwatch.ElapsedMilliseconds);
                    searchTask.Value = (i + 1) * 100.0 / searchQueries;
                }

                var finalMemory = GC.GetTotalMemory(true);
                memoryTask.Value = 100;

                // 결과 출력
                var avgSearchTime = searchTimes.Average();
                var p95SearchTime = searchTimes.OrderBy(t => t).Skip((int)(searchQueries * 0.95)).First();
                var memoryUsageMB = (finalMemory - initialMemory) / 1024.0 / 1024.0;
                var throughput = largeDatasetSize / (indexingTime / 1000.0);

                var resultsTable = new Table()
                    .AddColumn("메트릭")
                    .AddColumn("값")
                    .AddColumn("단위");

                resultsTable.AddRow("데이터셋 크기", largeDatasetSize.ToString("N0"), "개");
                resultsTable.AddRow("인덱싱 시간", indexingTime.ToString("N0"), "ms");
                resultsTable.AddRow("인덱싱 처리량", throughput.ToString("F1"), "docs/sec");
                resultsTable.AddRow("평균 검색 시간", avgSearchTime.ToString("F2"), "ms");
                resultsTable.AddRow("95% 검색 시간", p95SearchTime.ToString(), "ms");
                resultsTable.AddRow("메모리 사용량", memoryUsageMB.ToString("F1"), "MB");

                AnsiConsole.Write(resultsTable);
            });

        AnsiConsole.MarkupLine("\n[green]✅ 대용량 데이터 처리 테스트 완료[/]");
    }

    /// <summary>
    /// 실시간 문서 인덱싱 데모
    /// </summary>
    static async Task RunRealtimeIndexingDemo()
    {
        AnsiConsole.MarkupLine("[yellow]⚡ 실시간 문서 인덱싱 데모[/]");

        var clientBuilder = new FluxIndexClientBuilder()
            .UseInMemoryVectorStore()
            .UseMockEmbeddingService();

        using var client = await clientBuilder.BuildAsync();

        var documentCount = 0;

        await AnsiConsole.Live(new Panel("준비 중..."))
            .StartAsync(async ctx =>
            {
                var random = new Random();
                var categories = new[] { "Technology", "Science", "Business", "Health", "Education" };

                for (int i = 0; i < 50; i++)
                {
                    // 실시간으로 문서 생성 및 인덱싱
                    var category = categories[random.Next(categories.Length)];
                    var content = $"This is a {category.ToLower()} document about {random.Next(1000, 9999)} " +
                                 $"containing information about various topics and concepts. " +
                                 $"Generated at {DateTime.Now:HH:mm:ss}.";

                    var stopwatch = Stopwatch.StartNew();
                    await client.Indexer.IndexTextAsync(content, $"realtime_doc_{i}", new Dictionary<string, object>
                    {
                        ["category"] = category,
                        ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        ["batch"] = i / 10
                    });
                    stopwatch.Stop();

                    documentCount++;

                    // UI 업데이트
                    var status = new Panel(
                        new Markup($"""
                        [bold green]실시간 인덱싱 진행 중...[/]

                        [blue]현재 문서:[/] {i + 1}/50
                        [cyan]총 인덱싱된 문서:[/] {documentCount}개
                        [yellow]마지막 인덱싱 시간:[/] {stopwatch.ElapsedMilliseconds}ms
                        [magenta]카테고리:[/] {category}

                        [grey]내용 미리보기:[/]
                        {content[..Math.Min(60, content.Length)]}...
                        """))
                        .Header($"📄 문서 #{i + 1}")
                        .BorderColor(Color.Green);

                    ctx.UpdateTarget(status);

                    // 실시간 효과를 위한 딜레이
                    await Task.Delay(200);
                }

                // 최종 검색 테스트
                var finalStatus = new Panel(
                    new Markup($"""
                    [bold green]✅ 인덱싱 완료![/]

                    [cyan]총 문서 수:[/] {documentCount}개
                    [yellow]검색 테스트 시작...[/]
                    """))
                    .Header("🎉 완료")
                    .BorderColor(Color.Green);

                ctx.UpdateTarget(finalStatus);

                await Task.Delay(1000);

                // 검색 테스트
                var searchQueries = new[] { "technology", "science", "business" };
                var searchResults = new List<string>();

                foreach (var query in searchQueries)
                {
                    var results = await client.Retriever.SearchAsync(query, topK: 3);
                    searchResults.Add($"'{query}': {results.Count()}개 결과");
                }

                var searchStatus = new Panel(
                    new Markup($"""
                    [bold green]🔍 검색 테스트 완료![/]

                    [cyan]총 문서 수:[/] {documentCount}개
                    [yellow]검색 결과:[/]
                    {string.Join("\n", searchResults.Select(r => $"  • {r}"))}
                    """))
                    .Header("🎯 검색 완료")
                    .BorderColor(Color.Cyan);

                ctx.UpdateTarget(searchStatus);
            });

        AnsiConsole.MarkupLine("\n[green]✅ 실시간 인덱싱 데모 완료[/]");
    }

    /// <summary>
    /// 하이브리드 검색 데모 (벡터 + 키워드)
    /// </summary>
    static async Task RunHybridSearchDemo()
    {
        AnsiConsole.MarkupLine("[yellow]🔍 하이브리드 검색 데모[/]");
        AnsiConsole.MarkupLine("[grey]벡터 유사성과 키워드 매칭을 결합한 검색 방식[/]");

        var clientBuilder = new FluxIndexClientBuilder()
            .UseInMemoryVectorStore()
            .UseMockEmbeddingService();

        using var client = await clientBuilder.BuildAsync();

        // 다양한 도메인의 문서들 인덱싱
        var documents = new[]
        {
            new { Title = "Python Web Development", Content = "Flask and Django are popular Python web frameworks for building scalable web applications with robust features." },
            new { Title = "Machine Learning with Python", Content = "Python offers excellent libraries like scikit-learn, TensorFlow, and PyTorch for machine learning and deep learning projects." },
            new { Title = "JavaScript Frontend Development", Content = "React, Vue, and Angular are modern JavaScript frameworks that enable developers to build interactive user interfaces." },
            new { Title = "Database Design Principles", Content = "Proper database design involves normalization, indexing, and query optimization to ensure data integrity and performance." },
            new { Title = "Cloud Computing with AWS", Content = "Amazon Web Services provides scalable cloud infrastructure including EC2, S3, and Lambda for modern applications." },
            new { Title = "Data Science Analytics", Content = "Data science combines statistics, programming, and domain knowledge to extract insights from large datasets using Python or R." }
        };

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var indexTask = ctx.AddTask("[green]문서 인덱싱[/]");

                for (int i = 0; i < documents.Length; i++)
                {
                    var doc = documents[i];
                    await client.Indexer.IndexTextAsync(doc.Content, $"hybrid_doc_{i}", new Dictionary<string, object>
                    {
                        ["title"] = doc.Title,
                        ["domain"] = doc.Title.Split(' ')[^1].ToLower(), // 마지막 단어를 도메인으로
                        ["word_count"] = doc.Content.Split(' ').Length
                    });

                    indexTask.Value = (i + 1) * 100.0 / documents.Length;
                }
            });

        // 하이브리드 검색 시나리오들
        var searchScenarios = new[]
        {
            new { Query = "Python programming", Description = "Python 관련 문서 찾기" },
            new { Query = "web development frameworks", Description = "웹 개발 프레임워크 문서" },
            new { Query = "machine learning data science", Description = "ML/데이터사이언스 관련" },
            new { Query = "cloud infrastructure scalability", Description = "클라우드 인프라 관련" }
        };

        AnsiConsole.MarkupLine("\n[bold]🔍 하이브리드 검색 결과:[/]");

        foreach (var scenario in searchScenarios)
        {
            AnsiConsole.MarkupLine($"\n[cyan]쿼리:[/] [italic]'{scenario.Query}'[/] - {scenario.Description}");

            var results = await client.Retriever.SearchAsync(scenario.Query, topK: 3);

            if (results.Any())
            {
                var table = new Table()
                    .AddColumn("순위")
                    .AddColumn("제목")
                    .AddColumn("도메인")
                    .AddColumn("매칭 키워드");

                foreach (var (result, rank) in results.Select((r, i) => (r, i + 1)))
                {
                    var title = result.Metadata.TryGetValue("title", out var titleObj) ? titleObj.ToString() : "Unknown";
                    var domain = result.Metadata.TryGetValue("domain", out var domainObj) ? domainObj.ToString() : "Unknown";

                    // 간단한 키워드 매칭 시뮬레이션
                    var queryWords = scenario.Query.ToLower().Split(' ');
                    var contentWords = result.Content.ToLower().Split(' ');
                    var matchingKeywords = queryWords.Intersect(contentWords).Take(3);

                    table.AddRow(
                        rank.ToString(),
                        title ?? "Unknown",
                        domain ?? "Unknown",
                        string.Join(", ", matchingKeywords));
                }

                AnsiConsole.Write(table);
            }
            else
            {
                AnsiConsole.MarkupLine("[red]검색 결과가 없습니다.[/]");
            }
        }

        AnsiConsole.MarkupLine("\n[green]✅ 하이브리드 검색 데모 완료[/]");
    }

    /// <summary>
    /// 벡터 저장소 성능 벤치마크
    /// </summary>
    static async Task<long> BenchmarkVectorStore(string name, List<TestDocument> testData, bool useSQLiteVec)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        if (useSQLiteVec)
        {
            services.AddSQLiteVecInMemoryVectorStore();
        }
        else
        {
            services.AddSQLiteInMemoryVectorStore();
        }

        using var provider = services.BuildServiceProvider();

        // 호스팅 서비스 시작
        var hostedServices = provider.GetServices<IHostedService>();
        foreach (var service in hostedServices)
        {
            await service.StartAsync(CancellationToken.None);
        }

        var vectorStore = provider.GetRequiredService<FluxIndex.Core.Application.Interfaces.IVectorStore>();

        var stopwatch = Stopwatch.StartNew();

        // 인덱싱
        var chunks = testData.Select((doc, i) => new FluxIndex.Domain.Entities.DocumentChunk
        {
            DocumentId = doc.Id,
            ChunkIndex = 0,
            Content = doc.Content,
            Embedding = GenerateRandomEmbedding(),
            TokenCount = doc.Content.Split(' ').Length,
            Metadata = doc.Metadata
        }).ToList();

        await vectorStore.StoreBatchAsync(chunks);

        // 검색 테스트
        for (int i = 0; i < 10; i++)
        {
            var queryEmbedding = GenerateRandomEmbedding();
            await vectorStore.SearchAsync(queryEmbedding, topK: 5);
        }

        stopwatch.Stop();

        // 호스팅 서비스 정지
        foreach (var service in hostedServices.Reverse())
        {
            await service.StopAsync(CancellationToken.None);
        }

        return stopwatch.ElapsedMilliseconds;
    }

    /// <summary>
    /// 테스트 데이터 생성
    /// </summary>
    static List<TestDocument> GenerateTestData(int count)
    {
        var random = new Random(42);
        var categories = new[] { "technology", "science", "business", "education", "health" };
        var documents = new List<TestDocument>();

        for (int i = 0; i < count; i++)
        {
            var category = categories[i % categories.Length];
            var content = $"This is a test document about {category} with id {i}. " +
                         $"It contains various information and random data {random.Next(1000, 9999)}. " +
                         $"Generated for performance testing and benchmarking purposes.";

            documents.Add(new TestDocument
            {
                Id = $"test_doc_{i}",
                Content = content,
                Metadata = new Dictionary<string, object>
                {
                    ["category"] = category,
                    ["index"] = i,
                    ["timestamp"] = DateTimeOffset.UtcNow.AddMinutes(-i).ToUnixTimeSeconds()
                }
            });
        }

        return documents;
    }

    /// <summary>
    /// 랜덤 임베딩 생성 (테스트용)
    /// </summary>
    static float[] GenerateRandomEmbedding(int dimension = 384)
    {
        var random = new Random();
        var embedding = new float[dimension];

        for (int i = 0; i < dimension; i++)
        {
            embedding[i] = (float)(random.NextDouble() - 0.5) * 2;
        }

        // 정규화
        var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < dimension; i++)
            {
                embedding[i] /= magnitude;
            }
        }

        return embedding;
    }
}

/// <summary>
/// 테스트 문서 클래스
/// </summary>
public class TestDocument
{
    public string Id { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 벤치마크 결과 클래스
/// </summary>
public class BenchmarkResult
{
    public int DatasetSize { get; set; }
    public long LegacyTime { get; set; }
    public long SQLiteVecTime { get; set; }
}