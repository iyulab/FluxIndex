using System;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.AI.OpenAI.Services;

/// <summary>
/// OpenAI API 클라이언트 인터페이스
/// 테스트 가능성을 위한 추상화
/// </summary>
public interface IOpenAIClient
{
    /// <summary>
    /// 텍스트 완성 요청
    /// </summary>
    /// <param name="prompt">입력 프롬프트</param>
    /// <param name="timeout">요청 타임아웃</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>완성된 텍스트</returns>
    /// <exception cref="TimeoutException">타임아웃 시</exception>
    /// <exception cref="UnauthorizedAccessException">인증 실패 시</exception>
    /// <exception cref="InvalidOperationException">서비스 사용 불가 시</exception>
    Task<string> CompleteAsync(
        string prompt,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 클라이언트 상태 확인
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>클라이언트가 정상 작동하는지 여부</returns>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}