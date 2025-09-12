using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FluxIndex.Core.Application.Services.Reranking;

/// <summary>
/// ONNX-based Cross-Encoder reranker for high-quality relevance scoring
/// Uses ms-marco-MiniLM-L6 or similar cross-encoder models
/// </summary>
public class OnnxCrossEncoderReranker : IReranker, IDisposable
{
    private readonly ILogger<OnnxCrossEncoderReranker> _logger;
    private readonly OnnxCrossEncoderOptions _options;
    private InferenceSession? _session;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private bool _disposed;

    // Model input/output names (standard for most cross-encoder models)
    private const string InputIdsName = "input_ids";
    private const string AttentionMaskName = "attention_mask";
    private const string TokenTypeIdsName = "token_type_ids";
    private const string OutputName = "logits";

    public OnnxCrossEncoderReranker(
        OnnxCrossEncoderOptions options,
        ILogger<OnnxCrossEncoderReranker> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initialize the ONNX model session
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (_session != null)
                return;

            _logger.LogInformation("Initializing ONNX Cross-Encoder model from {ModelPath}", _options.ModelPath);

            if (!File.Exists(_options.ModelPath))
            {
                throw new FileNotFoundException($"ONNX model not found at {_options.ModelPath}");
            }

            // Configure session options
            var sessionOptions = new SessionOptions();
            
            if (_options.UseGpu && _options.GpuDeviceId.HasValue)
            {
                sessionOptions.AppendExecutionProvider_CUDA(_options.GpuDeviceId.Value);
                _logger.LogInformation("Using GPU device {DeviceId} for inference", _options.GpuDeviceId.Value);
            }
            else
            {
                sessionOptions.AppendExecutionProvider_CPU(_options.CpuThreads);
                _logger.LogInformation("Using CPU with {Threads} threads for inference", _options.CpuThreads);
            }

            // Set optimization level
            sessionOptions.GraphOptimizationLevel = _options.OptimizationLevel switch
            {
                OnnxOptimizationLevel.Basic => GraphOptimizationLevel.ORT_ENABLE_BASIC,
                OnnxOptimizationLevel.Extended => GraphOptimizationLevel.ORT_ENABLE_EXTENDED,
                OnnxOptimizationLevel.All => GraphOptimizationLevel.ORT_ENABLE_ALL,
                _ => GraphOptimizationLevel.ORT_ENABLE_EXTENDED
            };

            // Load the model
            _session = new InferenceSession(_options.ModelPath, sessionOptions);

            // Log model information
            LogModelInfo();

            _logger.LogInformation("ONNX Cross-Encoder model initialized successfully");
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task<IEnumerable<Document>> RerankAsync(
        string query,
        IEnumerable<Document> documents,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        // Ensure model is initialized
        if (_session == null)
        {
            await InitializeAsync(cancellationToken);
        }

        var docList = documents.ToList();
        if (!docList.Any())
            return docList;

        _logger.LogDebug("Reranking {Count} documents with ONNX Cross-Encoder", docList.Count);

        // Score all documents
        var scores = await ScoreDocumentsAsync(query, docList, cancellationToken);

        // Sort by score and return top K
        var rerankedDocs = docList
            .Zip(scores, (doc, score) => new { Document = doc, Score = score })
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select((x, index) =>
            {
                // Update document metadata with reranking info
                x.Document.Metadata ??= new();
                x.Document.Metadata["onnx_rerank_score"] = x.Score.ToString("F4");
                x.Document.Metadata["onnx_rerank_position"] = (index + 1).ToString();
                x.Document.Score = x.Score; // Update relevance score
                return x.Document;
            })
            .ToList();

        _logger.LogDebug("Reranking complete. Top score: {TopScore:F4}, Bottom score: {BottomScore:F4}",
            rerankedDocs.FirstOrDefault()?.Score ?? 0,
            rerankedDocs.LastOrDefault()?.Score ?? 0);

        return rerankedDocs;
    }

    /// <summary>
    /// Score documents using the cross-encoder model
    /// </summary>
    private async Task<float[]> ScoreDocumentsAsync(
        string query,
        List<Document> documents,
        CancellationToken cancellationToken)
    {
        var scores = new List<float>();
        
        // Process in batches for efficiency
        for (int i = 0; i < documents.Count; i += _options.BatchSize)
        {
            var batch = documents.Skip(i).Take(_options.BatchSize).ToList();
            var batchScores = await ScoreBatchAsync(query, batch, cancellationToken);
            scores.AddRange(batchScores);
        }

        return scores.ToArray();
    }

    /// <summary>
    /// Score a batch of documents
    /// </summary>
    private async Task<float[]> ScoreBatchAsync(
        string query,
        List<Document> documents,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            // Tokenize query-document pairs
            var inputs = TokenizeBatch(query, documents);

            // Run inference
            using var results = _session!.Run(inputs);
            
            // Extract scores from logits
            var output = results.First(r => r.Name == OutputName);
            var logits = output.AsTensor<float>();

            // Convert logits to scores (apply sigmoid for probability)
            var scores = new float[documents.Count];
            for (int i = 0; i < documents.Count; i++)
            {
                // For binary classification models, take the positive class logit
                var logit = logits[i, _options.PositiveClassIndex];
                scores[i] = Sigmoid(logit);
            }

            return scores;
        }, cancellationToken);
    }

    /// <summary>
    /// Tokenize a batch of query-document pairs
    /// </summary>
    private List<NamedOnnxValue> TokenizeBatch(string query, List<Document> documents)
    {
        var batchSize = documents.Count;
        var maxLength = _options.MaxSequenceLength;

        // Initialize tensors
        var inputIds = new DenseTensor<long>(new[] { batchSize, maxLength });
        var attentionMask = new DenseTensor<long>(new[] { batchSize, maxLength });
        var tokenTypeIds = new DenseTensor<long>(new[] { batchSize, maxLength });

        // Simple tokenization (in production, use proper tokenizer like BertTokenizer)
        for (int i = 0; i < batchSize; i++)
        {
            var text = documents[i].Content ?? "";
            var combined = $"{query} [SEP] {text}";
            
            // Simplified tokenization - in production, use proper BERT tokenizer
            var tokens = SimpleTokenize(combined, maxLength);
            
            for (int j = 0; j < tokens.Length; j++)
            {
                inputIds[i, j] = tokens[j];
                attentionMask[i, j] = 1; // Real tokens have mask = 1
                tokenTypeIds[i, j] = j < tokens.Length / 2 ? 0 : 1; // Query = 0, Doc = 1
            }
        }

        return new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(InputIdsName, inputIds),
            NamedOnnxValue.CreateFromTensor(AttentionMaskName, attentionMask),
            NamedOnnxValue.CreateFromTensor(TokenTypeIdsName, tokenTypeIds)
        };
    }

    /// <summary>
    /// Simple tokenization placeholder - replace with proper tokenizer
    /// </summary>
    private long[] SimpleTokenize(string text, int maxLength)
    {
        // This is a placeholder - in production, use proper BERT tokenizer
        // For now, we'll use simple character-based tokenization
        var tokens = new long[maxLength];
        
        // [CLS] token
        tokens[0] = 101;
        
        var chars = text.Take(maxLength - 2).ToArray();
        for (int i = 0; i < chars.Length && i < maxLength - 2; i++)
        {
            // Simple character to token ID mapping
            tokens[i + 1] = (long)(chars[i] % 30000) + 1000;
        }
        
        // [SEP] token
        tokens[Math.Min(chars.Length + 1, maxLength - 1)] = 102;
        
        return tokens;
    }

    /// <summary>
    /// Sigmoid activation function
    /// </summary>
    private static float Sigmoid(float x)
    {
        return 1.0f / (1.0f + MathF.Exp(-x));
    }

    /// <summary>
    /// Log model information
    /// </summary>
    private void LogModelInfo()
    {
        if (_session == null)
            return;

        var inputMeta = _session.InputMetadata;
        var outputMeta = _session.OutputMetadata;

        _logger.LogInformation("Model Inputs:");
        foreach (var input in inputMeta)
        {
            _logger.LogInformation("  - {Name}: {Type} {Dimensions}",
                input.Key,
                input.Value.ElementType,
                string.Join("x", input.Value.Dimensions));
        }

        _logger.LogInformation("Model Outputs:");
        foreach (var output in outputMeta)
        {
            _logger.LogInformation("  - {Name}: {Type} {Dimensions}",
                output.Key,
                output.Value.ElementType,
                string.Join("x", output.Value.Dimensions));
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _session?.Dispose();
        _sessionLock?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Configuration options for ONNX Cross-Encoder
/// </summary>
public class OnnxCrossEncoderOptions
{
    /// <summary>
    /// Path to the ONNX model file
    /// </summary>
    public string ModelPath { get; set; } = "models/ms-marco-MiniLM-L6-v2.onnx";

    /// <summary>
    /// Maximum sequence length for input
    /// </summary>
    public int MaxSequenceLength { get; set; } = 512;

    /// <summary>
    /// Batch size for inference
    /// </summary>
    public int BatchSize { get; set; } = 32;

    /// <summary>
    /// Index of positive class in output logits
    /// </summary>
    public int PositiveClassIndex { get; set; } = 1;

    /// <summary>
    /// Use GPU for inference
    /// </summary>
    public bool UseGpu { get; set; } = false;

    /// <summary>
    /// GPU device ID (if using GPU)
    /// </summary>
    public int? GpuDeviceId { get; set; }

    /// <summary>
    /// Number of CPU threads for inference
    /// </summary>
    public int CpuThreads { get; set; } = 4;

    /// <summary>
    /// Graph optimization level
    /// </summary>
    public OnnxOptimizationLevel OptimizationLevel { get; set; } = OnnxOptimizationLevel.Extended;
}

/// <summary>
/// ONNX optimization levels
/// </summary>
public enum OnnxOptimizationLevel
{
    None,
    Basic,
    Extended,
    All
}