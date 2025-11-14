using DotNetEnv;
using FluxIndex.AI.OpenAI;
using FluxIndex.AI.OpenAI.Services;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Interfaces;
using FluxIndex.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace FluxIndex.Tests.AI.OpenAI;

/// <summary>
/// .env.local 파일 존재 여부에 따라 자동으로 Mock/실제 API를 전환하는 테스트 Fixture
/// - .env.local 있음: 실제 OpenAI API 사용
/// - .env.local 없음: Mock ITextCompletionService 사용
/// </summary>
public class OpenAITestFixture : IDisposable
{
    private const string EnvFilePath = "D:\\data\\FluxIndex\\.env.local";

    public bool UseRealApi { get; }
    public Mock<ITextCompletionService>? MockCompletionService { get; }
    public Mock<IRuleBasedMetadataExtractor> MockRuleBasedExtractor { get; }
    public Mock<ILogger<OpenAIMetadataExtractor>> MockLogger { get; }
    public IMemoryCache Cache { get; }
    public OpenAIMetadataExtractor Extractor { get; }

    public OpenAITestFixture()
    {
        // .env.local 파일 존재 여부 확인
        UseRealApi = File.Exists(EnvFilePath);

        if (UseRealApi)
        {
            // 실제 API 모드: .env.local 로드
            Env.Load(EnvFilePath);
        }

        // 공통 Mock 설정
        MockRuleBasedExtractor = new Mock<IRuleBasedMetadataExtractor>();
        MockLogger = new Mock<ILogger<OpenAIMetadataExtractor>>();
        Cache = new MemoryCache(new MemoryCacheOptions());

        // RuleBasedExtractor 기본 동작 설정
        MockRuleBasedExtractor
            .Setup(x => x.ExtractAsync(It.IsAny<string>(), It.IsAny<MetadataSchema>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExtractedMetadata
            {
                Keywords = new[] { "fallback" },
                Topics = new[] { "fallback" },
                OverallConfidence = 0.5f
            });

        OpenAIOptions options;

        if (UseRealApi)
        {
            // 실제 API 모드: 환경 변수에서 API 키 로드
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? throw new InvalidOperationException("OPENAI_API_KEY not found in .env.local");

            options = new OpenAIOptions
            {
                ApiKey = apiKey,
                ModelName = Environment.GetEnvironmentVariable("OPENAI_MODEL_NAME") ?? "gpt-5-nano"
            };

            MockCompletionService = null; // 실제 API 사용 시 Mock 불필요
        }
        else
        {
            // Mock 모드: 테스트용 API 키
            options = new OpenAIOptions
            {
                ApiKey = "test-api-key",
                ModelName = "text-embedding-3-small"
            };

            // Mock CompletionService 설정
            MockCompletionService = new Mock<ITextCompletionService>();
        }

        // OpenAIMetadataExtractor 생성
        Extractor = new OpenAIMetadataExtractor(
            Options.Create(options),
            MockRuleBasedExtractor.Object,
            MockLogger.Object,
            Cache);
    }

    /// <summary>
    /// Mock 응답 설정 (Mock 모드에서만 동작)
    /// </summary>
    public void SetupMockResponse(string jsonResponse)
    {
        if (UseRealApi)
        {
            // 실제 API 모드에서는 Mock 설정 무시
            return;
        }

        MockCompletionService!
            .Setup(x => x.GenerateJsonCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);
    }

    /// <summary>
    /// Mock 예외 설정 (Mock 모드에서만 동작)
    /// </summary>
    public void SetupMockException<TException>(TException exception) where TException : Exception
    {
        if (UseRealApi)
        {
            // 실제 API 모드에서는 Mock 설정 무시
            return;
        }

        MockCompletionService!
            .Setup(x => x.GenerateJsonCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    /// <summary>
    /// 상태 기반 Mock 응답 설정 (Mock 모드에서만 동작)
    /// </summary>
    public void SetupMockResponseWithCallback(Func<string> callback)
    {
        if (UseRealApi)
        {
            return;
        }

        MockCompletionService!
            .Setup(x => x.GenerateJsonCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(callback);
    }

    /// <summary>
    /// 비동기 상태 기반 Mock 응답 설정 (Mock 모드에서만 동작)
    /// </summary>
    public void SetupMockResponseWithAsyncCallback(Func<Task<string>> callback)
    {
        if (UseRealApi)
        {
            return;
        }

        MockCompletionService!
            .Setup(x => x.GenerateJsonCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(callback);
    }

    public void Dispose()
    {
        Cache.Dispose();
    }
}
