using FileFlux;
using FluxIndex.Domain.Entities;
using FluxIndex.SDK;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Extensions.FileFlux;

/// <summary>
/// Simple FileFlux integration for FluxIndex
/// </summary>
public class FileFluxIntegration
{
    private readonly IDocumentProcessor _fileFlux;
    private readonly FluxIndexContext _fluxIndex;
    private readonly ILogger<FileFluxIntegration> _logger;

    public FileFluxIntegration(
        IDocumentProcessor fileFlux,
        FluxIndexContext fluxIndex,
        ILogger<FileFluxIntegration> logger)
    {
        _fileFlux = fileFlux;
        _fluxIndex = fluxIndex;
        _logger = logger;
    }

    /// <summary>
    /// Process a file with FileFlux and index with FluxIndex
    /// </summary>
    public async Task<string> ProcessAndIndexAsync(
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
            throw new InvalidOperationException($"No chunks generated from file: {filePath}");
        }

        // FluxIndex 청크로 변환
        var fluxIndexChunks = new List<DocumentChunk>();

        foreach (var chunk in chunks)
        {
            var fluxChunk = new DocumentChunk(chunk.Content, chunk.ChunkIndex)
            {
                DocumentId = Path.GetFileNameWithoutExtension(filePath)
            };

            // 메타데이터 추가
            if (fluxChunk.Metadata == null)
                fluxChunk.Metadata = new Dictionary<string, object>();

            fluxChunk.Metadata["StartPosition"] = chunk.StartPosition;
            fluxChunk.Metadata["EndPosition"] = chunk.EndPosition;
            fluxChunk.Metadata["CharacterCount"] = chunk.Content.Length;

            // FileFlux 메타데이터 보존
            if (chunk.Properties != null)
            {
                foreach (var prop in chunk.Properties)
                {
                    fluxChunk.Metadata![$"fileflux_{prop.Key}"] = prop.Value;
                }
            }

            fluxIndexChunks.Add(fluxChunk);
        }

        // FluxIndex Document 생성 및 인덱싱
        var document = Document.Create(Path.GetFileNameWithoutExtension(filePath));
        document.Content = string.Join("\n", fluxIndexChunks.Select(c => c.Content));
        document.Chunks = fluxIndexChunks;
        document.Metadata["source_file"] = filePath;
        document.Metadata["source_type"] = "file";

        var documentId = await _fluxIndex.Indexer.IndexDocumentAsync(
            document: document,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Successfully indexed {ChunkCount} chunks for document {DocumentId}",
            fluxIndexChunks.Count, documentId);

        return documentId;
    }
}

/// <summary>
/// Processing options for FileFlux integration
/// </summary>
public class ProcessingOptions
{
    public string ChunkingStrategy { get; set; } = "Auto";
    public int MaxChunkSize { get; set; } = 512;
    public int OverlapSize { get; set; } = 64;
}

// FileFlux 인터페이스 임시 정의 (실제 FileFlux 패키지 사용 시 제거)
public interface IDocumentProcessor
{
    Task<List<IDocumentChunk>> ProcessAsync(string filePath, ChunkingOptions options);
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
}