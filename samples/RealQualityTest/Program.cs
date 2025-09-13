using Microsoft.Extensions.Configuration;
using Spectre.Console;

namespace RealQualityTest;

class Program
{
    static async Task Main(string[] args)
    {
        AnsiConsole.Write(new FigletText("FluxIndex Quality Test").Color(Color.Cyan1));
        
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables()
            .Build();

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? config["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            AnsiConsole.MarkupLine("[red]Please set OPENAI_API_KEY environment variable or update appsettings.json[/]");
            return;
        }

        var tester = new SimpleQualityTest(apiKey, config);
        await tester.RunTestAsync();
    }
}