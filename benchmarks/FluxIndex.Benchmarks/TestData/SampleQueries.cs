using System;
using System.Collections.Generic;

namespace FluxIndex.Benchmarks.TestData;

/// <summary>
/// 벤치마크 테스트용 샘플 쿼리 생성
/// </summary>
public static class SampleQueries
{
    /// <summary>
    /// 단순 키워드 검색 쿼리 (10-30 tokens)
    /// </summary>
    public static List<string> GetSimpleKeywordQueries() => new()
    {
        "machine learning basics",
        "neural network architecture",
        "deep learning tutorial",
        "natural language processing",
        "computer vision algorithms",
        "reinforcement learning examples",
        "data preprocessing techniques",
        "model training strategies",
        "transfer learning methods",
        "optimization algorithms comparison"
    };

    /// <summary>
    /// 복잡한 의미론적 검색 쿼리 (50-150 tokens)
    /// </summary>
    public static List<string> GetComplexSemanticQueries() => new()
    {
        @"Explain the architectural differences between transformer models and recurrent neural networks,
          particularly focusing on attention mechanisms, positional encodings, and their impact on
          sequential data processing performance and scalability for natural language tasks.",

        @"Compare various optimization algorithms used in deep learning such as SGD, Adam, RMSprop, and AdaGrad.
          Discuss their convergence characteristics, computational efficiency, memory requirements,
          and practical considerations when training large-scale models with millions of parameters.",

        @"Describe the process of fine-tuning pre-trained language models for domain-specific tasks.
          Include details about transfer learning strategies, layer freezing techniques, learning rate
          scheduling, and how to prevent catastrophic forgetting while adapting to new domains.",

        @"What are the key challenges in deploying machine learning models to production environments?
          Address topics like model versioning, A/B testing, monitoring performance drift, handling
          data distribution shifts, and maintaining low latency inference at scale.",

        @"Explain the concept of few-shot and zero-shot learning in the context of modern language models.
          How do techniques like prompt engineering, in-context learning, and chain-of-thought reasoning
          enable models to perform tasks without explicit fine-tuning?",

        @"Compare different approaches to handling imbalanced datasets in machine learning, including
          oversampling, undersampling, SMOTE, cost-sensitive learning, and focal loss.
          Discuss their trade-offs and when each method is most appropriate.",

        @"Describe advanced regularization techniques for preventing overfitting in deep neural networks,
          such as dropout, batch normalization, weight decay, early stopping, and data augmentation.
          Explain how each technique works and their effectiveness in different scenarios.",

        @"What are the latest developments in multi-modal learning that combine vision, language, and
          other modalities? Discuss architectures like CLIP, DALL-E, and Flamingo, and their applications
          in cross-modal retrieval, generation, and understanding.",

        @"Explain the principles of federated learning and privacy-preserving machine learning.
          Cover topics like differential privacy, secure multi-party computation, homomorphic encryption,
          and their practical implications for training models on sensitive data.",

        @"Describe the evolution of word embeddings from Word2Vec and GloVe to contextual embeddings
          like ELMo and BERT. Explain how these representations capture semantic and syntactic information
          differently and their impact on downstream NLP tasks."
    };

    /// <summary>
    /// 혼합 쿼리 (키워드 + 의미론적, 30-80 tokens)
    /// </summary>
    public static List<string> GetHybridQueries() => new()
    {
        @"transformer attention mechanism: Explain how self-attention computes relationships
          between tokens and why it's more effective than RNNs for long sequences.",

        @"bert pre-training objectives: Describe masked language modeling and next sentence prediction,
          and how these tasks help the model learn contextual representations.",

        @"GPT architecture differences: Compare GPT-2, GPT-3, and GPT-4 in terms of model size,
          training data, and capabilities like few-shot learning and reasoning.",

        @"convolutional neural networks image classification: Explain how CNNs extract hierarchical
          features through convolution and pooling layers for visual recognition tasks.",

        @"gradient descent optimization: Discuss stochastic gradient descent, mini-batch training,
          momentum, and adaptive learning rate methods like Adam and RMSprop.",

        @"recurrent neural networks sequence modeling: Describe how RNNs process sequential data,
          the vanishing gradient problem, and solutions like LSTM and GRU architectures.",

        @"autoencoders representation learning: Explain how autoencoders learn compressed representations
          through encoder-decoder architecture and their applications in dimensionality reduction.",

        @"generative adversarial networks training: Describe the adversarial training process between
          generator and discriminator, and common techniques to stabilize GAN training.",

        @"batch normalization deep learning: Explain how batch norm normalizes activations,
          reduces internal covariate shift, and improves training stability and speed.",

        @"residual networks skip connections: Describe how residual connections enable training
          very deep networks by addressing the degradation problem in deep architectures."
    };

    /// <summary>
    /// 모든 쿼리 타입 반환 (벤치마크 전체 실행용)
    /// </summary>
    public static List<string> GetAllQueries()
    {
        var allQueries = new List<string>();
        allQueries.AddRange(GetSimpleKeywordQueries());
        allQueries.AddRange(GetComplexSemanticQueries());
        allQueries.AddRange(GetHybridQueries());
        return allQueries;
    }

    /// <summary>
    /// 랜덤 쿼리 선택
    /// </summary>
    public static string GetRandomQuery(Random? random = null)
    {
        random ??= new Random();
        var allQueries = GetAllQueries();
        return allQueries[random.Next(allQueries.Count)];
    }
}
