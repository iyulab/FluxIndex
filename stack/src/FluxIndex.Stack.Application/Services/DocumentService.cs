using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileFlux;
using FileFlux.Core;
using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Application.Mappings;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Documents;
using Microsoft.Extensions.Logging;
using ITextCompletionService = FluxIndex.Core.Application.Interfaces.ITextCompletionService;

namespace FluxIndex.Stack.Application.Services;

/// <summary>
/// Service implementation for document operations.
/// </summary>
public class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IIndexingJobRepository? _jobRepository;
    private readonly IIndexingService? _indexingService;
    private readonly IDocumentContentProvider? _contentProvider;
    private readonly IDocumentProcessorFactory? _processorFactory;
    private readonly ITextCompletionService? _textCompletionService;
    private readonly ILogger<DocumentService> _logger;

    // File extensions that require FileFlux extraction (binary formats)
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt",
        ".rtf", ".odt", ".ods", ".odp", ".epub"
    };

    // File extensions that can be read as text directly
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".xml", ".csv", ".tsv",
        ".yaml", ".yml", ".ini", ".cfg", ".conf", ".log",
        ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h",
        ".html", ".htm", ".css", ".scss", ".less"
    };

    public DocumentService(
        IDocumentRepository documentRepository,
        IDocumentChunkRepository chunkRepository,
        ILogger<DocumentService> logger,
        IIndexingJobRepository? jobRepository = null,
        IIndexingService? indexingService = null,
        IDocumentContentProvider? contentProvider = null,
        IDocumentProcessorFactory? processorFactory = null,
        ITextCompletionService? textCompletionService = null)
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _jobRepository = jobRepository;
        _indexingService = indexingService;
        _contentProvider = contentProvider;
        _processorFactory = processorFactory;
        _textCompletionService = textCompletionService;
        _logger = logger;
    }

    public async Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken);
        return document?.ToDto();
    }

    public async Task<DocumentDetailDto?> GetDetailByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdWithChunksAsync(id, cancellationToken);
        if (document == null) return null;

        var chunks = await _chunkRepository.GetByDocumentIdAsync(id, cancellationToken);
        return document.ToDetailDto(chunks);
    }

    public async Task<PagedResult<DocumentDto>> GetPagedAsync(
        int page,
        int pageSize,
        Guid? collectionId = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        DocumentStatus? statusEnum = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<DocumentStatus>(status, true, out var parsed))
        {
            statusEnum = parsed;
        }

        var (items, totalCount) = await _documentRepository.GetPagedAsync(
            page, pageSize, collectionId, statusEnum, cancellationToken);

        var dtos = items.Select(d => d.ToDto()).ToList();
        return PagedResult<DocumentDto>.Create(dtos, page, pageSize, totalCount);
    }

    public async Task<UploadDocumentResponse> UploadAsync(
        UploadDocumentRequest request,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var fileExtension = Path.GetExtension(fileName);
        var fileSize = fileStream.Length;

        // Extract text content based on file type
        string content;
        if (BinaryExtensions.Contains(fileExtension))
        {
            // Use FileFlux to extract text from binary formats (PDF, DOCX, etc.)
            content = await ExtractTextFromBinaryAsync(fileStream, fileName, cancellationToken);
        }
        else
        {
            // Read text directly for text-based formats
            using var reader = new StreamReader(fileStream, detectEncodingFromByteOrderMarks: true);
            content = await reader.ReadToEndAsync(cancellationToken);
        }

        // Compute content hash
        var contentHash = ComputeHash(content);

        // Check for duplicate
        if (await _documentRepository.ContentHashExistsAsync(contentHash, cancellationToken))
        {
            _logger.LogWarning("Document with same content already exists: {Hash}", contentHash);
        }

        // Create document
        var document = Document.Create(
            request.Title,
            request.CollectionId,
            request.SourceType ?? "file",
            fileName);

        document.SetContentHash(contentHash, fileSize);
        document.SetExtractedContent(content);

        if (request.Metadata != null)
        {
            document.SetMetadata(request.Metadata);
        }

        await _documentRepository.AddAsync(document, cancellationToken);

        _logger.LogInformation("Document uploaded: {DocumentId} - {Title}", document.Id, document.Title);

        // Store content for later indexing
        if (_contentProvider != null)
        {
            try
            {
                await _contentProvider.StoreContentAsync(document.Id, content, cancellationToken);
                _logger.LogInformation("Document content stored: {DocumentId}, Size: {Size} bytes", document.Id, content.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store document content: {DocumentId}", document.Id);
            }
        }
        else
        {
            _logger.LogWarning("Content provider not available, document content not stored: {DocumentId}", document.Id);
        }

        // Queue for indexing if service available
        Guid? jobId = null;
        if (_indexingService != null)
        {
            try
            {
                jobId = await _indexingService.QueueIndexingJobAsync(document.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue document for indexing: {DocumentId}", document.Id);
            }
        }

        return new UploadDocumentResponse
        {
            DocumentId = document.Id,
            JobId = jobId,
            Status = document.Status.ToString(),
            Message = jobId.HasValue
                ? "Document uploaded and queued for indexing."
                : "Document uploaded successfully."
        };
    }

    public async Task<UploadDocumentResponse> UploadContentAsync(
        UploadDocumentContentRequest request,
        CancellationToken cancellationToken = default)
    {
        // Compute content hash
        var contentHash = ComputeHash(request.Content);
        var fileSize = Encoding.UTF8.GetByteCount(request.Content);

        // Create document
        var document = Document.Create(
            request.Title,
            request.CollectionId,
            request.SourceType ?? "text");

        document.SetContentHash(contentHash, fileSize);
        document.SetExtractedContent(request.Content);

        if (request.Metadata != null)
        {
            document.SetMetadata(request.Metadata);
        }

        await _documentRepository.AddAsync(document, cancellationToken);

        _logger.LogInformation("Document content uploaded: {DocumentId} - {Title}", document.Id, document.Title);

        // Store content for later indexing
        if (_contentProvider != null)
        {
            try
            {
                await _contentProvider.StoreContentAsync(document.Id, request.Content, cancellationToken);
                _logger.LogInformation("Document content stored: {DocumentId}, Size: {Size} bytes", document.Id, request.Content.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store document content: {DocumentId}", document.Id);
            }
        }
        else
        {
            _logger.LogWarning("Content provider not available, document content not stored: {DocumentId}", document.Id);
        }

        // Queue for indexing if service available
        Guid? jobId = null;
        if (_indexingService != null)
        {
            try
            {
                jobId = await _indexingService.QueueIndexingJobAsync(document.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue document for indexing: {DocumentId}", document.Id);
            }
        }

        return new UploadDocumentResponse
        {
            DocumentId = document.Id,
            JobId = jobId,
            Status = document.Status.ToString(),
            Message = jobId.HasValue
                ? "Document content uploaded and queued for indexing."
                : "Document content uploaded successfully."
        };
    }

    public async Task<DocumentDto> UpdateAsync(
        Guid id,
        UpdateDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Document with id '{id}' not found.");

        document.Update(request.Title, request.Metadata);
        await _documentRepository.UpdateAsync(document, cancellationToken);

        _logger.LogInformation("Document updated: {DocumentId}", id);

        return document.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await _documentRepository.ExistsAsync(id, cancellationToken))
        {
            throw new KeyNotFoundException($"Document with id '{id}' not found.");
        }

        // Cancel any pending/processing indexing jobs for this document
        if (_jobRepository != null)
        {
            var job = await _jobRepository.GetByDocumentIdAsync(id, cancellationToken);
            if (job != null && (job.Status == IndexingJobStatus.Queued || job.Status == IndexingJobStatus.Processing))
            {
                job.Cancel();
                await _jobRepository.UpdateAsync(job, cancellationToken);
                _logger.LogInformation("Cancelled indexing job {JobId} for document {DocumentId}", job.Id, id);
            }
        }

        // Chunks are deleted via cascade
        await _documentRepository.DeleteAsync(id, cancellationToken);

        _logger.LogInformation("Document deleted: {DocumentId}", id);
    }

    public async Task ReindexAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Document with id '{id}' not found.");

        if (_indexingService == null)
        {
            throw new InvalidOperationException("Indexing service is not available.");
        }

        // Delete existing chunks
        await _chunkRepository.DeleteByDocumentIdAsync(id, cancellationToken);

        // Reset document status to Pending (will be set to Processing when job starts)
        document.MarkAsProcessing();
        await _documentRepository.UpdateAsync(document, cancellationToken);

        // Queue a new indexing job
        var jobId = await _indexingService.QueueIndexingJobAsync(id, cancellationToken);
        _logger.LogInformation("Document queued for reindexing: {DocumentId}, JobId: {JobId}", id, jobId);
    }

    public async Task<GenerateQAResponse> GenerateQAAsync(
        Guid id,
        int maxPairs = 10,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdWithChunksAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Document with id '{id}' not found.");

        if (_textCompletionService == null)
        {
            throw new InvalidOperationException("Text completion service is not available. Configure LMSupply or another AI provider.");
        }

        // Get content - prefer stored content, fallback to extracted content
        string? content = null;
        if (_contentProvider != null)
        {
            content = await _contentProvider.GetContentAsync(id, cancellationToken);
        }
        content ??= document.ExtractedContent;

        if (string.IsNullOrWhiteSpace(content))
        {
            return new GenerateQAResponse
            {
                DocumentId = id,
                QAPairsGenerated = 0,
                QAPairs = [],
                Message = "No content available to generate Q&A pairs."
            };
        }

        // Truncate content if too long (LLM context limits)
        const int maxContentLength = 8000;
        var truncatedContent = content.Length > maxContentLength
            ? content[..maxContentLength] + "\n\n[Content truncated...]"
            : content;

        // Generate Q&A pairs using LLM
        var prompt = $$"""
            Based on the following document content, generate {{maxPairs}} question-answer pairs that test understanding of the key concepts and information in the document.

            Format your response as a JSON array with objects containing "question" and "answer" fields.
            Example format:
            [
              {"question": "What is X?", "answer": "X is..."},
              {"question": "How does Y work?", "answer": "Y works by..."}
            ]

            Document content:
            {{truncatedContent}}

            Generate exactly {{maxPairs}} Q&A pairs in JSON format only, no additional text:
            """;

        try
        {
            var response = await _textCompletionService.GenerateJsonCompletionAsync(prompt, maxTokens: 2000, cancellationToken);

            // Parse JSON response
            var qaPairs = ParseQAPairs(response, maxPairs);

            // Update document with generated Q&A pairs
            var domainQAPairs = qaPairs.Select(qa => new DocumentQAPair
            {
                Question = qa.Question,
                Answer = qa.Answer
            }).ToList();

            document.SetQAPairs(domainQAPairs);
            await _documentRepository.UpdateAsync(document, cancellationToken);

            _logger.LogInformation("Generated {Count} Q&A pairs for document {DocumentId}",
                qaPairs.Count, id);

            return new GenerateQAResponse
            {
                DocumentId = id,
                QAPairsGenerated = qaPairs.Count,
                QAPairs = qaPairs,
                Message = $"Successfully generated {qaPairs.Count} Q&A pairs."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Q&A pairs for document {DocumentId}", id);
            throw new InvalidOperationException($"Failed to generate Q&A pairs: {ex.Message}", ex);
        }
    }

    private static List<QAPairDto> ParseQAPairs(string response, int maxPairs)
    {
        var result = new List<QAPairDto>();

        try
        {
            // Find JSON array in response
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonContent = response[jsonStart..(jsonEnd + 1)];
                var pairs = JsonSerializer.Deserialize<List<QAPairJson>>(jsonContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (pairs != null)
                {
                    result = pairs
                        .Where(p => !string.IsNullOrWhiteSpace(p.Question) && !string.IsNullOrWhiteSpace(p.Answer))
                        .Take(maxPairs)
                        .Select(p => new QAPairDto
                        {
                            Question = p.Question!.Trim(),
                            Answer = p.Answer!.Trim()
                        })
                        .ToList();
                }
            }
        }
        catch (JsonException)
        {
            // If JSON parsing fails, return empty list
        }

        return result;
    }

    private sealed class QAPairJson
    {
        public string? Question { get; set; }
        public string? Answer { get; set; }
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Extracts text content from binary file formats using FileFlux.
    /// Supports PDF, DOCX, XLSX, PPTX, and other binary document formats.
    /// </summary>
    private async Task<string> ExtractTextFromBinaryAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken)
    {
        if (_processorFactory == null)
        {
            _logger.LogWarning(
                "FileFlux processor not available for binary file extraction. " +
                "File '{FileName}' will be stored as raw bytes which may cause display issues.",
                fileName);

            // Fallback: read as text (will likely produce garbage for binary files)
            fileStream.Position = 0;
            using var reader = new StreamReader(fileStream, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        // Save stream to temp file for FileFlux processing
        var tempPath = Path.Combine(Path.GetTempPath(), $"fluxindex_upload_{Guid.NewGuid():N}{Path.GetExtension(fileName)}");

        try
        {
            // Copy stream to temp file
            await using (var tempFile = File.Create(tempPath))
            {
                fileStream.Position = 0;
                await fileStream.CopyToAsync(tempFile, cancellationToken);
            }

            _logger.LogInformation("Extracting text from binary file: {FileName} ({TempPath})", fileName, tempPath);

            // Use FileFlux to extract text
            await using var processor = _processorFactory.Create(tempPath);

            // Use large chunk size to get all text in minimal chunks
            var processingOptions = new ProcessingOptions
            {
                Chunking = new FileFlux.Core.ChunkingOptions
                {
                    Strategy = ChunkingStrategies.Auto,
                    MaxChunkSize = int.MaxValue, // Get all text without chunking
                    MinChunkSize = 0
                }
            };

            var extractedText = new StringBuilder();
            await foreach (var chunk in processor.ProcessStreamAsync(processingOptions, cancellationToken))
            {
                extractedText.Append(chunk.Content);
            }

            var result = extractedText.ToString();

            _logger.LogInformation(
                "Text extraction completed for {FileName}: {OriginalSize} bytes -> {ExtractedLength} chars",
                fileName, fileStream.Length, result.Length);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from binary file: {FileName}", fileName);

            // Return empty string rather than corrupted binary data
            return $"[Text extraction failed for {fileName}: {ex.Message}]";
        }
        finally
        {
            // Clean up temp file
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temp file: {TempPath}", tempPath);
            }
        }
    }
}
