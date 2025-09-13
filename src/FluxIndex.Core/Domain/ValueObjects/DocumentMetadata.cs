using System;
using System.Collections.Generic;

namespace FluxIndex.Core.Domain.Entities;

/// <summary>
/// 문서 메타데이터 값 객체
/// </summary>
public class DocumentMetadata
{
    public string Brand { get; private set; }
    public string Model { get; private set; }
    public string Category { get; private set; }
    public string Language { get; private set; }
    public string Version { get; private set; }
    public DateTime? PublishedDate { get; private set; }
    public Dictionary<string, string> CustomFields { get; private set; }
    public Dictionary<string, object> Properties { get; private set; }

    public DocumentMetadata()
    {
        Brand = string.Empty;
        Model = string.Empty;
        Category = string.Empty;
        Language = "ko";
        Version = string.Empty;
        CustomFields = new Dictionary<string, string>();
        Properties = new Dictionary<string, object>();
    }

    public DocumentMetadata(
        string brand,
        string model,
        string category,
        string language = "ko",
        string version = "",
        DateTime? publishedDate = null)
    {
        Brand = brand ?? string.Empty;
        Model = model ?? string.Empty;
        Category = category ?? string.Empty;
        Language = language ?? "ko";
        Version = version ?? string.Empty;
        PublishedDate = publishedDate;
        CustomFields = new Dictionary<string, string>();
        Properties = new Dictionary<string, object>();
    }

    public void AddCustomField(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Custom field key cannot be empty", nameof(key));
        
        CustomFields[key] = value ?? string.Empty;
    }

    public DocumentMetadata WithBrand(string brand)
    {
        Brand = brand ?? string.Empty;
        return this;
    }

    public DocumentMetadata WithModel(string model)
    {
        Model = model ?? string.Empty;
        return this;
    }

    public DocumentMetadata WithCategory(string category)
    {
        Category = category ?? string.Empty;
        return this;
    }

    public DocumentMetadata WithLanguage(string language)
    {
        Language = language ?? "ko";
        return this;
    }

    public DocumentMetadata WithVersion(string version)
    {
        Version = version ?? string.Empty;
        return this;
    }

    public DocumentMetadata WithPublishedDate(DateTime? publishedDate)
    {
        PublishedDate = publishedDate;
        return this;
    }
}