using FluxIndex.Core.Application.Interfaces;
using DocumentChunk = FluxIndex.Domain.Entities.DocumentChunk;
using FluxIndex.Storage.SQLite;
using FluxIndex.AI.OpenAI;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Diagnostics;
using System.Text.Json;

namespace FluxIndex.RealWorldDemo;

public class Program
{
    public static async Task Main(string[] args)
    {
        AnsiConsole.Write(
            new FigletText("FluxIndex")
                .Centered()
                .Color(Color.Blue));

        AnsiConsole.MarkupLine("[bold]sqlite-vec를 활용한 실제 API 기반 RAG 데모[/]");
        AnsiConsole.WriteLine();

        var builder = Host.CreateApplicationBuilder(args);

        // 환경 변수 로드
        LoadEnvironmentVariables();

        // 설정 구성
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables();

        // 로깅 설정
        builder.Logging.AddConsole();

        // FluxIndex 서비스 등록
        ConfigureServices(builder.Services, builder.Configuration);

        var host = builder.Build();

        try
        {
            var demo = host.Services.GetRequiredService<FluxIndexDemo>();
            await demo.RunAsync();
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
        }
    }

    private static void LoadEnvironmentVariables()
    {
        var envFile = Path.Combine(Directory.GetCurrentDirectory(), ".env.local");
        Console.WriteLine($"Looking for .env.local at: {envFile}");
        Console.WriteLine($"File exists: {File.Exists(envFile)}");

        if (File.Exists(envFile))
        {
            Console.WriteLine("Loading environment variables from .env.local");
            var lines = File.ReadAllLines(envFile);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    Console.WriteLine($"Setting {parts[0]}=***");
                    Environment.SetEnvironmentVariable(parts[0], parts[1]);
                }
            }
        }
        else
        {
            Console.WriteLine("WARNING: .env.local file not found!");
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // FluxIndex 핵심 서비스
        services.AddLogging();

        // SQLite 벡터 저장소 설정
        var dbPath = configuration["FluxIndex:DatabasePath"] ?? "./data/fluxindex_demo.db";
        var dataDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }

        // sqlite-vec 확장을 사용하는 고성능 SQLite 벡터 저장소 사용
        services.AddSQLiteVecVectorStore(options =>
        {
            options.DatabasePath = dbPath;
            options.VectorDimension = configuration.GetValue<int>("FluxIndex:VectorDimension", 1536);
            options.UseSQLiteVec = configuration.GetValue<bool>("FluxIndex:UseSQLiteVec", true);
            options.FallbackToInMemoryOnError = configuration.GetValue<bool>("FluxIndex:FallbackToInMemoryOnError", false);
        });

        // OpenAI 서비스 설정
        var openAIApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Console.WriteLine($"OpenAI API Key present: {!string.IsNullOrEmpty(openAIApiKey)}");

        if (!string.IsNullOrEmpty(openAIApiKey))
        {
            services.AddOpenAIEmbedding(options =>
            {
                options.ApiKey = openAIApiKey;
                options.ModelName = Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_MODEL") ?? "text-embedding-3-small";
            });
            Console.WriteLine("OpenAI services registered successfully");
        }
        else
        {
            Console.WriteLine("WARNING: OpenAI API key not found - embedding service will not be available");
        }

        // FileFlux 확장 (만약 사용 가능하다면)
        // services.AddFileFlux();

        services.AddSingleton<FluxIndexDemo>();
    }
}

public class FluxIndexDemo
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService? _embeddingService;
    private readonly ILogger<FluxIndexDemo> _logger;
    private readonly IConfiguration _configuration;
    private readonly SQLiteVecDbContext _vecDbContext;

    public FluxIndexDemo(
        IVectorStore vectorStore,
        IEmbeddingService? embeddingService,
        ILogger<FluxIndexDemo> logger,
        IConfiguration configuration,
        SQLiteVecDbContext vecDbContext)
    {
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _logger = logger;
        _configuration = configuration;
        _vecDbContext = vecDbContext;
    }

    public async Task RunAsync()
    {
        var testDataPath = _configuration["Demo:TestDataPath"] ?? "D:/test-filer";
        var maxDocuments = _configuration.GetValue<int>("Demo:MaxDocuments", 100);
        var searchTopK = _configuration.GetValue<int>("Demo:SearchTopK", 10);

        AnsiConsole.MarkupLine($"[cyan]테스트 데이터 경로:[/] {testDataPath}");
        AnsiConsole.MarkupLine($"[cyan]최대 문서 수:[/] {maxDocuments}");
        AnsiConsole.MarkupLine($"[cyan]검색 결과 수:[/] {searchTopK}");
        AnsiConsole.WriteLine();

        // 1. 데이터베이스 초기화
        await InitializeDatabase();

        // 2. sqlite-vec 확장 상태 확인
        await CheckSQLiteVecStatus();

        // 3. 테스트 데이터 인덱싱
        var indexedCount = await IndexTestDocuments(testDataPath, maxDocuments);

        if (indexedCount == 0)
        {
            AnsiConsole.MarkupLine("[red]인덱싱된 문서가 없습니다. 프로그램을 종료합니다.[/]");
            return;
        }

        // 3. 검색 기능 테스트
        await TestSearchFunctionality(searchTopK);

        // 4. 성능 벤치마크
        await PerformanceBenchmark(searchTopK);

        // 5. 대화형 검색 모드
        await InteractiveSearchMode(searchTopK);
    }

    private async Task InitializeDatabase()
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Star)
            .SpinnerStyle(Style.Parse("blue bold"))
            .StartAsync("데이터베이스 초기화 중...", async ctx =>
            {
                try
                {
                    // 데이터베이스 디렉터리 생성
                    var dbPath = _configuration["FluxIndex:DatabasePath"] ?? "./data/fluxindex_demo.db";
                    var dataDir = Path.GetDirectoryName(dbPath);
                    if (!string.IsNullOrEmpty(dataDir))
                    {
                        Directory.CreateDirectory(dataDir);
                    }

                    // SQLiteVec 데이터베이스 초기화 (sqlite-vec 확장 로드 및 vec0 테이블 생성)
                    await _vecDbContext.InitializeAsync();

                    _logger.LogInformation("sqlite-vec 데이터베이스가 성공적으로 초기화되었습니다: {DbPath}", dbPath);
                    AnsiConsole.MarkupLine("[green]✓ sqlite-vec 데이터베이스 초기화 완료![/]");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "데이터베이스 초기화 실패");
                    AnsiConsole.MarkupLine($"[red]✗ 데이터베이스 초기화 실패: {ex.Message}[/]");
                    throw;
                }
            });
    }

    private async Task CheckSQLiteVecStatus()
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Star)
            .SpinnerStyle(Style.Parse("green bold"))
            .StartAsync("sqlite-vec 확장 상태 확인 중...", async ctx =>
            {
                try
                {
                    // 벡터 저장소 연결 테스트
                    var testEmbedding = Enumerable.Range(0, 1536).Select(i => (float)Random.Shared.NextDouble()).ToArray();
                    var testId = await _vectorStore.StoreBatchAsync(new[]
                    {
                        new DocumentChunk
                        {
                            Id = "test_" + Guid.NewGuid().ToString(),
                            DocumentId = "test_doc",
                            ChunkIndex = 0,
                            Content = "테스트 내용",
                            TokenCount = 10,
                            Embedding = testEmbedding,
                            Metadata = new Dictionary<string, object>
                            {
                                ["test"] = true
                            }
                        }
                    });

                    if (testId.Any())
                    {
                        AnsiConsole.MarkupLine("[green]✓ sqlite-vec 확장이 성공적으로 로드되었습니다![/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[yellow]⚠ sqlite-vec 확장 로드 실패 - 폴백 모드로 동작합니다[/]");
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]✗ sqlite-vec 확장 테스트 실패: {ex.Message}[/]");
                }
            });
    }

    private async Task<int> IndexTestDocuments(string testDataPath, int maxDocuments)
    {
        if (_embeddingService == null)
        {
            AnsiConsole.MarkupLine("[red]임베딩 서비스가 구성되지 않았습니다. OpenAI API 키를 확인하세요.[/]");
            return 0;
        }

        var indexedCount = 0;
        var stopwatch = Stopwatch.StartNew();

        // 테스트 파일 수집
        var testFiles = CollectTestFiles(testDataPath, maxDocuments);

        AnsiConsole.MarkupLine($"[cyan]발견된 테스트 파일: {testFiles.Count}개[/]");

        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn()
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]문서 인덱싱[/]", maxValue: testFiles.Count);

                foreach (var file in testFiles)
                {
                    try
                    {
                        task.Description = $"[green]인덱싱: {Path.GetFileName(file)}[/]";

                        var content = await ReadFileContent(file);
                        if (string.IsNullOrWhiteSpace(content) || content.Length < 50)
                        {
                            task.Increment(1);
                            continue;
                        }

                        // 임베딩 생성
                        var embedding = await _embeddingService.GenerateEmbeddingAsync(content);

                        // 문서 청크 생성
                        var chunks = CreateDocumentChunks(file, content, embedding);

                        // 벡터 저장소에 저장
                        var storedIds = await _vectorStore.StoreBatchAsync(chunks);

                        if (storedIds.Any())
                        {
                            indexedCount++;
                            _logger.LogInformation("인덱싱 완료: {FileName}", Path.GetFileName(file));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "파일 인덱싱 실패: {FileName}", Path.GetFileName(file));
                    }

                    task.Increment(1);
                }
            });

        stopwatch.Stop();

        AnsiConsole.MarkupLine($"[green]✓ 인덱싱 완료![/]");
        AnsiConsole.MarkupLine($"[cyan]  - 처리된 파일: {indexedCount}/{testFiles.Count}[/]");
        AnsiConsole.MarkupLine($"[cyan]  - 소요 시간: {stopwatch.Elapsed.TotalSeconds:F2}초[/]");
        AnsiConsole.MarkupLine($"[cyan]  - 처리 속도: {(indexedCount / stopwatch.Elapsed.TotalSeconds):F1} 파일/초[/]");
        AnsiConsole.WriteLine();

        return indexedCount;
    }

    private List<string> CollectTestFiles(string testDataPath, int maxDocuments)
    {
        var files = new List<string>();

        var testDirs = new[]
        {
            Path.Combine(testDataPath, "test-pdf"),
            Path.Combine(testDataPath, "test-docx"),
            Path.Combine(testDataPath, "test-md"),
            Path.Combine(testDataPath, "test-xlsx")
        };

        foreach (var dir in testDirs.Where(Directory.Exists))
        {
            var dirFiles = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => IsValidTestFile(f))
                .Take(maxDocuments / testDirs.Length)
                .ToList();

            files.AddRange(dirFiles);

            if (files.Count >= maxDocuments)
                break;
        }

        return files.Take(maxDocuments).ToList();
    }

    private bool IsValidTestFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var validExtensions = new[] { ".pdf", ".docx", ".md", ".txt", ".xlsx" };

        return validExtensions.Contains(extension) &&
               !Path.GetFileName(filePath).StartsWith('.') &&
               new FileInfo(filePath).Length > 100; // 최소 100바이트
    }

    private async Task<string> ReadFileContent(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".md" or ".txt" => await File.ReadAllTextAsync(filePath),
            ".pdf" => $"PDF 문서: {Path.GetFileName(filePath)}",
            ".docx" => $"DOCX 문서: {Path.GetFileName(filePath)}",
            ".xlsx" => $"Excel 문서: {Path.GetFileName(filePath)}",
            _ => $"문서: {Path.GetFileName(filePath)}"
        };
    }

    private List<DocumentChunk> CreateDocumentChunks(string filePath, string content, float[] embedding)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);

        return new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = $"{fileName}_{Guid.NewGuid()}",
                DocumentId = fileName,
                ChunkIndex = 0,
                Content = content.Length > 2000 ? content[..2000] : content,
                TokenCount = Math.Min(content.Length / 4, 500), // 대략적인 토큰 수
                Embedding = embedding,
                Metadata = new Dictionary<string, object>
                {
                    ["fileName"] = fileName,
                    ["extension"] = extension,
                    ["filePath"] = filePath,
                    ["fileSize"] = new FileInfo(filePath).Length,
                    ["indexedAt"] = DateTime.UtcNow
                }
            }
        };
    }

    private async Task TestSearchFunctionality(int topK)
    {
        if (_embeddingService == null) return;

        var rule1 = new Rule("[bold blue]검색 기능 테스트[/]");
        AnsiConsole.Write(rule1);

        var searchQueries = new[]
        {
            "machine learning",
            "artificial intelligence",
            "data analysis",
            "programming",
            "technology"
        };

        foreach (var query in searchQueries)
        {
            try
            {
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
                var searchResults = await _vectorStore.SearchAsync(queryEmbedding, topK);

                AnsiConsole.MarkupLine($"[cyan]검색어:[/] '{query}'");
                AnsiConsole.MarkupLine($"[cyan]결과 수:[/] {searchResults.Count()}");

                if (searchResults.Any())
                {
                    var table = new Table();
                    table.AddColumn("문서명");
                    table.AddColumn("점수");
                    table.AddColumn("내용 미리보기");

                    foreach (var result in searchResults.Take(3))
                    {
                        var fileName = result.Metadata?.TryGetValue("fileName", out var name) == true ? name?.ToString() : "Unknown";
                        var preview = result.Content.Length > 50 ? result.Content[..50] + "..." : result.Content;
                        table.AddRow(fileName ?? "Unknown", result.Score?.ToString("F3") ?? "0.000", preview);
                    }

                    AnsiConsole.Write(table);
                }

                AnsiConsole.WriteLine();
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]검색 실패 '{query}': {ex.Message}[/]");
            }
        }
    }

    private async Task PerformanceBenchmark(int topK)
    {
        if (_embeddingService == null) return;

        var rule2 = new Rule("[bold blue]성능 벤치마크[/]");
        AnsiConsole.Write(rule2);

        var benchmarkQueries = Enumerable.Range(0, 20)
            .Select(i => $"benchmark query {i}")
            .ToArray();

        var searchTimes = new List<double>();
        var stopwatch = new Stopwatch();

        foreach (var query in benchmarkQueries)
        {
            try
            {
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);

                stopwatch.Restart();
                var results = await _vectorStore.SearchAsync(queryEmbedding, topK);
                stopwatch.Stop();

                searchTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "벤치마크 검색 실패: {Query}", query);
            }
        }

        if (searchTimes.Any())
        {
            var avgTime = searchTimes.Average();
            var minTime = searchTimes.Min();
            var maxTime = searchTimes.Max();

            var table = new Table();
            table.AddColumn("메트릭");
            table.AddColumn("값");

            table.AddRow("평균 검색 시간", $"{avgTime:F2}ms");
            table.AddRow("최소 검색 시간", $"{minTime:F2}ms");
            table.AddRow("최대 검색 시간", $"{maxTime:F2}ms");
            table.AddRow("초당 검색 수", $"{1000 / avgTime:F1}");

            AnsiConsole.Write(table);
        }

        AnsiConsole.WriteLine();
    }

    private async Task InteractiveSearchMode(int topK)
    {
        if (_embeddingService == null) return;

        var rule3 = new Rule("[bold blue]대화형 검색 모드[/]");
        AnsiConsole.Write(rule3);
        AnsiConsole.MarkupLine("[yellow]검색어를 입력하세요 (종료하려면 'quit' 입력):[/]");

        while (true)
        {
            var query = AnsiConsole.Ask<string>("[green]검색어:[/]");

            if (string.Equals(query, "quit", StringComparison.OrdinalIgnoreCase))
                break;

            if (string.IsNullOrWhiteSpace(query))
                continue;

            try
            {
                var stopwatch = Stopwatch.StartNew();
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
                var embeddingTime = stopwatch.ElapsedMilliseconds;

                stopwatch.Restart();
                var results = await _vectorStore.SearchAsync(queryEmbedding, topK);
                var searchTime = stopwatch.ElapsedMilliseconds;

                AnsiConsole.MarkupLine($"[cyan]임베딩 시간:[/] {embeddingTime}ms");
                AnsiConsole.MarkupLine($"[cyan]검색 시간:[/] {searchTime}ms");
                AnsiConsole.MarkupLine($"[cyan]결과 수:[/] {results.Count()}");

                if (results.Any())
                {
                    var table = new Table();
                    table.AddColumn("순위");
                    table.AddColumn("문서명");
                    table.AddColumn("점수");
                    table.AddColumn("내용");

                    var resultList = results.ToList();
                    for (int i = 0; i < Math.Min(resultList.Count, 5); i++)
                    {
                        var result = resultList[i];
                        var fileName = result.Metadata?.TryGetValue("fileName", out var name) == true ? name?.ToString() : "Unknown";
                        var content = result.Content.Length > 100 ? result.Content[..100] + "..." : result.Content;

                        table.AddRow(
                            (i + 1).ToString(),
                            fileName ?? "Unknown",
                            result.Score?.ToString("F3") ?? "0.000",
                            content);
                    }

                    AnsiConsole.Write(table);
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]검색 결과가 없습니다.[/]");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]검색 오류: {ex.Message}[/]");
            }

            AnsiConsole.WriteLine();
        }
    }
}
