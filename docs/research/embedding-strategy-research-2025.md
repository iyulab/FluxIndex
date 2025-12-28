# Embedding Model Selection by Chunking/Enrichment Strategy

## Research Summary (December 2025)

This document provides recommendations for selecting optimal embedding models based on chunking and enrichment strategies in RAG systems.

---

## 1. Strategy Overview

### 1.1 Contextual Retrieval (Anthropic Style)

**How it works:**
- LLM generates 50-100 token context header for each chunk
- Context is prepended to chunk before embedding
- Combines with BM25 for hybrid search

**Performance gains:**
- Contextual Embeddings alone: 35% reduction in retrieval failures
- + Contextual BM25: 49% reduction
- + Reranking: 67% reduction (5.7% → 1.9%)

**Cost:** ~$1.02 per million tokens with prompt caching

### 1.2 Late Chunking (Jina AI Style)

**How it works:**
- Embed entire document first using long-context model
- Apply mean pooling on token embeddings per chunk boundary
- Preserves cross-chunk contextual information

**Requirements:**
- Long-context embedding model (8K+ tokens)
- Mean pooling support
- ~30 lines of code modification

### 1.3 Standard Chunking

**How it works:**
- Chunk document first
- Embed each chunk independently
- Simple but loses inter-chunk context

---

## 2. Recommended Models by Strategy

### 2.1 Contextual Retrieval Models

| Model | Dimensions | Context | Best For | Notes |
|-------|------------|---------|----------|-------|
| **text-embedding-3-large** | 3072 | 8K | Production, high accuracy | OpenAI, best quality |
| **text-embedding-3-small** | 1536 | 8K | Cost-effective production | OpenAI, good balance |
| **Voyage-3-large** | 1024 | 32K | Best-in-class retrieval | Top MTEB scores |
| **BGE-M3** | 1024 | 8K | Multilingual, hybrid | Open source, versatile |

**Why these models:**
- Contextual retrieval adds 50-100 tokens per chunk
- Models must handle slightly longer input effectively
- Quality matters more than speed (context already adds latency)

### 2.2 Late Chunking Models

| Model | Dimensions | Context | Best For | Notes |
|-------|------------|---------|----------|-------|
| **jina-embeddings-v3** | 1024 | 8K | Native late chunking support | API flag available |
| **jina-embeddings-v2-base-en** | 768 | 8K | English documents | Well-tested |
| **BGE-M3** | 1024 | 8K | Multilingual late chunking | Open source |
| **Qwen3-Embedding-8B** | 4096 | 32K | Maximum context | Apache 2.0 |

**Requirements for Late Chunking:**
- ✅ Long context window (8K+ tokens)
- ✅ Mean pooling support
- ✅ Token-level embeddings accessible
- ❌ CLS pooling only models (not suitable)

### 2.3 Standard Chunking Models

| Model | Dimensions | Context | Best For | Notes |
|-------|------------|---------|----------|-------|
| **all-MiniLM-L6-v2** | 384 | 512 | Development, testing | Fast, lightweight |
| **text-embedding-3-small** | 1536 | 8K | General production | Good balance |
| **nomic-embed-text** | 768 | 8K | Local deployment | Open source |
| **mxbai-embed-large** | 1024 | 512 | High quality local | Open source |

---

## 3. Language-Specific Recommendations

### 3.1 Korean / Multilingual

| Model | Korean Performance | Languages | Notes |
|-------|-------------------|-----------|-------|
| **BGE-M3** | 97.6% accuracy | 100+ | Best for Korean |
| **multilingual-e5-large** | 89.7% accuracy | 100+ | Good alternative |
| **BGE-m3-ko** | Optimized | Korean focus | Fine-tuned variant |
| **KU-HIAI-ONTHEIT-large-v1** | High | Korean | Domain-specific |

**Key findings:**
- BGE-M3 outperforms multilingual-e5 for Korean
- Native Korean models outperform translated benchmarks
- Long document handling is crucial for Korean (longer average sentences)

### 3.2 Code Embeddings

| Model | Dimensions | Languages | Performance |
|-------|------------|-----------|-------------|
| **voyage-code-3** | 2048 | 300+ | SOTA, 13.8% better than OpenAI |
| **Qodo-Embed-1-7B** | - | Multi | 71.5 CoIR score |
| **jina-code-embeddings** | 768 | Multi | Efficient, autoregressive |
| **CodeSage-large** | 1024 | Multi | Good baseline |

**voyage-code-3 features:**
- Matryoshka support (2048, 1024, 512, 256 dims)
- Quantization options (int8, binary)
- 32K context length

---

## 4. Dimension Trade-offs (Matryoshka)

### 4.1 Understanding Matryoshka Representation Learning

```
Full embedding:    [d1, d2, d3, d4, ... d768, ... d1536, ... d3072]
                    ↓   ↓   ↓   ↓
Truncated 768:     [d1, d2, d3, d4, ... d768]
Truncated 384:     [d1, d2, d3, d4, ... d384]
Truncated 128:     [d1, d2, ... d128]
```

### 4.2 Performance vs Storage Trade-offs

| Dimensions | Storage (per vector) | Retrieval Quality | Use Case |
|------------|---------------------|-------------------|----------|
| 3072 | 12 KB | 100% baseline | Maximum accuracy |
| 1536 | 6 KB | ~99% | Production default |
| 1024 | 4 KB | ~98% | Good balance |
| 768 | 3 KB | ~96% | Cost-effective |
| 512 | 2 KB | ~94% | High-volume |
| 256 | 1 KB | ~90% | Edge/mobile |
| 128 | 0.5 KB | ~85% | Extremely constrained |

### 4.3 Models with Native Matryoshka Support

- **text-embedding-3-large/small** (OpenAI)
- **voyage-code-3** (Voyage AI)
- **EmbeddingGemma** (Google)
- **nomic-embed-text-v1.5** (Nomic)
- **jina-embeddings-v3** (Jina AI)

---

## 5. Strategy Selection Matrix

### 5.1 By Document Type

| Document Type | Recommended Strategy | Model Choice |
|---------------|---------------------|--------------|
| Technical docs | Contextual + BM25 | text-embedding-3-large |
| Legal/contracts | Late Chunking | jina-embeddings-v3 |
| Code repositories | Standard + Code model | voyage-code-3 |
| Korean documents | Contextual + Hybrid | BGE-M3 |
| Long narratives | Late Chunking | Qwen3-Embedding-8B |
| Mixed content | Contextual + BM25 | BGE-M3 |

### 5.2 By Use Case Priority

| Priority | Strategy | Model | Rationale |
|----------|----------|-------|-----------|
| **Accuracy first** | Contextual + Rerank | Voyage-3-large | 67% failure reduction |
| **Cost first** | Standard | all-MiniLM-L6-v2 | Free, fast |
| **Multilingual** | Late Chunking | BGE-M3 | 100+ languages |
| **Code search** | Standard | voyage-code-3 | SOTA code retrieval |
| **Offline/local** | Standard | nomic-embed-text | No API needed |
| **Korean focus** | Contextual + Hybrid | BGE-M3 / BGE-m3-ko | Best Korean performance |

---

## 6. Implementation Recommendations for FluxIndex

### 6.1 Default Configuration

```yaml
# Recommended defaults for FluxIndex.Stack
embedding:
  # Fallback chain (first available wins)
  primary: "text-embedding-3-small"  # If OpenAI API configured
  fallback: "BGE-M3"                 # If local GPU available
  offline: "all-MiniLM-L6-v2"        # Always available (CPU)

  # Strategy defaults
  contextual_enrichment: true        # Enable for production
  hybrid_search: true                # BM25 + Vector
  reranking: true                    # Cross-encoder rerank
```

### 6.2 Collection-Level Configuration

```csharp
// Proposed schema extension
public class CollectionSettings
{
    // Existing fields...

    // New embedding strategy fields
    public string EmbeddingModel { get; set; }
    public string EmbeddingStrategy { get; set; } // Standard, Contextual, LateChunking
    public int EmbeddingDimensions { get; set; }  // For Matryoshka truncation
    public bool EnableHybridSearch { get; set; }
    public bool EnableReranking { get; set; }
}
```

### 6.3 Automatic Strategy Selection

```
Document Analysis
       │
       ▼
┌──────────────────┐
│ Detect Language  │
└────────┬─────────┘
         │
    ┌────┴────┐
    │         │
    ▼         ▼
 Korean    English/Other
    │         │
    │    ┌────┴────┐
    │    │         │
    ▼    ▼         ▼
 BGE-M3  Code?    Text
    │    │         │
    │    ▼         ▼
    │  voyage-  text-emb-3
    │  code-3   small/large
    │    │         │
    └────┴────┬────┘
              │
              ▼
    ┌─────────────────┐
    │ Select Strategy │
    └────────┬────────┘
             │
    ┌────────┴────────┐
    │                 │
    ▼                 ▼
Long docs?      Short docs?
    │                 │
    ▼                 ▼
Late Chunking   Contextual
```

---

## 7. Cost Analysis

### 7.1 API-based Models (per 1M tokens)

| Model | Cost | Notes |
|-------|------|-------|
| text-embedding-3-small | $0.02 | Best value |
| text-embedding-3-large | $0.13 | Highest quality |
| voyage-3 | $0.06 | Good balance |
| voyage-code-3 | $0.18 | Code-specialized |
| Cohere embed-v3 | $0.10 | Multilingual |

### 7.2 Local Models (Infrastructure)

| Model | VRAM | Throughput | Notes |
|-------|------|------------|-------|
| all-MiniLM-L6-v2 | 500 MB | ~1000 docs/sec | CPU viable |
| BGE-M3 | 2 GB | ~200 docs/sec | Needs GPU |
| Qwen3-Embedding-8B | 16 GB | ~50 docs/sec | High quality |

---

## 8. Key Takeaways

1. **Contextual Retrieval** is the current SOTA for RAG accuracy (67% failure reduction)
2. **Late Chunking** excels for long documents with cross-reference dependencies
3. **BGE-M3** is the best open-source multilingual model, especially for Korean
4. **voyage-code-3** leads code embedding benchmarks by significant margin
5. **Matryoshka** enables flexible dimension/quality trade-offs without retraining
6. **Hybrid search** (BM25 + Vector) should be default for production systems
7. **Reranking** provides additional 18% improvement over hybrid search alone

---

## References

1. Anthropic. "Contextual Retrieval in AI Systems" (2024)
2. Jina AI. "Late Chunking: Contextual Chunk Embeddings" arXiv:2409.04701
3. BAAI. "BGE M3-Embedding" arXiv:2402.03216
4. Voyage AI. "voyage-code-3: More Accurate Code Retrieval" (2024)
5. Google. "Matryoshka Representation Learning" NeurIPS 2022
6. Hugging Face. "MTEB Leaderboard" (2025)
