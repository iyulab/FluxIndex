using FluxIndex.Application.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.SDK.Services;

/// <summary>
/// 테스트용 모의 임베딩 서비스
/// </summary>
public class MockEmbeddingService : IEmbeddingService
{
    private readonly int _dimension;
    private readonly Random _random = new Random();

    public MockEmbeddingService(int dimension = 384)
    {
        _dimension = dimension;
    }

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        // Generate deterministic embedding based on text hash
        var hash = text.GetHashCode();
        var random = new Random(hash);
        
        var embedding = new float[_dimension];
        for (int i = 0; i < _dimension; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1); // Range -1 to 1
        }
        
        // Normalize the vector
        var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < _dimension; i++)
            {
                embedding[i] /= magnitude;
            }
        }
        
        return Task.FromResult(embedding);
    }

    public async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var embeddings = new List<float[]>();
        foreach (var text in texts)
        {
            var embedding = await GenerateEmbeddingAsync(text, cancellationToken);
            embeddings.Add(embedding);
        }
        return embeddings;
    }

    public int GetEmbeddingDimension()
    {
        return _dimension;
    }

    public string GetModelName()
    {
        return "mock-embedding-model";
    }

    public int GetMaxTokens()
    {
        return 8192;
    }

    public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default)
    {
        // Simple estimation: ~4 characters per token
        return Task.FromResult(text.Length / 4);
    }
}