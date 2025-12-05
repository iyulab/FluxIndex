using DotNetEnv;
using FileFlux;
using FluxIndex.Demo.Services;
using FluxIndex.AI.OpenAI;
using FluxIndex.AI.LocalReranker;
using FluxIndex.AI.LocalEmbedder;
using FluxIndex.Storage.SQLite;
using FluxIndex.Storage.PostgreSQL;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Extensions.FluxImprover;
using FluxIndex.Extensions.FluxImprover.Services;
using FluxImprover.Evaluation;
using FluxImprover.QAGeneration;
using FluxImproverCompletion = FluxImprover.Services.ITextCompletionService;
using Microsoft.Extensions.Logging;

// Load environment variables from .env file
var envPath = Path.Combine(Directory.GetCurrentDirectory(), "..", ".env");
if (File.Exists(envPath))
{
    Env.Load(envPath);
    Console.WriteLine($"Loaded environment from: {envPath}");
}
else
{
    // Try demo directory
    var demoEnvPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
    if (File.Exists(demoEnvPath))
    {
        Env.Load(demoEnvPath);
        Console.WriteLine($"Loaded environment from: {demoEnvPath}");
    }
    else
    {
        Console.WriteLine("Warning: .env file not found. Using defaults.");
    }
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

// Get configuration from environment
var storageBackend = Environment.GetEnvironmentVariable("STORAGE_BACKEND") ?? "sqlite";
var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
var embeddingModel = Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_MODEL") ?? "text-embedding-3-small";
var enableGraphFeatures = Environment.GetEnvironmentVariable("ENABLE_GRAPH_FEATURES") == "true";

// Configure storage backend
string storageInfo;
if (storageBackend.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
{
    var pgHost = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
    var pgPort = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
    var pgUser = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "fluxindex";
    var pgPassword = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "fluxindex123";
    var pgDatabase = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "fluxindex";

    var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
        ?? $"Host={pgHost};Port={pgPort};Database={pgDatabase};Username={pgUser};Password={pgPassword}";

    // Determine embedding dimensions based on model
    var embeddingDimensions = GetEmbeddingDimensions(embeddingModel, openAiApiKey);

    builder.Services.AddPostgreSQLVectorStore(options =>
    {
        options.ConnectionString = connectionString;
        options.EmbeddingDimensions = embeddingDimensions;
        options.AutoMigrate = true;
    });

    storageInfo = $"PostgreSQL ({pgHost}:{pgPort}/{pgDatabase})";
    Console.WriteLine($"Using PostgreSQL storage: {pgHost}:{pgPort}/{pgDatabase}");
}
else
{
    // Default to SQLite
    var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "fluxindex.db");
    Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

    builder.Services.AddSQLiteVectorStore(options =>
    {
        options.DatabasePath = dbPath;
        options.AutoMigrate = true;
    });

    storageInfo = $"SQLite ({dbPath})";
    Console.WriteLine($"Using SQLite storage: {dbPath}");
}

// Configure embedding service - prefer OpenAI if API key available, fallback to local
string embeddingInfo;
if (!string.IsNullOrEmpty(openAiApiKey))
{
    builder.Services.AddOpenAIEmbedding(options =>
    {
        options.ApiKey = openAiApiKey;
        options.ModelName = embeddingModel;
    });
    embeddingInfo = $"OpenAI ({embeddingModel})";
    Console.WriteLine($"Using OpenAI embedding model: {embeddingModel}");
}
else
{
    var localModel = Environment.GetEnvironmentVariable("LOCAL_EMBEDDING_MODEL") ?? "all-MiniLM-L6-v2";
    builder.Services.AddLocalEmbedder(options =>
    {
        options.ModelId = localModel;
    });
    embeddingInfo = $"Local ({localModel})";
    Console.WriteLine($"Using local embedding model: {localModel}");
}

// Configure LocalReranker with resilient fallback
builder.Services.AddResilientLocalReranker();

// Configure FileFlux for document processing
builder.Services.AddFileFlux();

// Configure FluxImprover services for QA generation (requires OpenAI API key)
var completionModel = Environment.GetEnvironmentVariable("OPENAI_COMPLETION_MODEL") ?? "gpt-5-nano";
if (!string.IsNullOrEmpty(openAiApiKey))
{
    // Register OpenAI Text Completion Service
    builder.Services.AddOpenAITextCompletion(options =>
    {
        options.ApiKey = openAiApiKey;
        options.ModelName = completionModel;
    });

    // Register FluxImprover Text Completion Adapter (bridges FluxIndex → FluxImprover)
    builder.Services.AddFluxImproverTextCompletion();

    // Register FluxImprover Evaluators (all depend on FluxImprover's ITextCompletionService)
    builder.Services.AddSingleton<AnswerabilityEvaluator>(provider =>
        new AnswerabilityEvaluator(provider.GetRequiredService<FluxImproverCompletion>()));
    builder.Services.AddSingleton<FaithfulnessEvaluator>(provider =>
        new FaithfulnessEvaluator(provider.GetRequiredService<FluxImproverCompletion>()));
    builder.Services.AddSingleton<RelevancyEvaluator>(provider =>
        new RelevancyEvaluator(provider.GetRequiredService<FluxImproverCompletion>()));

    // Register FluxImprover QA Services
    builder.Services.AddSingleton<QAGeneratorService>(provider =>
        new QAGeneratorService(provider.GetRequiredService<FluxImproverCompletion>()));
    builder.Services.AddSingleton<QAFilterService>(provider =>
        new QAFilterService(
            provider.GetRequiredService<FaithfulnessEvaluator>(),
            provider.GetRequiredService<RelevancyEvaluator>(),
            provider.GetRequiredService<AnswerabilityEvaluator>()));
    builder.Services.AddSingleton<QAPipeline>(provider =>
        new QAPipeline(
            provider.GetRequiredService<QAGeneratorService>(),
            provider.GetRequiredService<QAFilterService>()));

    // Register FluxIndex QAGenerationService wrapper
    builder.Services.AddQAGeneration();

    // Register RAGEvaluationService for quality evaluation
    builder.Services.AddRAGEvaluation();

    // Register ParallelPipelineExecutor for streaming QA generation
    builder.Services.AddSingleton<FluxIndex.Extensions.FluxImprover.Services.ParallelPipelineExecutor>(provider =>
        new FluxIndex.Extensions.FluxImprover.Services.ParallelPipelineExecutor(
            enrichmentService: null,
            qaService: provider.GetRequiredService<QAGenerationService>(),
            evaluationService: provider.GetService<RAGEvaluationService>(),
            logger: provider.GetService<ILogger<FluxIndex.Extensions.FluxImprover.Services.ParallelPipelineExecutor>>()));

    Console.WriteLine($"FluxImprover QA generation enabled with model: {completionModel}");
}
else
{
    Console.WriteLine("FluxImprover QA generation disabled (no OPENAI_API_KEY)");
}

// Register demo services
builder.Services.AddSingleton<DemoState>();
builder.Services.AddScoped<IndexingService>();
builder.Services.AddScoped<SearchService>();
builder.Services.AddSingleton<ProcessLogService>();

// Store configuration for status endpoint
builder.Services.AddSingleton(new DemoConfiguration
{
    StorageBackend = storageBackend,
    StorageInfo = storageInfo,
    EmbeddingInfo = embeddingInfo,
    CompletionModel = !string.IsNullOrEmpty(openAiApiKey) ? completionModel : "N/A",
    GraphFeaturesEnabled = enableGraphFeatures
});

var app = builder.Build();

// Run database migration for PostgreSQL
if (storageBackend.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetService<FluxIndexDbContext>();
    if (dbContext != null)
    {
        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            Console.WriteLine("PostgreSQL database initialized successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not initialize PostgreSQL database: {ex.Message}");
            Console.WriteLine("Make sure PostgreSQL is running (docker compose up -d)");
        }
    }
}

app.UseCors();
app.UseStaticFiles();

// API Endpoints
app.MapGet("/", () => Results.Redirect("/index.html"));

app.MapGet("/api/status", (DemoState state, DemoConfiguration config) => new
{
    state.TotalDocuments,
    state.TotalChunks,
    state.LastIndexed,
    StorageBackend = config.StorageBackend,
    StorageInfo = config.StorageInfo,
    EmbeddingModel = config.EmbeddingInfo,
    CompletionModel = config.CompletionModel,
    GraphFeaturesEnabled = config.GraphFeaturesEnabled
});

app.MapGet("/api/health", async (IVectorStore vectorStore) =>
{
    try
    {
        // Simple health check - try to count documents
        var count = await vectorStore.CountAsync();
        return Results.Ok(new { Status = "healthy", DocumentChunks = count });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { Status = "unhealthy", Error = ex.Message });
    }
});

app.MapPost("/api/upload", async (HttpRequest request, IndexingService indexingService, ProcessLogService logService) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data");

    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();

    if (file == null)
        return Results.BadRequest("No file uploaded");

    logService.Info("Upload", $"Starting upload: {file.FileName}", $"Size: {file.Length} bytes");

    try
    {
        var result = await indexingService.IndexFileAsync(file);
        logService.Success("Upload", $"Uploaded: {file.FileName}", $"Document ID: {result.DocumentId}, Chunks: {result.ChunkCount}");
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logService.Error("Upload", $"Upload failed: {file.FileName}", ex.Message);
        throw;
    }
});

app.MapPost("/api/search", async (SearchRequest request, SearchService searchService) =>
{
    var results = await searchService.SearchAsync(request.Query, request.TopK, request.UseReranker);
    return Results.Ok(results);
});

app.MapGet("/api/documents", async (IVectorStore vectorStore, DemoState state) =>
{
    return Results.Ok(state.GetDocumentList());
});

app.MapDelete("/api/documents/{id}", async (string id, IVectorStore vectorStore, DemoState state) =>
{
    await vectorStore.DeleteByDocumentIdAsync(id);
    state.RemoveDocument(id);
    return Results.Ok(new { Message = "Document deleted" });
});

app.MapGet("/api/documents/{id}", async (string id, IVectorStore vectorStore, DemoState state) =>
{
    var chunks = await vectorStore.GetByDocumentIdAsync(id);
    var chunkList = chunks.ToList();

    if (!chunkList.Any())
    {
        return Results.NotFound(new { Error = "Document not found" });
    }

    var docInfo = state.GetDocumentList().FirstOrDefault(d => d.Id == id);
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
            } : null,
            QA = ExtractQAFromMetadata(c.Metadata)
        }).ToList()
    };

    return Results.Ok(result);
});

// MCP-style function endpoint
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

// Rememorize - Update chunk content and regenerate embedding
app.MapPut("/api/chunks/{id}/rememorize", async (
    string id,
    RememorizeRequest request,
    IVectorStore vectorStore,
    FluxIndex.Core.Application.Interfaces.IEmbeddingService embeddingService) =>
{
    var chunk = await vectorStore.GetByIdAsync(id);
    if (chunk == null)
    {
        return Results.NotFound(new { Error = "Chunk not found" });
    }

    // Update content
    chunk.Content = request.Content;
    chunk.TokenCount = request.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    // Update QA if provided
    if (request.QA != null)
    {
        chunk.Metadata ??= new Dictionary<string, object>();
        chunk.Metadata["qa"] = request.QA;
    }

    // Regenerate embedding with new content
    var newEmbedding = await embeddingService.GenerateEmbeddingAsync(request.Content);
    chunk.Embedding = newEmbedding;

    // Update in store
    var success = await vectorStore.UpdateAsync(chunk);
    if (!success)
    {
        return Results.Problem("Failed to update chunk");
    }

    return Results.Ok(new {
        Message = "Chunk rememorized successfully",
        ChunkId = id,
        NewTokenCount = chunk.TokenCount,
        EmbeddingDimensions = newEmbedding.Length
    });
});

// Batch rememorize - Update multiple chunks
app.MapPut("/api/documents/{documentId}/rememorize", async (
    string documentId,
    BatchRememorizeRequest request,
    IVectorStore vectorStore,
    FluxIndex.Core.Application.Interfaces.IEmbeddingService embeddingService) =>
{
    var chunks = await vectorStore.GetByDocumentIdAsync(documentId);
    var chunkList = chunks.ToList();

    if (!chunkList.Any())
    {
        return Results.NotFound(new { Error = "Document not found" });
    }

    var updatedCount = 0;
    var errors = new List<string>();

    foreach (var update in request.Updates)
    {
        var chunk = chunkList.FirstOrDefault(c => c.Id == update.ChunkId);
        if (chunk == null)
        {
            errors.Add($"Chunk {update.ChunkId} not found");
            continue;
        }

        chunk.Content = update.Content;
        chunk.TokenCount = update.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        if (update.QA != null)
        {
            chunk.Metadata ??= new Dictionary<string, object>();
            chunk.Metadata["qa"] = update.QA;
        }

        var newEmbedding = await embeddingService.GenerateEmbeddingAsync(update.Content);
        chunk.Embedding = newEmbedding;

        var success = await vectorStore.UpdateAsync(chunk);
        if (success)
        {
            updatedCount++;
        }
        else
        {
            errors.Add($"Failed to update chunk {update.ChunkId}");
        }
    }

    return Results.Ok(new {
        Message = $"Batch rememorize completed",
        UpdatedCount = updatedCount,
        TotalRequested = request.Updates.Count,
        Errors = errors
    });
});

// Generate QA for a chunk using FluxImprover
app.MapPost("/api/chunks/{id}/generate-qa", async (
    string id,
    IVectorStore vectorStore,
    FluxIndex.Core.Application.Interfaces.IEmbeddingService embeddingService,
    QAGenerationService? qaService) =>
{
    if (qaService == null)
    {
        return Results.BadRequest(new { Error = "QA generation service not available. Please configure OPENAI_API_KEY." });
    }

    var chunk = await vectorStore.GetByIdAsync(id);
    if (chunk == null)
    {
        return Results.NotFound(new { Error = "Chunk not found" });
    }

    try
    {
        // Create a simple IEnrichedChunk adapter for QA generation
        var enrichedChunk = new SimpleEnrichedChunk(chunk);
        var qaPairs = await qaService.GenerateFromChunkAsync(enrichedChunk);

        // Store QA pairs in chunk metadata
        chunk.Metadata ??= new Dictionary<string, object>();
        chunk.Metadata["qa"] = qaPairs.Select(qa => new QAItem(qa.Question, qa.Answer)).ToList();

        // Regenerate embedding to include QA context
        var qaContext = string.Join("\n", qaPairs.Select(qa => $"Q: {qa.Question}\nA: {qa.Answer}"));
        var enrichedContent = chunk.Content + "\n\n" + qaContext;
        chunk.Embedding = await embeddingService.GenerateEmbeddingAsync(enrichedContent);

        await vectorStore.UpdateAsync(chunk);

        return Results.Ok(new {
            Message = "QA generated successfully",
            ChunkId = id,
            QACount = qaPairs.Count,
            QAPairs = qaPairs.Select(qa => new QAItemDto
            {
                Question = qa.Question,
                Answer = qa.Answer
            }).ToList()
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to generate QA: {ex.Message}");
    }
});

// Generate QA for all chunks in a document (using streaming)
app.MapPost("/api/documents/{documentId}/generate-qa", async (
    string documentId,
    IVectorStore vectorStore,
    FluxIndex.Core.Application.Interfaces.IEmbeddingService embeddingService,
    ProcessLogService logService,
    ParallelPipelineExecutor? pipelineExecutor) =>
{
    if (pipelineExecutor == null)
    {
        return Results.BadRequest(new { Error = "QA generation service not available. Please configure OPENAI_API_KEY." });
    }

    var chunks = await vectorStore.GetByDocumentIdAsync(documentId);
    var chunkList = chunks.ToList();

    if (!chunkList.Any())
    {
        return Results.NotFound(new { Error = "Document not found" });
    }

    logService.Info("QA", $"Starting QA generation for document: {documentId}", $"Total chunks: {chunkList.Count}");

    var totalQA = 0;
    var processedChunks = 0;
    var errors = new List<string>();

    // Create enriched chunks for the pipeline
    var enrichedChunks = chunkList.Select(c => new SimpleEnrichedChunk(c)).ToList();

    // Configure pipeline options (QA generation only)
    var pipelineOptions = new PipelineOptions
    {
        EnableEnrichment = false,
        EnableQAGeneration = true,
        EnableEvaluation = false
    };

    var parallelOptions = new ParallelExecutionOptions
    {
        MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4) // Limit parallel API calls
    };

    // Process using streaming for real-time progress updates
    await foreach (var result in pipelineExecutor.ProcessStreamAsync(enrichedChunks, pipelineOptions, parallelOptions))
    {
        processedChunks++;
        var chunk = chunkList.FirstOrDefault(c => c.Id == result.ChunkId);

        if (result.Success && result.GeneratedQAPairs?.Count > 0 && chunk != null)
        {
            try
            {
                // Update chunk with QA pairs
                chunk.Metadata ??= new Dictionary<string, object>();
                chunk.Metadata["qa"] = result.GeneratedQAPairs.Select(qa => new QAItem(qa.Question, qa.Answer)).ToList();

                // Regenerate embedding with QA context
                var qaContext = string.Join("\n", result.GeneratedQAPairs.Select(qa => $"Q: {qa.Question}\nA: {qa.Answer}"));
                var enrichedContent = chunk.Content + "\n\n" + qaContext;
                chunk.Embedding = await embeddingService.GenerateEmbeddingAsync(enrichedContent);

                await vectorStore.UpdateAsync(chunk);
                totalQA += result.GeneratedQAPairs.Count;

                logService.Success("QA", $"[{processedChunks}/{chunkList.Count}] Generated {result.GeneratedQAPairs.Count} Q&A pairs", $"Chunk: {result.ChunkId}");
            }
            catch (Exception ex)
            {
                errors.Add($"Chunk {result.ChunkId}: Update failed - {ex.Message}");
                logService.Error("QA", $"[{processedChunks}/{chunkList.Count}] Failed to update chunk", $"Chunk: {result.ChunkId}, Error: {ex.Message}");
            }
        }
        else if (!result.Success)
        {
            errors.Add($"Chunk {result.ChunkId}: {result.ErrorMessage}");
            logService.Warning("QA", $"[{processedChunks}/{chunkList.Count}] QA generation failed", $"Chunk: {result.ChunkId}, Error: {result.ErrorMessage}");
        }
        else
        {
            logService.Info("QA", $"[{processedChunks}/{chunkList.Count}] No Q&A pairs generated", $"Chunk: {result.ChunkId}");
        }
    }

    logService.Success("QA", $"QA generation completed for document: {documentId}", $"Processed: {processedChunks}/{chunkList.Count}, Total Q&A: {totalQA}, Errors: {errors.Count}");

    return Results.Ok(new {
        Message = "Document QA generation completed",
        DocumentId = documentId,
        ProcessedChunks = processedChunks,
        TotalChunks = chunkList.Count,
        TotalQAPairs = totalQA,
        Errors = errors
    });
});

// Evaluate QA quality for a chunk
app.MapPost("/api/chunks/{chunkId}/evaluate-qa", async (
    string chunkId,
    EvaluateQARequest request,
    IVectorStore vectorStore,
    ProcessLogService logService,
    RAGEvaluationService? evaluationService) =>
{
    if (evaluationService == null)
    {
        return Results.BadRequest(new { Error = "RAG evaluation service not available. Please configure OPENAI_API_KEY." });
    }

    var chunk = await vectorStore.GetByIdAsync(chunkId);
    if (chunk == null)
    {
        return Results.NotFound(new { Error = "Chunk not found" });
    }

    logService.Info("Evaluate", $"Evaluating Q&A quality for chunk: {chunkId}", $"Question: {request.Question[..Math.Min(50, request.Question.Length)]}...");

    try
    {
        var result = await evaluationService.EvaluateAsync(
            chunk.Content,
            request.Question,
            request.Answer);

        logService.Success("Evaluate", $"Evaluation completed for chunk: {chunkId}",
            $"Answerability: {result.Answerability.Score:F2}, Faithfulness: {result.Faithfulness.Score:F2}, Relevancy: {result.Relevancy.Score:F2}");

        return Results.Ok(new EvaluationResponse
        {
            ChunkId = chunkId,
            Question = request.Question,
            Answer = request.Answer,
            Answerability = new MetricResultDto { Score = result.Answerability.Score },
            Faithfulness = new MetricResultDto { Score = result.Faithfulness.Score },
            Relevancy = new MetricResultDto { Score = result.Relevancy.Score },
            OverallScore = result.OverallScore,
            PassesThreshold = result.PassesThreshold(0.7)
        });
    }
    catch (Exception ex)
    {
        logService.Error("Evaluate", $"Evaluation failed for chunk: {chunkId}", ex.Message);
        return Results.Problem($"Failed to evaluate Q&A: {ex.Message}");
    }
});

// Evaluate all QA pairs in a document
app.MapPost("/api/documents/{documentId}/evaluate-qa", async (
    string documentId,
    IVectorStore vectorStore,
    ProcessLogService logService,
    RAGEvaluationService? evaluationService) =>
{
    if (evaluationService == null)
    {
        return Results.BadRequest(new { Error = "RAG evaluation service not available. Please configure OPENAI_API_KEY." });
    }

    var chunks = await vectorStore.GetByDocumentIdAsync(documentId);
    var chunkList = chunks.ToList();

    if (!chunkList.Any())
    {
        return Results.NotFound(new { Error = "Document not found" });
    }

    logService.Info("Evaluate", $"Starting QA evaluation for document: {documentId}", $"Total chunks: {chunkList.Count}");

    var evaluatedCount = 0;
    var totalQAPairs = 0;
    var passedCount = 0;
    var failedCount = 0;
    var errors = new List<string>();
    var chunkEvaluations = new List<ChunkEvaluationSummary>();

    foreach (var chunk in chunkList)
    {
        var qaPairs = ExtractQAFromMetadata(chunk.Metadata);
        if (qaPairs == null || qaPairs.Count == 0) continue;

        foreach (var qa in qaPairs)
        {
            try
            {
                logService.Info("Evaluate", $"[{totalQAPairs + 1}] Evaluating Q&A pair", $"Chunk: {chunk.Id}");

                var result = await evaluationService.EvaluateAsync(chunk.Content, qa.Question, qa.Answer);
                totalQAPairs++;

                if (result.PassesThreshold(0.7))
                {
                    passedCount++;
                    logService.Success("Evaluate", $"Q&A passed (score: {result.OverallScore:F2})", $"Chunk: {chunk.Id}");
                }
                else
                {
                    failedCount++;
                    logService.Warning("Evaluate", $"Q&A below threshold (score: {result.OverallScore:F2})", $"Chunk: {chunk.Id}");
                }

                chunkEvaluations.Add(new ChunkEvaluationSummary
                {
                    ChunkId = chunk.Id,
                    Question = qa.Question,
                    OverallScore = result.OverallScore,
                    Passed = result.PassesThreshold(0.7)
                });
            }
            catch (Exception ex)
            {
                errors.Add($"Chunk {chunk.Id}: {ex.Message}");
                logService.Error("Evaluate", $"Evaluation failed", $"Chunk: {chunk.Id}, Error: {ex.Message}");
            }
        }
        evaluatedCount++;
    }

    logService.Success("Evaluate", $"Evaluation completed for document: {documentId}",
        $"Evaluated: {totalQAPairs} Q&A pairs, Passed: {passedCount}, Failed: {failedCount}");

    return Results.Ok(new DocumentEvaluationResponse
    {
        DocumentId = documentId,
        ChunksEvaluated = evaluatedCount,
        TotalQAPairs = totalQAPairs,
        PassedCount = passedCount,
        FailedCount = failedCount,
        PassRate = totalQAPairs > 0 ? (double)passedCount / totalQAPairs : 0,
        Evaluations = chunkEvaluations,
        Errors = errors
    });
});

// Logs API endpoints
app.MapGet("/api/logs", (ProcessLogService logService, int? limit, string? category, string? level) =>
{
    var logs = logService.GetLogs(limit ?? 100, category, level);
    return Results.Ok(logs);
});

app.MapDelete("/api/logs", (ProcessLogService logService) =>
{
    logService.Clear();
    return Results.Ok(new { Message = "Logs cleared" });
});

Console.WriteLine("========================================");
Console.WriteLine("FluxIndex Demo starting...");
Console.WriteLine($"Storage: {storageInfo}");
Console.WriteLine($"Embedding: {embeddingInfo}");
Console.WriteLine($"Graph Features: {(enableGraphFeatures ? "Enabled" : "Disabled")}");
Console.WriteLine("========================================");

app.Run();

// Helper to determine embedding dimensions
static int GetEmbeddingDimensions(string model, string apiKey)
{
    if (string.IsNullOrEmpty(apiKey))
    {
        // Local models
        return 384; // all-MiniLM-L6-v2 dimension
    }

    return model switch
    {
        "text-embedding-3-small" => 1536,
        "text-embedding-3-large" => 3072,
        "text-embedding-ada-002" => 1536,
        _ => 1536
    };
}

// Helper to extract QA from metadata
static List<QAItemDto>? ExtractQAFromMetadata(Dictionary<string, object>? metadata)
{
    if (metadata == null || !metadata.TryGetValue("qa", out var qaValue))
        return null;

    try
    {
        if (qaValue is System.Text.Json.JsonElement jsonElement)
        {
            var qaList = new List<QAItemDto>();
            foreach (var item in jsonElement.EnumerateArray())
            {
                qaList.Add(new QAItemDto
                {
                    Question = item.GetProperty("Question").GetString() ?? item.GetProperty("question").GetString() ?? "",
                    Answer = item.GetProperty("Answer").GetString() ?? item.GetProperty("answer").GetString() ?? ""
                });
            }
            return qaList.Count > 0 ? qaList : null;
        }

        // Handle List<QAItem> (when stored in memory)
        if (qaValue is IEnumerable<QAItem> qaItems)
        {
            return qaItems
                .Select(q => new QAItemDto
                {
                    Question = q.Question,
                    Answer = q.Answer
                })
                .ToList();
        }

        if (qaValue is IEnumerable<object> qaEnumerable)
        {
            return qaEnumerable
                .Select(q => {
                    if (q is Dictionary<string, object> dict)
                    {
                        return new QAItemDto
                        {
                            Question = dict.GetValueOrDefault("Question")?.ToString() ?? dict.GetValueOrDefault("question")?.ToString() ?? "",
                            Answer = dict.GetValueOrDefault("Answer")?.ToString() ?? dict.GetValueOrDefault("answer")?.ToString() ?? ""
                        };
                    }
                    if (q is QAItem qaItem)
                    {
                        return new QAItemDto
                        {
                            Question = qaItem.Question,
                            Answer = qaItem.Answer
                        };
                    }
                    return null;
                })
                .Where(q => q != null)
                .Cast<QAItemDto>()
                .ToList();
        }
    }
    catch
    {
        // Ignore deserialization errors
    }

    return null;
}

// Configuration class
public class DemoConfiguration
{
    public string StorageBackend { get; set; } = "sqlite";
    public string StorageInfo { get; set; } = "";
    public string EmbeddingInfo { get; set; } = "";
    public string CompletionModel { get; set; } = "";
    public bool GraphFeaturesEnabled { get; set; }
}

// Request/Response models
public record SearchRequest(string Query, int TopK = 10, bool UseReranker = true);
public record McpSearchRequest(string Query, int TopK = 10, bool UseReranker = true, bool IncludeMetadata = true, int MaxTokens = 5000);

// Rememorize request models
public record QAItem(string Question, string Answer);
public record RememorizeRequest(string Content, List<QAItem>? QA = null);
public record ChunkUpdateRequest(string ChunkId, string Content, List<QAItem>? QA = null);
public record BatchRememorizeRequest(List<ChunkUpdateRequest> Updates);

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
    public List<QAItemDto>? QA { get; set; }
}

public class QAItemDto
{
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
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

// Simple adapter to wrap DocumentChunk as IEnrichedChunk for FluxImprover QA generation
public class SimpleEnrichedChunk : FluxIndex.Core.Application.Interfaces.IEnrichedChunk
{
    private readonly FluxIndex.Core.Domain.Entities.DocumentChunk _chunk;
    private readonly SimpleSourceMetadata _source;

    public SimpleEnrichedChunk(FluxIndex.Core.Domain.Entities.DocumentChunk chunk)
    {
        _chunk = chunk;
        _source = new SimpleSourceMetadata(chunk.DocumentId);
    }

    public string Content => _chunk.Content;
    public string ChunkId => _chunk.Id;
    public int ChunkIndex => _chunk.ChunkIndex;
    public IReadOnlyList<string> HeadingPath => _chunk.ChunkMetadata?.SectionTitle != null
        ? new[] { _chunk.ChunkMetadata.SectionTitle }
        : Array.Empty<string>();
    public string? SectionTitle => _chunk.ChunkMetadata?.SectionTitle;
    public int? StartPage => null;
    public int? EndPage => null;
    public double Quality => _chunk.Quality?.Coherence ?? 0.5;
    public double ContextDependency => 0.3;
    public int? TokenCount => _chunk.TokenCount;
    public FluxIndex.Core.Application.Interfaces.ISourceMetadata Source => _source;
}

public class SimpleSourceMetadata : FluxIndex.Core.Application.Interfaces.ISourceMetadata
{
    private readonly string _documentId;

    public SimpleSourceMetadata(string documentId)
    {
        _documentId = documentId;
    }

    public string SourceId => _documentId;
    public string SourceType => "document";
    public string Title => _documentId;
    public string? FilePath => null;
    public string? Url => null;
    public DateTime CreatedAt => DateTime.UtcNow;
    public string Language => "en";
    public double? LanguageConfidence => 0.9;
    public int WordCount => 0;
    public int ChunkCount => 1;
    public int? PageCount => null;
    public DateTime? PublishedAt => null;
    public string? Author => null;
    public IReadOnlyList<string>? Keywords => null;
}

// RAG Evaluation request/response models
public record EvaluateQARequest(string Question, string Answer);

public class MetricResultDto
{
    public double Score { get; set; }
}

public class EvaluationResponse
{
    public string ChunkId { get; set; } = "";
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public MetricResultDto Answerability { get; set; } = new();
    public MetricResultDto Faithfulness { get; set; } = new();
    public MetricResultDto Relevancy { get; set; } = new();
    public double OverallScore { get; set; }
    public bool PassesThreshold { get; set; }
}

public class ChunkEvaluationSummary
{
    public string ChunkId { get; set; } = "";
    public string Question { get; set; } = "";
    public double OverallScore { get; set; }
    public bool Passed { get; set; }
}

public class DocumentEvaluationResponse
{
    public string DocumentId { get; set; } = "";
    public int ChunksEvaluated { get; set; }
    public int TotalQAPairs { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public double PassRate { get; set; }
    public List<ChunkEvaluationSummary> Evaluations { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
