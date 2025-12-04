using DotNetEnv;
using FileFlux;
using FluxIndex.Demo.Services;
using FluxIndex.AI.OpenAI;
using FluxIndex.AI.LocalReranker;
using FluxIndex.AI.LocalEmbedder;
using FluxIndex.Storage.SQLite;
using FluxIndex.Core.Application.Interfaces;

// Load environment variables from .env.local
var envPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env.local");
if (File.Exists(envPath))
{
    Env.Load(envPath);
    Console.WriteLine($"Loaded environment from: {envPath}");
}
else
{
    Console.WriteLine($"Warning: .env.local not found at {envPath}");
}

var builder = WebApplication.CreateBuilder(args);

// Configure services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure FluxIndex
var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
var embeddingModel = Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_MODEL") ?? "text-embedding-3-small";

// Configure SQLite storage
var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "fluxindex.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
builder.Services.AddSQLiteVectorStore(options =>
{
    options.DatabasePath = dbPath;
    options.AutoMigrate = true;
});

// Configure embedding service - prefer OpenAI if API key available, fallback to local
if (!string.IsNullOrEmpty(openAiApiKey))
{
    builder.Services.AddOpenAIEmbedding(options =>
    {
        options.ApiKey = openAiApiKey;
        options.ModelName = embeddingModel;
    });
    Console.WriteLine($"Using OpenAI embedding model: {embeddingModel}");
}
else
{
    builder.Services.AddLocalEmbedder(options =>
    {
        options.ModelId = "all-MiniLM-L6-v2";
    });
    Console.WriteLine("Using local embedding model: all-MiniLM-L6-v2");
}

// Configure LocalReranker with resilient fallback
builder.Services.AddResilientLocalReranker();

// Configure FileFlux for document processing (basic integration, no LLM enrichment)
builder.Services.AddFileFlux();

// Register demo services
builder.Services.AddSingleton<DemoState>();
builder.Services.AddScoped<IndexingService>();
builder.Services.AddScoped<SearchService>();

var app = builder.Build();

app.UseCors();
app.UseStaticFiles();

// API Endpoints
app.MapGet("/", () => Results.Redirect("/index.html"));

app.MapGet("/api/status", (DemoState state) => new
{
    state.TotalDocuments,
    state.TotalChunks,
    state.LastIndexed,
    DatabasePath = dbPath,
    EmbeddingModel = !string.IsNullOrEmpty(openAiApiKey) ? embeddingModel : "local:all-MiniLM-L6-v2"
});

app.MapPost("/api/upload", async (HttpRequest request, IndexingService indexingService) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data");

    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();

    if (file == null)
        return Results.BadRequest("No file uploaded");

    var result = await indexingService.IndexFileAsync(file);
    return Results.Ok(result);
});

app.MapPost("/api/search", async (SearchRequest request, SearchService searchService) =>
{
    var results = await searchService.SearchAsync(request.Query, request.TopK, request.UseReranker);
    return Results.Ok(results);
});

app.MapGet("/api/documents", async (IVectorStore vectorStore, DemoState state) =>
{
    // Return simple document list from state
    return Results.Ok(state.GetDocumentList());
});

app.MapDelete("/api/documents/{id}", async (string id, IVectorStore vectorStore, DemoState state) =>
{
    await vectorStore.DeleteByDocumentIdAsync(id);
    state.RemoveDocument(id);
    return Results.Ok(new { Message = "Document deleted" });
});

// Document detail endpoint - returns extracted content and chunks
app.MapGet("/api/documents/{id}", async (string id, IVectorStore vectorStore, DemoState state) =>
{
    var chunks = await vectorStore.GetByDocumentIdAsync(id);
    var chunkList = chunks.ToList();

    if (!chunkList.Any())
    {
        return Results.NotFound(new { Error = "Document not found" });
    }

    var docInfo = state.GetDocumentList().FirstOrDefault(d => d.Id == id);

    // Combine all chunk contents for full extracted text
    var fullContent = string.Join("\n\n---\n\n", chunkList.OrderBy(c => c.ChunkIndex).Select(c => c.Content));

    var result = new DocumentDetailResponse
    {
        Id = id,
        Title = docInfo?.Title ?? "Unknown",
        CreatedAt = docInfo?.CreatedAt ?? DateTime.UtcNow,
        TotalChunks = chunkList.Count,
        FullContent = fullContent,
        Chunks = chunkList.OrderBy(c => c.ChunkIndex).Select(c => new ChunkDetail
        {
            Id = c.Id,
            Index = c.ChunkIndex,
            Content = c.Content,
            TokenCount = c.TokenCount,
            Metadata = c.Metadata ?? new Dictionary<string, object>(),
            ChunkMetadata = c.ChunkMetadata != null ? new ChunkMetadataDto
            {
                Language = c.ChunkMetadata.Language,
                ContentType = c.ChunkMetadata.ContentType,
                Keywords = c.ChunkMetadata.Keywords,
                Entities = c.ChunkMetadata.Entities,
                Topics = c.ChunkMetadata.Topics,
                SectionTitle = c.ChunkMetadata.SectionTitle,
                ImportanceScore = c.ChunkMetadata.ImportanceScore,
                TokenCount = c.ChunkMetadata.TokenCount,
                CharacterCount = c.ChunkMetadata.CharacterCount,
                SentenceCount = c.ChunkMetadata.SentenceCount,
                ReadabilityScore = c.ChunkMetadata.ReadabilityScore
            } : null,
            Quality = c.Quality != null ? new ChunkQualityDto
            {
                ContentCompleteness = c.Quality.ContentCompleteness,
                InformationDensity = c.Quality.InformationDensity,
                Coherence = c.Quality.Coherence,
                Uniqueness = c.Quality.Uniqueness
            } : null
        }).ToList()
    };

    return Results.Ok(result);
});

// MCP-style function endpoint (simulating MCP tool call)
app.MapPost("/api/mcp/search", async (McpSearchRequest request, SearchService searchService) =>
{
    var results = await searchService.SearchWithMcpFormatAsync(
        request.Query,
        request.TopK,
        request.UseReranker,
        request.IncludeMetadata,
        request.MaxTokens);
    return Results.Ok(results);
});

Console.WriteLine("FluxIndex Demo starting...");
Console.WriteLine($"Database: {dbPath}");

app.Run();

// Request/Response models
public record SearchRequest(string Query, int TopK = 10, bool UseReranker = true);
public record McpSearchRequest(string Query, int TopK = 10, bool UseReranker = true, bool IncludeMetadata = true, int MaxTokens = 5000);

// Document detail response models
public class DocumentDetailResponse
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int TotalChunks { get; set; }
    public string FullContent { get; set; } = "";
    public List<ChunkDetail> Chunks { get; set; } = new();
}

public class ChunkDetail
{
    public string Id { get; set; } = "";
    public int Index { get; set; }
    public string Content { get; set; } = "";
    public int TokenCount { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public ChunkMetadataDto? ChunkMetadata { get; set; }
    public ChunkQualityDto? Quality { get; set; }
}

public class ChunkMetadataDto
{
    public string Language { get; set; } = "";
    public string ContentType { get; set; } = "";
    public List<string> Keywords { get; set; } = new();
    public List<string> Entities { get; set; } = new();
    public List<string> Topics { get; set; } = new();
    public string SectionTitle { get; set; } = "";
    public double ImportanceScore { get; set; }
    public int TokenCount { get; set; }
    public int CharacterCount { get; set; }
    public int SentenceCount { get; set; }
    public double ReadabilityScore { get; set; }
}

public class ChunkQualityDto
{
    public double ContentCompleteness { get; set; }
    public double InformationDensity { get; set; }
    public double Coherence { get; set; }
    public double Uniqueness { get; set; }
}
