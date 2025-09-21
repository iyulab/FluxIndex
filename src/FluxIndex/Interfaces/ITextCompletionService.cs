using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Interfaces;

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

