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

Not yet measured. Results will be added here with the package version, the embedding model, the reranker and the fusion
settings they were measured with.
