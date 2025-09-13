using FileFlux;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.SDK;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace FluxIndex.Extensions.FileFlux;

/// <summary>
/// FileFlux와 FluxIndex를 통합하는 메인 서비스
/// </summary>
public class FileFluxIntegration : IFileFluxIntegration
{
    private readonly IDocumentProcessor _fileFlux;
    private readonly IFluxIndexClient _fluxIndex;
    private readonly ILogger<FileFluxIntegration> _logger;

    public FileFluxIntegration(
        IDocumentProcessor fileFlux,
        IFluxIndexClient fluxIndex,
        ILogger<FileFluxIntegration> logger)
    {
        _fileFlux = fileFlux;
        _fluxIndex = fluxIndex;
        _logger = logger;
    }

    public async Task<ProcessingResult> ProcessAndIndexAsync(
        string filePath,
        ProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ProcessingOptions();
        
        _logger.LogInformation("Processing file: {FilePath}", filePath);

        // FileFlux로 문서 처리
        var chunks = await _fileFlux.ProcessAsync(filePath, new ChunkingOptions
        {
            Strategy = options.ChunkingStrategy,
            MaxSize = options.MaxChunkSize,
            OverlapSize = options.OverlapSize
        });

        if (!chunks.Any())
        {
            _logger.LogWarning("No chunks generated from file: {FilePath}", filePath);
            return new ProcessingResult { Success = false };
        }

        // FluxIndex 청크로 변환
        var fluxIndexChunks = new List<DocumentChunk>();
        double totalQuality = 0;
        
        foreach (var chunk in chunks)
        {
            var fluxChunk = new DocumentChunk(chunk.Content, chunk.ChunkIndex);
            fluxChunk.TokenCount = EstimateTokens(chunk.Content);
            fluxChunk.AddProperty("StartPosition", chunk.StartPosition);
            fluxChunk.AddProperty("EndPosition", chunk.EndPosition);
            fluxChunk.AddProperty("CharacterCount", chunk.Content.Length);

            // 품질 점수 계산 (시뮬레이션)
            if (options.EnableQualityScoring)
            {
                var quality = CalculateChunkQuality(chunk);
                fluxChunk.AddProperty("quality_score", quality);
                totalQuality += quality;
            }

            // FileFlux 메타데이터 보존
            if (chunk.Properties != null)
            {
                foreach (var prop in chunk.Properties)
                {
                    fluxChunk.AddProperty($"fileflux_{prop.Key}", prop.Value);
                }
            }

            fluxIndexChunks.Add(fluxChunk);
        }

        // FluxIndex로 인덱싱
        var documentId = await _fluxIndex.IndexChunksAsync(
            fluxIndexChunks,
            Path.GetFileNameWithoutExtension(filePath),
            null,
            cancellationToken);

        return new ProcessingResult
        {
            Success = true,
            DocumentId = documentId,
            ChunkCount = fluxIndexChunks.Count,
            AverageQualityScore = options.EnableQualityScoring ? totalQuality / fluxIndexChunks.Count : 0,
            MetadataCount = fluxIndexChunks.Sum(c => c.Properties.Count)
        };
    }

    public async IAsyncEnumerable<ProcessingProgress> ProcessWithProgressAsync(
        string filePath,
        ProcessingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new ProcessingOptions();
        
        var chunkIndex = 0;
        await foreach (var chunk in _fileFlux.ProcessStreamAsync(filePath, cancellationToken))
        {
            var fluxChunk = new DocumentChunk(chunk.Content, chunkIndex);
            fluxChunk.TokenCount = EstimateTokens(chunk.Content);
            fluxChunk.AddProperty("CharacterCount", chunk.Content.Length);

            var quality = options.EnableQualityScoring ? CalculateChunkQuality(chunk) : 0;
            
            yield return new ProcessingProgress
            {
                ChunkIndex = chunkIndex++,
                CurrentChunk = fluxChunk,
                QualityScore = quality,
                TokenCount = fluxChunk.TokenCount,
                Status = ProcessingStatus.InProgress
            };
        }

        yield return new ProcessingProgress
        {
            ChunkIndex = chunkIndex,
            Status = ProcessingStatus.Completed
        };
    }

    public async Task<List<SearchResult>> SearchWithStrategyAsync(
        string query,
        RerankingStrategy strategy,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        var results = await _fluxIndex.SearchAsync(
            query,
            topK,
            0.5f,
            null,
            cancellationToken);

        return results.Select(r => new SearchResult
        {
            Chunk = new DocumentChunk(r.Content, r.ChunkIndex)
            {
                Id = r.Id,
                DocumentId = r.DocumentId
            },
            Score = r.Score,
            Metadata = new ChunkMetadata()
        }).ToList();
    }

    public async Task<MultimodalResult> ProcessMultimodalDocumentAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var result = new MultimodalResult
        {
            DocumentId = Guid.NewGuid().ToString()
        };

        // FileFlux로 멀티모달 처리
        var chunks = await _fileFlux.ProcessAsync(filePath, new ChunkingOptions
        {
            Strategy = "Multimodal",
            ExtractImages = true,
            ExtractTables = true
        });

        foreach (var chunk in chunks)
        {
            if (chunk.Properties?.ContainsKey("content_type") == true)
            {
                var contentType = chunk.Properties["content_type"].ToString();
                
                if (contentType == "image")
                {
                    result.ImageChunks++;
                }
                else
                {
                    result.TextChunks++;
                }

                var quality = CalculateChunkQuality(chunk);
                result.TotalQuality += quality;
            }

            result.TotalChunks++;
        }

        if (result.TotalChunks > 0)
        {
            result.AverageQuality = result.TotalQuality / result.TotalChunks;
        }

        return result;
    }

    private int EstimateTokens(string content)
    {
        // 간단한 토큰 추정 (실제로는 더 정교한 토크나이저 사용)
        return content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private double CalculateChunkQuality(IDocumentChunk chunk)
    {
        // 품질 점수 계산 로직 (시뮬레이션)
        double quality = 0.5;

        // 콘텐츠 길이 기반
        if (chunk.Content.Length > 100)
            quality += 0.1;
        if (chunk.Content.Length > 300)
            quality += 0.1;

        // 문장 완성도 (마침표로 끝나는지)
        if (chunk.Content.TrimEnd().EndsWith('.'))
            quality += 0.15;

        // 키워드 밀도 (시뮬레이션)
        var keywords = chunk.Content.Split(' ').Distinct().Count();
        var totalWords = chunk.Content.Split(' ').Length;
        if (totalWords > 0)
        {
            var density = (double)keywords / totalWords;
            quality += Math.Min(0.15, density * 0.3);
        }

        return Math.Min(1.0, quality);
    }
}

public interface IFileFluxIntegration
{
    Task<ProcessingResult> ProcessAndIndexAsync(
        string filePath,
        ProcessingOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ProcessingProgress> ProcessWithProgressAsync(
        string filePath,
        ProcessingOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<List<SearchResult>> SearchWithStrategyAsync(
        string query,
        RerankingStrategy strategy,
        int topK = 10,
        CancellationToken cancellationToken = default);

    Task<MultimodalResult> ProcessMultimodalDocumentAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}

public class ProcessingOptions
{
    public string ChunkingStrategy { get; set; } = "Auto";
    public int MaxChunkSize { get; set; } = 512;
    public int OverlapSize { get; set; } = 64;
    public bool EnableQualityScoring { get; set; } = true;
}

public class ProcessingResult
{
    public bool Success { get; set; }
    public string DocumentId { get; set; } = string.Empty;
    public int ChunkCount { get; set; }
    public double AverageQualityScore { get; set; }
    public int MetadataCount { get; set; }
}

public class ProcessingProgress
{
    public int ChunkIndex { get; set; }
    public DocumentChunk? CurrentChunk { get; set; }
    public double QualityScore { get; set; }
    public int TokenCount { get; set; }
    public ProcessingStatus Status { get; set; }
}

public enum ProcessingStatus
{
    Pending,
    InProgress,
    Completed,
    Failed
}

public class SearchResult
{
    public DocumentChunk Chunk { get; set; } = null!;
    public double Score { get; set; }
    public ChunkMetadata Metadata { get; set; } = null!;
}

public class MultimodalResult
{
    public string DocumentId { get; set; } = string.Empty;
    public int TotalChunks { get; set; }
    public int TextChunks { get; set; }
    public int ImageChunks { get; set; }
    public double TotalQuality { get; set; }
    public double AverageQuality { get; set; }
}

// FileFlux 인터페이스 임시 정의 (실제 FileFlux 패키지 사용 시 제거)
public interface IDocumentProcessor
{
    Task<List<IDocumentChunk>> ProcessAsync(string filePath, ChunkingOptions options);
    IAsyncEnumerable<IDocumentChunk> ProcessStreamAsync(string filePath, CancellationToken cancellationToken);
}

public interface IDocumentChunk
{
    string Content { get; }
    int ChunkIndex { get; }
    int StartPosition { get; }
    int EndPosition { get; }
    Dictionary<string, object>? Properties { get; }
}

public class ChunkingOptions
{
    public string Strategy { get; set; } = "Auto";
    public int MaxSize { get; set; } = 512;
    public int OverlapSize { get; set; } = 64;
    public bool ExtractImages { get; set; }
    public bool ExtractTables { get; set; }
}