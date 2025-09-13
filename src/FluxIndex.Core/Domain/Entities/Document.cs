using System;
using System.Collections.Generic;

namespace FluxIndex.Core.Domain.Entities;

/// <summary>
/// 문서 도메인 엔티티 - 청킹된 데이터의 논리적 그룹
/// </summary>
public class Document
{
    public string Id { get; private set; }
    public string FileName { get; private set; }
    public string FilePath { get; private set; }
    public string Content { get; private set; }
    public DocumentMetadata Metadata { get; private set; }
    public List<DocumentChunk> Chunks { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DocumentStatus Status { get; private set; }

    private Document() 
    {
        Chunks = new List<DocumentChunk>();
        Metadata = new DocumentMetadata();
        FileName = string.Empty;
        FilePath = string.Empty;
        Content = string.Empty;
    }

    public static Document Create(string? id = null)
    {
        return new Document
        {
            Id = id ?? Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Status = DocumentStatus.Pending
        };
    }

    public void AddChunk(DocumentChunk chunk)
    {
        if (chunk == null) throw new ArgumentNullException(nameof(chunk));
        Chunks.Add(chunk);
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateMetadata(DocumentMetadata metadata)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsIndexed()
    {
        Status = DocumentStatus.Indexed;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsFailed(string reason)
    {
        Status = DocumentStatus.Failed;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetFileName(string fileName)
    {
        FileName = fileName ?? string.Empty;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetFilePath(string filePath)
    {
        FilePath = filePath ?? string.Empty;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetContent(string content)
    {
        Content = content ?? string.Empty;
        UpdatedAt = DateTime.UtcNow;
    }
}

public enum DocumentStatus
{
    Pending,
    Processing,
    Indexed,
    Failed,
    Deleted
}