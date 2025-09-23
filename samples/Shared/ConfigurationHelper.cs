using Microsoft.Extensions.Configuration;

namespace FluxIndex.Samples.Shared;

public static class ConfigurationHelper
{
    /// <summary>
    /// 환경 변수에서 OpenAI 설정을 안전하게 로드합니다.
    /// </summary>
    /// <returns>API 키와 모델 설정이 포함된 튜플</returns>
    public static (string? apiKey, string embeddingModel, string completionModel) LoadOpenAIConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddUserSecrets<object>(optional: true)
            .Build();

        var apiKey = configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var embeddingModel = configuration["OPENAI_EMBEDDING_MODEL"] ??
                           Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_MODEL") ??
                           "text-embedding-3-small";
        var completionModel = configuration["OPENAI_MODEL"] ??
                            Environment.GetEnvironmentVariable("OPENAI_MODEL") ??
                            "gpt-5-nano";

        return (apiKey, embeddingModel, completionModel);
    }

    /// <summary>
    /// API 키 유효성을 검증하고 사용자에게 안내 메시지를 표시합니다.
    /// </summary>
    /// <param name="apiKey">검증할 API 키</param>
    /// <returns>API 키가 유효한지 여부</returns>
    public static bool ValidateApiKey(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.WriteLine("❌ OPENAI_API_KEY 환경 변수가 설정되지 않았습니다.");
            Console.WriteLine("   다음 중 하나의 방법으로 API 키를 설정해주세요:");
            Console.WriteLine("   1. .env.local 파일에 OPENAI_API_KEY=your_key_here 추가");
            Console.WriteLine("   2. 환경 변수로 OPENAI_API_KEY 설정");
            Console.WriteLine("   3. User Secrets 사용 (dotnet user-secrets set \"OPENAI_API_KEY\" \"your_key_here\")");
            Console.WriteLine("   4. appsettings.json에 설정 (권장하지 않음 - 보안상 위험)");
            return false;
        }

        // API 키 형식 기본 검증
        if (!apiKey.StartsWith("sk-"))
        {
            Console.WriteLine("❌ API 키 형식이 올바르지 않습니다. OpenAI API 키는 'sk-'로 시작해야 합니다.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 설정 정보를 안전하게 표시합니다 (API 키는 마스킹).
    /// </summary>
    /// <param name="apiKey">API 키</param>
    /// <param name="embeddingModel">임베딩 모델</param>
    /// <param name="completionModel">완성 모델</param>
    public static void DisplayConfiguration(string apiKey, string embeddingModel, string completionModel)
    {
        var maskedApiKey = apiKey.Length > 10
            ? $"{apiKey[..7]}...{apiKey[^4..]}"
            : "***";

        Console.WriteLine("🔑 OpenAI 설정:");
        Console.WriteLine($"   API Key: {maskedApiKey}");
        Console.WriteLine($"   Embedding Model: {embeddingModel}");
        Console.WriteLine($"   Completion Model: {completionModel}");
    }
}