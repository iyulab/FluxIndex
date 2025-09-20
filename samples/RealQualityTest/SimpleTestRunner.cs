using System;
using System.IO;
using System.Threading.Tasks;
using Spectre.Console;

namespace RealQualityTest;

public class SimpleTestRunner
{
    public static async Task Main(string[] args)
    {
        AnsiConsole.Write(new FigletText("FluxIndex Hybrid Search Test").Centered().Color(Color.Blue1));
        AnsiConsole.WriteLine();

        // .env.local 파일 로드
        var apiKey = LoadApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            AnsiConsole.MarkupLine("[red]OpenAI API 키를 찾을 수 없습니다. .env.local 파일을 확인하세요.[/]");
            AnsiConsole.MarkupLine("[yellow]예시: OPENAI_API_KEY=sk-...[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[green]API 키 로드 완료: {apiKey.Substring(0, 8)}...[/]");
        AnsiConsole.WriteLine();

        try
        {
            using var test = new StandaloneHybridTest(apiKey);
            await test.RunQualityTestAsync();
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to exit...[/]");
        Console.ReadKey();
    }

    private static string LoadApiKey()
    {
        var envFile = Path.Combine(Directory.GetCurrentDirectory(), ".env.local");

        if (!File.Exists(envFile))
        {
            // 상위 디렉토리에서도 찾기
            envFile = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env.local");
            if (!File.Exists(envFile))
            {
                return Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
            }
        }

        try
        {
            var lines = File.ReadAllLines(envFile);
            foreach (var line in lines)
            {
                if (line.StartsWith("OPENAI_API_KEY="))
                {
                    return line.Substring("OPENAI_API_KEY=".Length).Trim();
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red].env.local 파일 읽기 오류: {ex.Message}[/]");
        }

        return "";
    }
}