namespace FluxIndex.Service.Domain.Entities;

/// <summary>
/// Represents a collection of documents for organizing and managing content.
/// </summary>
public class Collection
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public CollectionSettings Settings { get; private set; } = new();
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    // Navigation
    private readonly List<Document> _documents = new();
    public IReadOnlyCollection<Document> Documents => _documents.AsReadOnly();

    private Collection() { } // EF Core

    public static Collection Create(string name, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Collection
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Settings = new CollectionSettings(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Update(string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Description = description;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateSettings(CollectionSettings settings)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        UpdatedAt = DateTime.UtcNow;
    }

    public void AddDocument(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _documents.Add(document);
        UpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// Collection-specific settings for chunking, embedding, and search behavior.
/// </summary>
public class CollectionSettings
{
    public int ChunkSize { get; set; } = 1000;
    public int ChunkOverlap { get; set; } = 200;
    public string ChunkingStrategy { get; set; } = "intelligent";
    public bool EnableQAGeneration { get; set; } = false;
    public bool EnableEnrichment { get; set; } = false;
    public Dictionary<string, object> CustomSettings { get; set; } = new();
}
