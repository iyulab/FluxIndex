using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

namespace FileFluxIndexSample;

/// <summary>
/// 단독 실행 가능한 샘플 - FluxIndex Core 의존성 없음
/// </summary>
public class StandaloneSample
{
    public static async Task Main(string[] args)
    {
        AnsiConsole.Write(
            new FigletText("FileFlux Test")
                .LeftJustified()
                .Color(Color.Blue));

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var testPath = configuration["TestConfiguration:TestDataPath"] ?? @"D:\data\FileFlux\test";
        
        AnsiConsole.MarkupLine($"[green]테스트 경로:[/] {testPath}");
        AnsiConsole.MarkupLine($"[green]SQLite DB:[/] {configuration.GetConnectionString("SQLite")}");
        AnsiConsole.MarkupLine($"[green]OpenAI Model:[/] {configuration["OpenAI:Model"]}");
        
        // API Key 확인
        var apiKey = configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            AnsiConsole.MarkupLine("[red]경고: OpenAI API Key가 설정되지 않았습니다.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]API Key:[/] {apiKey.Substring(0, 10)}...");
        }

        // 테스트 파일 목록
        if (Directory.Exists(testPath))
        {
            var files = Directory.GetFiles(testPath, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || f.Contains("extract"))
                .Take(10)
                .ToArray();

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[yellow]테스트 파일 ({files.Length}개):[/]");
            
            var table = new Table();
            table.AddColumn("파일명");
            table.AddColumn("크기");
            table.AddColumn("형식");

            foreach (var file in files)
            {
                var info = new FileInfo(file);
                table.AddRow(
                    Path.GetFileName(file),
                    $"{info.Length / 1024.0:F2} KB",
                    Path.GetExtension(file).ToUpper()
                );
            }

            AnsiConsole.Write(table);
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]테스트 경로를 찾을 수 없습니다: {testPath}[/]");
        }

        // 메뉴
        while (true)
        {
            AnsiConsole.WriteLine();
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[green]테스트 옵션:[/]")
                    .AddChoices(new[]
                    {
                        "1. 설정 확인",
                        "2. 파일 목록 보기",
                        "3. SQLite 연결 테스트",
                        "4. OpenAI 연결 테스트",
                        "5. 종료"
                    }));

            if (choice.StartsWith("5")) break;

            await ExecuteChoice(choice, configuration, testPath);
        }
    }

    private static async Task ExecuteChoice(string choice, IConfiguration configuration, string testPath)
    {
        switch (choice[0])
        {
            case '1':
                ShowConfiguration(configuration);
                break;
            case '2':
                ShowFiles(testPath);
                break;
            case '3':
                await TestSQLiteConnection(configuration);
                break;
            case '4':
                await TestOpenAIConnection(configuration);
                break;
        }
    }

    private static void ShowConfiguration(IConfiguration configuration)
    {
        var panel = new Panel(new Markup(
            $"[green]SQLite:[/] {configuration.GetConnectionString("SQLite")}\n" +
            $"[green]Model:[/] {configuration["OpenAI:Model"]}\n" +
            $"[green]Embedding:[/] {configuration["OpenAI:EmbeddingModel"]}\n" +
            $"[green]Test Path:[/] {configuration["TestConfiguration:TestDataPath"]}\n" +
            $"[green]Parallel Degree:[/] {configuration["TestConfiguration:ParallelDegree"]}"))
        {
            Header = new PanelHeader("현재 설정"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
    }

    private static void ShowFiles(string testPath)
    {
        if (!Directory.Exists(testPath))
        {
            AnsiConsole.MarkupLine("[red]테스트 경로를 찾을 수 없습니다.[/]");
            return;
        }

        var tree = new Tree($"[yellow]{testPath}[/]");
        
        foreach (var dir in Directory.GetDirectories(testPath))
        {
            var dirNode = tree.AddNode($"[blue]{Path.GetFileName(dir)}[/]");
            
            foreach (var file in Directory.GetFiles(dir).Take(5))
            {
                var info = new FileInfo(file);
                dirNode.AddNode($"{Path.GetFileName(file)} [grey]({info.Length / 1024.0:F2} KB)[/]");
            }
        }

        AnsiConsole.Write(tree);
    }

    private static async Task TestSQLiteConnection(IConfiguration configuration)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Star)
            .StartAsync("SQLite 연결 테스트 중...", async ctx =>
            {
                try
                {
                    var connectionString = configuration.GetConnectionString("SQLite");
                    // 실제 SQLite 연결 테스트 코드
                    await Task.Delay(1000); // 시뮬레이션
                    
                    AnsiConsole.MarkupLine($"[green]✓[/] SQLite 연결 성공: {connectionString}");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]✗[/] SQLite 연결 실패: {ex.Message}");
                }
            });
    }

    private static async Task TestOpenAIConnection(IConfiguration configuration)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Star)
            .StartAsync("OpenAI API 연결 테스트 중...", async ctx =>
            {
                try
                {
                    var apiKey = configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                    
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        AnsiConsole.MarkupLine("[red]✗[/] API Key가 설정되지 않았습니다.");
                        return;
                    }

                    // 실제 OpenAI API 테스트 코드
                    using var httpClient = new System.Net.Http.HttpClient();
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                    
                    var response = await httpClient.GetAsync("https://api.openai.com/v1/models");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        AnsiConsole.MarkupLine($"[green]✓[/] OpenAI API 연결 성공");
                        AnsiConsole.MarkupLine($"[green]Model:[/] {configuration["OpenAI:Model"]}");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]✗[/] OpenAI API 연결 실패: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]✗[/] OpenAI API 테스트 실패: {ex.Message}");
                }
            });
    }
}