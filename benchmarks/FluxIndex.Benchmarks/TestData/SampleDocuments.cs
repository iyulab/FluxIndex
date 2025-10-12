using System;
using System.Collections.Generic;
using System.Linq;
using FluxIndex.Domain.Models;

namespace FluxIndex.Benchmarks.TestData;

/// <summary>
/// 벤치마크 테스트용 샘플 문서 및 청크 생성
/// </summary>
public static class SampleDocuments
{
    private static readonly string[] Topics = new[]
    {
        "machine learning", "deep learning", "neural networks", "natural language processing",
        "computer vision", "reinforcement learning", "transfer learning", "optimization",
        "data preprocessing", "model evaluation", "feature engineering", "embeddings"
    };

    private static readonly string[] ContentTemplates = new[]
    {
        "Introduction to {0}: This section provides an overview of fundamental concepts and principles.",
        "Advanced techniques in {0} include various methodologies and best practices for practitioners.",
        "The history of {0} dates back to early research in artificial intelligence and computational theory.",
        "Practical applications of {0} span across industries including healthcare, finance, and technology.",
        "Recent developments in {0} have led to breakthrough performance on challenging benchmarks.",
        "Theoretical foundations of {0} are rooted in mathematics, statistics, and computer science.",
        "Common challenges in {0} include scalability, generalization, and interpretability of models.",
        "Future directions for {0} research focus on efficiency, robustness, and ethical considerations.",
        "Implementation strategies for {0} require careful consideration of architecture and hyperparameters.",
        "Evaluation metrics for {0} help assess model quality and guide development decisions."
    };

    /// <summary>
    /// 지정된 개수의 DocumentChunk 생성
    /// </summary>
    public static List<DocumentChunk> GenerateChunks(int count, int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        var chunks = new List<DocumentChunk>();

        for (int i = 0; i < count; i++)
        {
            var documentId = $"doc_{i / 10}"; // 10 chunks per document
            var chunkIndex = i % 10;
            var topic = Topics[random.Next(Topics.Length)];
            var template = ContentTemplates[random.Next(ContentTemplates.Length)];
            var content = string.Format(template, topic);

            // 긴 컨텐츠 생성 (50-200 tokens)
            var additionalContent = GenerateAdditionalContent(random, 50 + random.Next(150));
            content += " " + additionalContent;

            chunks.Add(new DocumentChunk
            {
                Id = $"chunk_{i}",
                DocumentId = documentId,
                ChunkIndex = chunkIndex,
                Content = content,
                TokenCount = content.Split(' ').Length,
                Embedding = GenerateRandomEmbedding(384, random), // 384 dimensions (common for sentence transformers)
                Metadata = new Dictionary<string, object>
                {
                    { "topic", topic },
                    { "source", "benchmark_test" },
                    { "timestamp", DateTime.UtcNow.ToString("O") }
                }
            });
        }

        return chunks;
    }

    /// <summary>
    /// 작은 배치 생성 (100 chunks)
    /// </summary>
    public static List<DocumentChunk> GenerateSmallBatch() => GenerateChunks(100, seed: 12345);

    /// <summary>
    /// 중간 배치 생성 (1,000 chunks)
    /// </summary>
    public static List<DocumentChunk> GenerateMediumBatch() => GenerateChunks(1000, seed: 12345);

    /// <summary>
    /// 큰 배치 생성 (10,000 chunks)
    /// </summary>
    public static List<DocumentChunk> GenerateLargeBatch() => GenerateChunks(10000, seed: 12345);

    /// <summary>
    /// 랜덤 임베딩 벡터 생성
    /// </summary>
    private static float[] GenerateRandomEmbedding(int dimensions, Random random)
    {
        var embedding = new float[dimensions];
        for (int i = 0; i < dimensions; i++)
        {
            // 정규 분포 근사 (Box-Muller 변환)
            var u1 = random.NextDouble();
            var u2 = random.NextDouble();
            var randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            embedding[i] = (float)(randStdNormal * 0.1); // 표준편차 0.1
        }

        // L2 정규화
        var norm = Math.Sqrt(embedding.Sum(x => x * x));
        for (int i = 0; i < dimensions; i++)
        {
            embedding[i] /= (float)norm;
        }

        return embedding;
    }

    /// <summary>
    /// 추가 컨텐츠 생성 (자연스러운 텍스트)
    /// </summary>
    private static string GenerateAdditionalContent(Random random, int wordCount)
    {
        var words = new[]
        {
            "algorithm", "architecture", "attention", "backpropagation", "batch", "bias", "classification",
            "clustering", "computation", "convergence", "dataset", "dimension", "distribution", "embedding",
            "encoder", "epoch", "evaluation", "feature", "gradient", "hidden", "hyperparameter", "inference",
            "iteration", "layer", "learning", "loss", "matrix", "model", "network", "neuron", "normalization",
            "optimization", "overfitting", "parameter", "performance", "prediction", "preprocessing", "regularization",
            "representation", "sampling", "scaling", "sequence", "supervised", "tensor", "training", "transform",
            "underfitting", "unsupervised", "validation", "vector", "weight"
        };

        var sentences = new List<string>();
        var currentSentence = new List<string>();

        for (int i = 0; i < wordCount; i++)
        {
            currentSentence.Add(words[random.Next(words.Length)]);

            // 8-15 단어마다 문장 종료
            if (currentSentence.Count >= 8 + random.Next(8))
            {
                sentences.Add(string.Join(" ", currentSentence) + ".");
                currentSentence.Clear();
            }
        }

        // 남은 단어가 있으면 문장 완성
        if (currentSentence.Count > 0)
        {
            sentences.Add(string.Join(" ", currentSentence) + ".");
        }

        return string.Join(" ", sentences);
    }

    /// <summary>
    /// 특정 토픽의 청크만 필터링
    /// </summary>
    public static List<DocumentChunk> FilterByTopic(List<DocumentChunk> chunks, string topic)
    {
        return chunks.Where(c => c.Metadata.ContainsKey("topic") &&
                                 c.Metadata["topic"].ToString()!.Contains(topic, StringComparison.OrdinalIgnoreCase))
                     .ToList();
    }
}
