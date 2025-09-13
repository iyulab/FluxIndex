using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

namespace RealQualityTest;

/// <summary>
/// Simple quality test for FluxIndex functionality
/// </summary>
public class SimpleQualityTest
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly IConfiguration _config;
    private readonly TestDatabase _db;
    private readonly Dictionary<string, float[]> _embeddingCache;

    /// <summary>
    /// Initializes a new instance of the SimpleQualityTest class
    /// </summary>
    /// <param name="apiKey">OpenAI API key</param>
    /// <param name="config">Configuration instance</param>
    public SimpleQualityTest(string apiKey, IConfiguration config)
    {
        _apiKey = apiKey;
        _config = config;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");

        _db = new TestDatabase();
        _db.Database.EnsureDeleted();
        _db.Database.EnsureCreated();

        _embeddingCache = new Dictionary<string, float[]>();
    }

    /// <summary>
    /// Runs the quality test asynchronously
    /// </summary>
    public async Task RunTestAsync()
    {
        AnsiConsole.Write(new FigletText("FluxIndex Test").Color(Color.Cyan1));
        
        // 1. Create test documents
        AnsiConsole.MarkupLine("[yellow]Creating test documents...[/]");
        var documents = CreateTestDocuments();
        
        // 2. Chunk documents
        AnsiConsole.MarkupLine("[yellow]Chunking documents...[/]");
        var chunks = ChunkDocuments(documents);
        
        // 3. Generate embeddings
        AnsiConsole.MarkupLine("[yellow]Generating embeddings with OpenAI...[/]");
        await GenerateEmbeddings(chunks);
        
        // 4. Test search
        AnsiConsole.MarkupLine("[yellow]Testing search functionality...[/]");
        await TestSearch();
        
        // 5. Performance test
        AnsiConsole.MarkupLine("[yellow]Running performance tests...[/]");
        await RunPerformanceTest();
        
        // Display results
        DisplayResults();
    }

    private List<TestDocument> CreateTestDocuments()
    {
        return new List<TestDocument>
        {
            new TestDocument
            {
                Title = "Introduction to Machine Learning",
                Content = @"Machine learning is a method of data analysis that automates analytical model building. 
                It is a branch of artificial intelligence based on the idea that systems can learn from data, 
                identify patterns and make decisions with minimal human intervention. Machine learning algorithms 
                build a model based on sample data, known as training data, in order to make predictions or 
                decisions without being explicitly programmed to do so."
            },
            new TestDocument
            {
                Title = "Neural Networks Explained",
                Content = @"A neural network is a series of algorithms that endeavors to recognize underlying 
                relationships in a set of data through a process that mimics the way the human brain operates. 
                Neural networks can adapt to changing input; so the network generates the best possible result 
                without needing to redesign the output criteria. The concept of neural networks, which has its 
                roots in artificial intelligence, is swiftly gaining popularity in the development of trading systems."
            },
            new TestDocument
            {
                Title = "Deep Learning Fundamentals",
                Content = @"Deep learning is a subset of machine learning in artificial intelligence that has networks 
                capable of learning unsupervised from data that is unstructured or unlabeled. Also known as deep 
                neural learning or deep neural network. Deep learning models are inspired by information processing 
                and communication patterns in biological nervous systems yet have various differences from the 
                structural and functional properties of biological brains."
            }
        };
    }

    private List<DocumentChunk> ChunkDocuments(List<TestDocument> documents)
    {
        var chunks = new List<DocumentChunk>();
        int chunkId = 0;

        foreach (var doc in documents)
        {
            var intelligentChunks = CreateIntelligentChunks(doc.Content, doc.Title);
            foreach (var chunk in intelligentChunks)
            {
                chunk.ChunkIndex = chunkId++;
                chunks.Add(chunk);
            }
        }

        return chunks;
    }

    private List<DocumentChunk> CreateIntelligentChunks(string content, string title)
    {
        var chunks = new List<DocumentChunk>();
        var sentences = SplitIntoSentences(content);

        int maxChunkSize = 200;
        int minChunkSize = 100;
        int overlapSentences = 1;

        var currentChunk = new List<string>();
        int currentLength = 0;
        int startPosition = 0;

        for (int i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];

            // If adding this sentence would exceed max size and we have content
            if (currentLength + sentence.Length > maxChunkSize && currentChunk.Count > 0)
            {
                // Create chunk if it meets minimum size
                if (currentLength >= minChunkSize)
                {
                    var chunkContent = string.Join(" ", currentChunk);
                    chunks.Add(new DocumentChunk
                    {
                        DocumentTitle = title,
                        Content = chunkContent.Trim(),
                        StartPosition = startPosition,
                        EndPosition = startPosition + chunkContent.Length
                    });

                    // Start new chunk with overlap
                    var overlapStart = Math.Max(0, currentChunk.Count - overlapSentences);
                    currentChunk = currentChunk.Skip(overlapStart).ToList();
                    currentLength = currentChunk.Sum(s => s.Length + 1) - 1; // -1 for last space
                    startPosition += chunkContent.Length - currentLength;
                }
            }

            currentChunk.Add(sentence);
            currentLength += sentence.Length + (currentChunk.Count > 1 ? 1 : 0); // +1 for space
        }

        // Add final chunk if it has content
        if (currentChunk.Count > 0)
        {
            var chunkContent = string.Join(" ", currentChunk);
            chunks.Add(new DocumentChunk
            {
                DocumentTitle = title,
                Content = chunkContent.Trim(),
                StartPosition = startPosition,
                EndPosition = startPosition + chunkContent.Length
            });
        }

        return chunks;
    }

    private List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var sentenceEnders = new[] { '.', '!', '?', '\n' };

        var currentSentence = new StringBuilder();

        for (int i = 0; i < text.Length; i++)
        {
            currentSentence.Append(text[i]);

            if (sentenceEnders.Contains(text[i]))
            {
                // Look ahead to avoid splitting on abbreviations
                if (i < text.Length - 1 && char.IsLower(text[i + 1]))
                    continue;

                var sentence = currentSentence.ToString().Trim();
                if (!string.IsNullOrEmpty(sentence))
                {
                    sentences.Add(sentence);
                }
                currentSentence.Clear();
            }
        }

        // Add remaining text as final sentence
        if (currentSentence.Length > 0)
        {
            var sentence = currentSentence.ToString().Trim();
            if (!string.IsNullOrEmpty(sentence))
            {
                sentences.Add(sentence);
            }
        }

        return sentences;
    }

    private async Task GenerateEmbeddings(List<DocumentChunk> chunks)
    {
        var table = new Table();
        table.AddColumn("Chunk");
        table.AddColumn("Status");

        // Process in batches for better performance
        int batchSize = 5;
        for (int i = 0; i < chunks.Count; i += batchSize)
        {
            var batch = chunks.Skip(i).Take(batchSize).ToList();
            var embeddings = await GetEmbeddingsBatch(batch.Select(c => c.Content).ToList());

            for (int j = 0; j < batch.Count; j++)
            {
                try
                {
                    if (j < embeddings.Count)
                    {
                        batch[j].Embedding = embeddings[j];
                        _db.Chunks.Add(batch[j]);
                        table.AddRow($"Chunk {batch[j].ChunkIndex}", "[green]✓ Embedded[/]");
                    }
                    else
                    {
                        table.AddRow($"Chunk {batch[j].ChunkIndex}", "[red]✗ Batch failed[/]");
                    }
                }
                catch (Exception ex)
                {
                    table.AddRow($"Chunk {batch[j].ChunkIndex}", $"[red]✗ {ex.Message}[/]");
                }
            }

            await _db.SaveChangesAsync();
        }

        AnsiConsole.Write(table);
    }

    private async Task<float[]> GetEmbedding(string text)
    {
        // Create cache key from text content
        var cacheKey = text.GetHashCode().ToString();

        // Check cache first
        if (_embeddingCache.ContainsKey(cacheKey))
        {
            return _embeddingCache[cacheKey];
        }

        var request = new
        {
            model = "text-embedding-3-small",
            input = text
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("embeddings", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<EmbeddingResponse>(responseJson);

        var embedding = result?.Data?.FirstOrDefault()?.Embedding ?? Array.Empty<float>();

        // Cache the result
        _embeddingCache[cacheKey] = embedding;

        return embedding;
    }

    private async Task<List<float[]>> GetEmbeddingsBatch(List<string> texts)
    {
        var results = new List<float[]>();
        var uncachedTexts = new List<(int index, string text)>();

        // Check cache for each text
        for (int i = 0; i < texts.Count; i++)
        {
            var cacheKey = texts[i].GetHashCode().ToString();
            if (_embeddingCache.ContainsKey(cacheKey))
            {
                results.Add(_embeddingCache[cacheKey]);
            }
            else
            {
                results.Add(null!); // Placeholder
                uncachedTexts.Add((i, texts[i]));
            }
        }

        // Batch process uncached texts
        if (uncachedTexts.Count > 0)
        {
            var request = new
            {
                model = "text-embedding-3-small",
                input = uncachedTexts.Select(t => t.text).ToArray()
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("embeddings", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<EmbeddingResponse>(responseJson);

            // Update results and cache
            for (int i = 0; i < uncachedTexts.Count; i++)
            {
                var (index, text) = uncachedTexts[i];
                var embedding = result?.Data?[i]?.Embedding ?? Array.Empty<float>();

                results[index] = embedding;
                _embeddingCache[text.GetHashCode().ToString()] = embedding;
            }
        }

        return results.Where(r => r != null).ToList();
    }

    private async Task TestSearch()
    {
        var queries = new[]
        {
            "What is machine learning?",
            "How do neural networks work?",
            "Explain deep learning"
        };
        
        var table = new Table();
        table.AddColumn("Query");
        table.AddColumn("Top Result");
        table.AddColumn("Score");
        table.AddColumn("Time (ms)");
        
        foreach (var query in queries)
        {
            var sw = Stopwatch.StartNew();
            
            // Get query embedding
            var queryEmbedding = await GetEmbedding(query);
            
            // Search
            var chunks = await _db.Chunks.ToListAsync();
            var results = chunks
                .Where(c => c.Embedding != null && c.Embedding.Length > 0)
                .Select(c => new
                {
                    Chunk = c,
                    Score = CosineSimilarity(queryEmbedding, c.Embedding!)
                })
                .OrderByDescending(r => r.Score)
                .Take(3)
                .ToList();
            
            sw.Stop();
            
            if (results.Any())
            {
                var top = results.First();
                table.AddRow(
                    query.Length > 30 ? query.Substring(0, 30) + "..." : query,
                    top.Chunk.DocumentTitle ?? "Unknown",
                    top.Score.ToString("F3"),
                    sw.ElapsedMilliseconds.ToString()
                );
            }
        }
        
        AnsiConsole.Write(table);
    }

    private async Task RunPerformanceTest()
    {
        var iterations = 50;
        var times = new List<long>();
        var testQuery = "machine learning";
        
        var queryEmbedding = await GetEmbedding(testQuery);
        
        var progressBar = AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn(),
            });
        
        await progressBar.StartAsync(async ctx =>
        {
            var task = ctx.AddTask("[green]Performance testing[/]", maxValue: iterations);
            
            for (int i = 0; i < iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                
                var chunks = await _db.Chunks.ToListAsync();
                var results = chunks
                    .Where(c => c.Embedding != null)
                    .Select(c => new
                    {
                        Chunk = c,
                        Score = CosineSimilarity(queryEmbedding, c.Embedding!)
                    })
                    .OrderByDescending(r => r.Score)
                    .Take(5)
                    .ToList();
                
                sw.Stop();
                times.Add(sw.ElapsedMilliseconds);
                
                task.Increment(1);
            }
        });
        
        _performanceResults = new PerformanceResults
        {
            AverageTime = times.Average(),
            MinTime = times.Min(),
            MaxTime = times.Max(),
            P95Time = times.OrderBy(t => t).Skip((int)(iterations * 0.95)).First(),
            Iterations = iterations
        };
    }

    private float CosineSimilarity(float[] vec1, float[] vec2)
    {
        if (vec1.Length != vec2.Length) return 0;
        
        float dotProduct = 0;
        float norm1 = 0;
        float norm2 = 0;
        
        for (int i = 0; i < vec1.Length; i++)
        {
            dotProduct += vec1[i] * vec2[i];
            norm1 += vec1[i] * vec1[i];
            norm2 += vec2[i] * vec2[i];
        }
        
        if (norm1 == 0 || norm2 == 0) return 0;
        return dotProduct / (MathF.Sqrt(norm1) * MathF.Sqrt(norm2));
    }

    private PerformanceResults? _performanceResults;
    
    private void DisplayResults()
    {
        AnsiConsole.Write(new Rule("[bold yellow]Test Results Summary[/]"));
        
        if (_performanceResults != null)
        {
            var panel = new Panel(
                $"[cyan]Average Response Time:[/] {_performanceResults.AverageTime:F2} ms\n" +
                $"[cyan]Min Response Time:[/] {_performanceResults.MinTime} ms\n" +
                $"[cyan]Max Response Time:[/] {_performanceResults.MaxTime} ms\n" +
                $"[cyan]P95 Response Time:[/] {_performanceResults.P95Time} ms\n" +
                $"[cyan]Total Iterations:[/] {_performanceResults.Iterations}")
            {
                Header = new PanelHeader("[yellow]Performance Metrics[/]"),
                Border = BoxBorder.Rounded
            };
            
            AnsiConsole.Write(panel);
        }
        
        var stats = new Table();
        stats.AddColumn("Metric");
        stats.AddColumn("Value");
        
        var chunkCount = _db.Chunks.Count();
        var embeddedCount = _db.Chunks.ToList().Count(c => c.Embedding != null && c.Embedding.Length > 0);
        
        stats.AddRow("Total Chunks", chunkCount.ToString());
        stats.AddRow("Embedded Chunks", embeddedCount.ToString());
        stats.AddRow("Embedding Model", "text-embedding-3-small");
        stats.AddRow("Vector Dimensions", "1536");
        
        AnsiConsole.Write(stats);
    }
}

// Database Models
/// <summary>
/// Test database context for quality testing
/// </summary>
public class TestDatabase : DbContext
{
    /// <summary>
    /// Gets or sets the document chunks
    /// </summary>
    public DbSet<DocumentChunk> Chunks { get; set; }
    
    /// <summary>
    /// Configures the database context
    /// </summary>
    /// <param name="optionsBuilder">Options builder</param>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite("Data Source=quality_test.db");
    }
    
    /// <summary>
    /// Configures the database model
    /// </summary>
    /// <param name="modelBuilder">Model builder</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentChunk>()
            .Property(e => e.Embedding)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<float[]>(v, (JsonSerializerOptions?)null));
    }
}

/// <summary>
/// Document chunk entity for testing
/// </summary>
public class DocumentChunk
{
    /// <summary>
    /// Gets or sets the chunk ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the document title
    /// </summary>
    public string? DocumentTitle { get; set; }

    /// <summary>
    /// Gets or sets the content
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the chunk index
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// Gets or sets the start position
    /// </summary>
    public int StartPosition { get; set; }

    /// <summary>
    /// Gets or sets the end position
    /// </summary>
    public int EndPosition { get; set; }

    /// <summary>
    /// Gets or sets the embedding data
    /// </summary>
    public float[]? Embedding { get; set; }
}

/// <summary>
/// Test document class
/// </summary>
public class TestDocument
{
    /// <summary>
    /// Gets or sets the document title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the document content
    /// </summary>
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Embedding response from API
/// </summary>
public class EmbeddingResponse
{
    /// <summary>
    /// Gets or sets the embedding data
    /// </summary>
    [JsonPropertyName("data")]
    public List<EmbeddingData>? Data { get; set; }
}

/// <summary>
/// Embedding data item
/// </summary>
public class EmbeddingData
{
    /// <summary>
    /// Gets or sets the embedding vector
    /// </summary>
    [JsonPropertyName("embedding")]
    public float[]? Embedding { get; set; }
}

/// <summary>
/// Performance test results
/// </summary>
public class PerformanceResults
{
    /// <summary>
    /// Gets or sets the average time
    /// </summary>
    public double AverageTime { get; set; }

    /// <summary>
    /// Gets or sets the minimum time
    /// </summary>
    public long MinTime { get; set; }

    /// <summary>
    /// Gets or sets the maximum time
    /// </summary>
    public long MaxTime { get; set; }

    /// <summary>
    /// Gets or sets the 95th percentile time
    /// </summary>
    public long P95Time { get; set; }

    /// <summary>
    /// Gets or sets the number of iterations
    /// </summary>
    public int Iterations { get; set; }
}