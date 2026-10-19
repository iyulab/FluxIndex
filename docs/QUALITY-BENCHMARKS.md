# Quality Benchmarks

Which public benchmarks measure FluxIndex retrieval — hybrid vector + keyword search with reranking — with Korean as a
primary language. [REFERENCE.md](REFERENCE.md#performance-benchmarks) has the speed figures.

## Korean retrieval: MTEB(kor, v2), led by MIRACL-ko

[MTEB(kor, v2)](https://github.com/embeddings-benchmark/mteb) ([MMTEB paper](https://arxiv.org/abs/2502.13595)) defines a
Korean suite whose retrieval tasks are corpus + queries + relevance judgements — a hybrid retriever produces a ranked list
and is scored like any other system, not only an embedding model.

| Choice | Value |
|---|---|
| Lead dataset | [MIRACL](https://github.com/project-miracl/miracl) Korean (native Korean Wikipedia, human-judged, Apache-2.0; 213 dev queries) — run on its [hard-negatives corpus](https://huggingface.co/datasets/mteb/MIRACLRetrievalHardNegatives) (43.3k passages) rather than the full 1.49M |
| Reranker on its own | MIRACLReranking |
| Smoke test | AutoRAGRetrieval (Korean domain documents, MIT; 114 queries — too small for a headline number) |
| Metric | nDCG@10 and Recall@100, against MIRACL's published BM25 baseline |
| English counterpart | [BEIR](https://github.com/beir-cellar/beir) |

The hard-negatives corpus is pooled from BM25 and two dense models, so recall on it is capped by that pool; the full corpus
is the check when a number looks surprisingly good. End-to-end Korean RAG leaderboards that grade an LLM's answer measure a
different layer.

## Status

**Default hybrid fusion (measured 2026-10, nDCG@10, no reranker, SQLite store).** Embedding model BAAI/bge-m3 (GGUF on
llama-server for the Korean sets, ONNX for the English ones); keyword leg with the CJK bigram analyzer. «Former» is the
default through 0.102 (RRF; vector weight 0.8 for CJK or long queries, 0.6 middle, 0.3 short; weighted sum for technical terms),
«current» the relative-score-fusion default that replaced it (vector weight 0.6 CJK or long, 0.5 middle, 0.4 short).

| Set | Queries | Vector only | Keyword only | Former default | Current default |
|---|---|---|---|---|---|
| MIRACL-ko (dev, full hard-negatives corpus, 43,293 passages) | 213 | 0.7024 | 0.4079 | 0.7083 | **0.7257** |
| AutoRAG-ko (`mteb/AutoRAGRetrieval` test, 720 passages) | 114 | 0.8298 | 0.9183 | 0.8581 | **0.9164** |
| BEIR SciFact (test) | 300 | 0.6435 | 0.6629 | 0.6749 | **0.7062** |
| BEIR NFCorpus (test) | 323 | 0.3150 | 0.3055 | **0.3472** | 0.3444 |

The weights were chosen on MIRACL-ko, SciFact and NFCorpus; AutoRAG-ko was measured afterwards. Fitting the weight to an index's
own labelled queries did not beat these fixed values on any set.
