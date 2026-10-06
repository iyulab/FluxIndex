using System;
using System.Collections.Generic;

namespace FluxIndex.SDK;

/// <summary>
/// 문서 메타데이터
/// </summary>
public class DocumentMetadata
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Language { get; set; } = "ko";
    public string Version { get; set; } = string.Empty;
    public DateTime? PublishedDate { get; set; }
    public Dictionary<string, string> CustomFields { get; set; } = new();
}