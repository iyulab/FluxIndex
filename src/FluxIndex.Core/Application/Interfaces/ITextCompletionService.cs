using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 텍스트 완성 서비스 인터페이스 (LLM 기반 평가용)
/// </summary>
public interface ITextCompletionService
{
    /// <summary>
    /// 주어진 프롬프트에 대한 텍스트 완성 생성
    /// </summary>
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default);
}

/// <summary>
/// 테스트용 모의 텍스트 완성 서비스
/// </summary>
public class MockTextCompletionService : ITextCompletionService
{
    public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        // 평가용 기본 점수 반환
        if (prompt.Contains("faithfulness") || prompt.Contains("relevancy"))
        {
            return Task.FromResult("0.8");
        }
        return Task.FromResult("Mock response");
    }
}