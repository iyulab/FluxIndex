using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RealQualityTest;
using Spectre.Console;

namespace RealQualityTest;

class Program
{
    static async Task Main(string[] args)
    {
        // .env.local 파일 로드
        LoadEnvironmentFile();

        // 설정 빌더
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // 서비스 컨테이너 설정
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 테스트 서비스 등록
        services.AddTransient<SimpleQualityTest>();
        services.AddTransient<HybridSearchQualityTest>();
        services.AddTransient<SimpleHybridTest>();

        var serviceProvider = services.BuildServiceProvider();

        // 앱 실행
        try
        {
            AnsiConsole.Write(new FigletText("FluxIndex Quality Test").Centered().Color(Color.Cyan1));
            AnsiConsole.WriteLine();

            // API 키 확인
            var apiKey = configuration["OPENAI_API_KEY"];
            if (string.IsNullOrEmpty(apiKey))
            {
                AnsiConsole.MarkupLine("[red]OPENAI_API_KEY가 설정되지 않았습니다. .env.local 파일을 확인하세요.[/]");
                return;
            }

            // 테스트 메뉴 선택
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("실행할 테스트를 선택하세요:")
                    .AddChoices(new[] {
                        "기존 품질 테스트",
                        "하이브리드 검색 테스트",
                        "간단한 하이브리드 테스트",
                        "모든 테스트 실행"
                    }));

            switch (choice)
            {
                case "기존 품질 테스트":
                    var simpleTest = serviceProvider.GetRequiredService<SimpleQualityTest>();
                    await simpleTest.RunTestAsync();
                    break;

                case "하이브리드 검색 테스트":
                    var hybridTest = serviceProvider.GetRequiredService<HybridSearchQualityTest>();
                    await hybridTest.RunHybridSearchQualityTestAsync();
                    break;

                case "간단한 하이브리드 테스트":
                    var simpleHybridTest = serviceProvider.GetRequiredService<SimpleHybridTest>();
                    await simpleHybridTest.RunSimpleHybridTestAsync();
                    break;

                case "모든 테스트 실행":
                    var allSimpleTest = serviceProvider.GetRequiredService<SimpleQualityTest>();
                    await allSimpleTest.RunTestAsync();

                    AnsiConsole.WriteLine();
                    AnsiConsole.Rule("[yellow]하이브리드 검색 테스트 시작[/]");
                    AnsiConsole.WriteLine();

                    var allHybridTest = serviceProvider.GetRequiredService<HybridSearchQualityTest>();
                    await allHybridTest.RunHybridSearchQualityTestAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            Environment.Exit(1);
        }
    }

    private static void LoadEnvironmentFile()
    {
        var envFile = Path.Combine(Directory.GetCurrentDirectory(), ".env.local");
        if (File.Exists(envFile))
        {
            var lines = File.ReadAllLines(envFile);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
                }
            }
        }
    }
}