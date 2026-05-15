using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using ITextCompletionService = Flux.Abstractions.ITextCompletionService;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// 하이브리드 Contextual Header 생성기
/// 규칙 기반 + LLM 기반 접근을 결합하여 비용 최적화
/// </summary>
public partial class HybridContextualHeaderGenerator : IContextualHeaderGenerator
{
    private readonly ITextCompletionService? _textCompletion;
    private readonly ContextualHeaderOptions _options;
    private readonly ILogger<HybridContextualHeaderGenerator> _logger;

    public HybridContextualHeaderGenerator(
        IOptions<ContextualHeaderOptions> options,
        ILogger<HybridContextualHeaderGenerator> logger,
        ITextCompletionService? textCompletion = null)
    {
        _options = options.Value;
        _logger = logger;
        _textCompletion = textCompletion;
    }

    public async Task<string> GenerateAsync(
        IEnrichedChunk chunk,
        string? documentSummary = null,
        CancellationToken cancellationToken = default)
    {
        // ContextDependency가 임계값 미만이면 규칙 기반
        if (chunk.ContextDependency < _options.LlmThreshold)
        {
            LogHybridContextualHeader6(_logger, chunk.ChunkId, chunk.ContextDependency);
            return GenerateRuleBased(chunk);
        }

        // LLM이 있으면 LLM 기반
        if (_textCompletion != null)
        {
            LogHybridContextualHeader5(_logger, chunk.ChunkId, chunk.ContextDependency);
            return await GenerateLlmBasedAsync(chunk, documentSummary, cancellationToken);
        }

        // LLM이 없으면 규칙 기반 폴백
        LogHybridContextualHeader4(_logger, chunk.ChunkId);
        return GenerateRuleBased(chunk);
    }

    public async Task<Dictionary<string, string>> GenerateBatchAsync(
        IEnumerable<IEnrichedChunk> chunks,
        string? documentSummary = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, string>();
        var chunkList = chunks.ToList();

        // 규칙 기반과 LLM 기반 분류
        var ruleBasedChunks = chunkList.Where(c => c.ContextDependency < _options.LlmThreshold).ToList();
        var llmBasedChunks = chunkList.Where(c => c.ContextDependency >= _options.LlmThreshold).ToList();

        // 규칙 기반 처리 (즉시)
        foreach (var chunk in ruleBasedChunks)
        {
            results[chunk.ChunkId] = GenerateRuleBased(chunk);
        }

        // LLM 기반 처리
        if (_textCompletion != null && llmBasedChunks.Count > 0)
        {
            LogHybridContextualHeader3(_logger, llmBasedChunks.Count, chunkList.Count);

            foreach (var chunk in llmBasedChunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results[chunk.ChunkId] = await GenerateLlmBasedAsync(chunk, documentSummary, cancellationToken);
            }
        }
        else
        {
            // LLM 없으면 규칙 기반 폴백
            foreach (var chunk in llmBasedChunks)
            {
                results[chunk.ChunkId] = GenerateRuleBased(chunk);
            }
        }

        LogHybridContextualHeader2(_logger, ruleBasedChunks.Count, llmBasedChunks.Count);

        return results;
    }

    /// <summary>
    /// 규칙 기반 Contextual Header 생성
    /// HeadingPath, 페이지 정보 등을 활용
    /// </summary>
    private string GenerateRuleBased(IEnrichedChunk chunk)
    {
        var parts = new List<string>();

        // 문서 제목
        if (_options.IncludeDocumentTitle && !string.IsNullOrEmpty(chunk.Source.Title))
        {
            parts.Add($"[{chunk.Source.Title}]");
        }

        // HeadingPath
        if (_options.IncludeHeadingPath && chunk.HeadingPath.Count > 0)
        {
            var headingPath = string.Join(" > ", chunk.HeadingPath);
            parts.Add($"[{headingPath}]");
        }

        // 페이지 정보
        if (_options.IncludePageInfo && chunk.StartPage.HasValue)
        {
            var pageInfo = chunk.EndPage.HasValue && chunk.EndPage != chunk.StartPage
                ? $"pp.{chunk.StartPage}-{chunk.EndPage}"
                : $"p.{chunk.StartPage}";
            parts.Add($"[{pageInfo}]");
        }

        var header = string.Join(" ", parts);

        // 최대 길이 제한
        if (header.Length > _options.MaxHeaderLength)
        {
            header = header[.._options.MaxHeaderLength] + "...";
        }

        return header;
    }

    /// <summary>
    /// LLM 기반 Contextual Header 생성
    /// Anthropic의 Contextual Retrieval 프롬프트 사용
    /// </summary>
    private async Task<string> GenerateLlmBasedAsync(
        IEnrichedChunk chunk,
        string? documentSummary,
        CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(chunk, documentSummary);

        try
        {
            var response = await _textCompletion!.CompleteAsync(
                prompt, new Flux.Abstractions.TextCompletionOptions { MaxTokens = 150, Temperature = 0.3f }, cancellationToken);

            // 응답 정제
            var header = response.Trim();

            // 최대 길이 제한
            if (header.Length > _options.MaxHeaderLength)
            {
                header = header[.._options.MaxHeaderLength] + "...";
            }

            return header;
        }
        catch (Exception ex)
        {
            LogHybridContextualHeader1(_logger, ex, chunk.ChunkId);
            return GenerateRuleBased(chunk);
        }
    }

    /// <summary>
    /// Contextual Retrieval 프롬프트 생성
    /// </summary>
    private static string BuildPrompt(IEnrichedChunk chunk, string? documentSummary)
    {
        var contextInfo = new List<string>
        {
            $"Document: {chunk.Source.Title}"
        };

        if (chunk.HeadingPath.Count > 0)
        {
            contextInfo.Add($"Section: {string.Join(" > ", chunk.HeadingPath)}");
        }

        if (chunk.StartPage.HasValue)
        {
            contextInfo.Add($"Page: {chunk.StartPage}");
        }

        if (!string.IsNullOrEmpty(documentSummary))
        {
            contextInfo.Add($"Document Summary: {documentSummary}");
        }

        var contextSection = string.Join("\n", contextInfo);

        return $"""
            <document>
            {contextSection}
            </document>

            <chunk>
            {chunk.Content}
            </chunk>

            Please give a short succinct context to situate this chunk within the overall document for the purposes of improving search retrieval of the chunk. Answer only with the succinct context and nothing else. Keep it under 100 words.
            """;
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Using rule-based header for chunk {ChunkId} (ContextDependency: {Dependency})")]
    private static partial void LogHybridContextualHeader6(ILogger logger, string chunkId, double dependency);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Using LLM-based header for chunk {ChunkId} (ContextDependency: {Dependency})")]
    private static partial void LogHybridContextualHeader5(ILogger logger, string chunkId, double dependency);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Falling back to rule-based header (no LLM available) for chunk {ChunkId}")]
    private static partial void LogHybridContextualHeader4(ILogger logger, string chunkId);
    [LoggerMessage(Level = LogLevel.Information, Message = "Processing {Count} chunks with LLM (out of {Total} total)")]
    private static partial void LogHybridContextualHeader3(ILogger logger, int count, int total);
    [LoggerMessage(Level = LogLevel.Information, Message = "Generated headers: {RuleBased} rule-based, {LlmBased} LLM-based")]
    private static partial void LogHybridContextualHeader2(ILogger logger, int ruleBased, int llmBased);
    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM header generation failed for chunk {ChunkId}, falling back to rule-based")]
    private static partial void LogHybridContextualHeader1(ILogger logger, Exception exception, string chunkId);

    #endregion
}
