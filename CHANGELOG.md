# Changelog

All notable changes to FluxIndex packages are documented here.
Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) conventions.

---

## [Unreleased]

---

## [0.47.0]

### Fixed
- **A reranker that scores on a logit scale no longer loses rows to the default options.**
  `RerankOptions.ScoreThreshold` defaulted to `0f`, which is "no threshold" only for a reranker that
  answers in (0, 1). On a raw-logit reranker relevant documents routinely score below zero, so every
  `RerankerBase` provider dropped them with nothing set — including the SDK's
  `SearchOptions.UseReranker` path, which passes default options. `ListwiseRerankOptions` had the same
  default and the same effect: candidates arriving with negative initial scores came back as an empty
  list.

### Changed
- **Breaking: `RerankOptions.ScoreThreshold` and `ListwiseRerankOptions.ScoreThreshold` are `float?`,
  default `null` = no threshold.** An explicit value still filters, including `0f` and negative
  values. Assignments (`ScoreThreshold = 0.5f`) compile unchanged; code that *reads* the property as
  `float` — a custom `IReranker` honouring it — handles `null`. If you relied on the old default to
  drop negative scores, set `ScoreThreshold = 0f`.
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.69.0 -> 0.70.0, `LMSupply.Generator` 0.69.0 -> 0.70.0, `LMSupply.Reranker` 0.69.0 -> 0.70.0, `WebFlux` 0.10.0 -> 0.11.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.
- Re-pinned sibling package(s) `FluxImprover` 0.12.15 -> 0.12.16 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.
- Re-pinned sibling package(s) `FileFlux` 0.23.19 -> 0.23.20 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.46.4]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.23.18 -> 0.23.19, `FluxCurator` 0.8.3 -> 0.9.0, `FluxImprover` 0.12.14 -> 0.12.15, `LMSupply.Embedder` 0.68.3 -> 0.69.0, `LMSupply.Generator` 0.68.3 -> 0.69.0, `LMSupply.Reranker` 0.68.3 -> 0.69.0, `WebFlux` 0.9.0 -> 0.10.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.46.3]

### Changed
- Re-pinned sibling package(s) `FluxGuard.Remote` 0.16.0 -> 0.17.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.46.2]

### Changed
- Re-pinned sibling package(s) `WebFlux` 0.8.0 -> 0.9.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.46.1]

### Changed
- Re-pinned sibling package(s) `FluxGuard.Remote` 0.15.1 -> 0.16.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.46.0]

### Fixed
- **A scoped search on the SQLite vector store no longer re-parses the query vector once per stored
  vector.** When a metadata filter is narrow enough that the candidate window cannot fill `TopK`, the
  store walks every vector exactly (so a match ranked past the window is still found). That walk bound
  the query vector as text, and the scalar distance function parses its argument per row — so the scan
  paid for parsing the whole query vector 6,000 times on a 6,000-vector store. It is now bound as a
  float32 blob. Measured over 6,000 chunks of 384 dimensions at `TopK` 10, a scope of 100 documents:
  **277 ms to 63 ms**. Unfiltered and vault-wide scopes were already ~6 ms and are unchanged.

### Added
- **`MetadataFilterMatcher` — a metadata filter with its alternatives expanded once.** Every store that
  cannot push a filter into its query matches rows in memory, and each of those loops re-expanded and
  re-normalized the filter for every row: a scope of 100 documents over 6,000 rows was 600,000
  normalizations for one search. `MetadataFilterMatcher.Compile(filters)` does that work once and
  `Matches(metadata)` is a set lookup; all nine in-memory filter loops now use it.
  `VectorStoreBase.MatchesMetadataFilter` remains for one-shot use and delegates to it, so match
  semantics are defined in exactly one place. (This removes a quadratic; at the sizes measured above it
  is not where the time was going — see the scan fix.)

### Changed
- **A malformed filter value now throws when the search starts, not when the first row is matched.**
  Expanding the filter up front means an unsupported or empty collection value fails the search even
  when no row reaches the match — previously such a filter passed silently as an empty result on an
  empty store.
- Re-pinned sibling package(s) `WebFlux` 0.7.4 -> 0.8.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.45.0]

### Added
- **`SearchOptions.UseReranker` — a registered `IReranker` now reaches `Retriever`.** `AddLMSupplyReranker` and
  `AddOpenAICompatibleReranker` registered a reranker that nothing called; you had to resolve it and rerank the
  results yourself. With `UseReranker = true`, `Retriever.SearchAsync(query, options)` fetches
  `RerankCandidateCount` candidates (default `TopK × 3`), runs them through the registered reranker after the RAG
  security pass, and returns its top `TopK`. `SearchResult.Score` becomes the rerank score, the new
  `SearchResult.RetrievalScore` keeps the retrieval score, and `SearchResponse.Metadata["reranked"]` reports it.
  Off by default, so existing searches are unchanged; `UseReranker = true` with no reranker registered throws
  `InvalidOperationException`. `Retriever`'s constructor takes a new optional `IReranker`.

- **`AddSQLiteKeywordSearch()`** (`FluxIndex.Storage.SQLite`) registers the SQLite keyword index in a container the
  builder does not assemble, as `AddPostgreSQLKeywordSearch` does for PostgreSQL. With no connection string it uses
  the database of the registered SQLite vector store. It picks up a registered `ITextAnalyzer` and
  `KeywordFieldOptions`, which a hand-written `new SQLiteKeywordSearchService(...)` registration has to remember.
  The README has named this method since 0.33.0; it did not exist until now.

### Fixed
- **`SearchOptions.MinSimilarity` no longer empties a hybrid search.** The builder registers a hybrid search service,
  so `Retriever.SearchAsync(query, options)` runs hybrid by default — and the threshold was mapped to the fused
  score, which is rank-sized under reciprocal rank fusion (about 0.016 at best), so any similarity-sized value
  dropped every result. It is now a similarity floor on the vector leg, as it is on the vector path. **Behaviour
  change**: a thresholded hybrid search returns results where it returned none.
- **A hybrid search returns up to `MaxResults` / `TopK` results.** `HybridSearchService` fetched each leg's own
  `MaxResults` (default 10) whatever the fused list was asked for, so a request for 25 returned about 17. A leg now
  fetches at least as many candidates as the fused list must return.
- **`HybridSearchOptions` (SDK) weights and `FusionMethod` are applied.** The hybrid service's auto strategy is on
  by default and replaces the weights and the fusion method per query, so the values mapped from the SDK options
  were dead. Passing the SDK's `HybridSearchOptions` now turns auto strategy off for that search; plain
  `SearchOptions` keep it.

---

## [0.44.7]

### Fixed
- **A keyword-index write costs what it writes, not the size of the index** (SQLite and PostgreSQL keyword
  services). Every `IndexChunksAsync` and delete re-read both posting tables in full to update document frequency —
  the term filter sat outside a `UNION ALL` the planner did not push it into — and recounted the corpus statistics
  from every posting row. Writing entries one at a time therefore cost the square of the index: about 7 ms per entry
  at the start of a 1 500-entry run and 41 ms at its end, half an hour for a 6 000-entry rebuild. Document frequency
  is now read per term through the posting tables' keys, and the statistics move by what the transaction added and
  removed. An index written by an earlier release is recounted once, on its next write; `OptimizeIndexAsync`
  recounts on demand.
- **A keyword search with a wide metadata filter no longer slows with the number of accepted values.** The filter
  was tested per posting row, value list and all: on a 6 000-chunk index a search that takes 30 ms unfiltered took
  9 s when scoped to every document id (a filter of 6 000 values) — the shape a vault-wide FluxFeed search sends on
  every keyword and hybrid request. The filter is now an uncorrelated membership test, and one with more than 256
  accepted values is resolved to its chunk set once per search (78 ms for the same request). Results are unchanged,
  and the value list is no longer bounded by the backend's parameter limit.
- **Re-indexing a chunk whose new content has no terms removes its old postings.** The chunk was skipped before its
  previous rows were deleted, so text it no longer held kept matching.
- **Hybrid auto strategy matched technical terms as substrings.** "email", "maintain" and "html" counted as `AI` and
  `ML`, which switched those queries from rank fusion to weighted-sum fusion — a different ranking and a different
  score scale. Terms are matched as whole tokens.

---

## [0.44.6]

### Fixed
- **A registered `IRAGSecurityPipeline` now reaches the builder's `Retriever`.** `FluxIndexContextBuilder.Build()`
  constructed the retriever without it, so the guard registered in `ConfigureServices` never ran on a search. A roster
  test now holds every optional retriever dependency to the container.
- **The "UseHybridSearch is enabled but IHybridSearchService is not registered" error named a method that does not
  exist** (`UseQdrantWithHybrid()`). It now points at `AddQdrantWithHybridSearch` and the builder's default registration.

### Changed
- **README: Key Features rewritten against the code** — each feature names its entry point and how to enable it.
  Corrected along the way: `IReranker` is not called by `Retriever` (rerank the results yourself); the query-embedding
  cache is internal to `Retriever`; `UseRedisCache` needs `AddRedisStorage()`; `FluxIndex.MCP` is a library you host.
  The Performance table is removed — the benchmark results it cited are no longer in the repository.

---

## [0.44.5]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.23.17 -> 0.23.18, `FluxImprover` 0.12.13 -> 0.12.14, `LMSupply.Embedder` 0.68.2 -> 0.68.3, `LMSupply.Generator` 0.68.2 -> 0.68.3, `LMSupply.Reranker` 0.68.2 -> 0.68.3, `WebFlux` 0.7.3 -> 0.7.4 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

### Fixed
- **Exception messages in `FluxIndex.Core` and `FluxIndex.Storage.SQLite` were not in English.** Twelve distinct messages, thrown from eighteen places (sqlite-vec extension loading, `SQLiteVecOptions.Validate`, the performance monitor, and the retrieval evaluation helpers), were Korean. Operators read exception messages, paste them into issues and search them in log pipelines, and the library's log messages were already English-only. The messages are now English; their meaning is unchanged.

---

## [0.44.4]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.23.15 -> 0.23.17, `FluxImprover` 0.12.11 -> 0.12.13, `LMSupply.Embedder` 0.68.0 -> 0.68.2, `LMSupply.Generator` 0.68.0 -> 0.68.2, `LMSupply.Reranker` 0.68.0 -> 0.68.2, `WebFlux` 0.7.2 -> 0.7.3 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

### Fixed
- **`SQLiteVecVectorStore`: a metadata-filtered search could miss in-scope chunks that ranked past the KNN window, and on a large enough store it returned nothing at any `topK`.** The metadata lives in `vector_chunks`, not in the vec0 table, so the filter ran after a KNN window of `topK * 3`, capped at sqlite-vec's `k` ceiling of 4,096. A narrow scope inside a store of a few thousand chunks could therefore not be answered however large `topK` was. When the window comes back full and the filter still cannot fill `topK`, the store now computes exact distances over the whole vec0 table, using the table's own distance metric, and walks the candidates in distance order until `topK` pass the filter. The result is exact. vec0 is a brute-force index, so the scan costs about what the KNN costs, and it only runs in that case. The on-disk schema is unchanged, so an existing database works as it is. The window warnings added in 0.29.0 and 0.44.2/0.44.3 ("the metadata filter is applied after the KNN step", "results may starve") no longer describe a possible outcome and are gone. The clamp and the scan are recorded at `Debug`.
- **`SQLiteVecVectorStore.HybridSearchAsync`: the text leg had the same shape.** A filtered FTS5 query read `LIMIT topK * 3` rows and filtered those, so an in-scope match ranked past that limit was dropped. Filtered text queries now read in rank order until `topK` matches pass.

---

## [0.44.3]

### Changed
- **`SQLiteVecVectorStore`: the KNN-window clamp warns only when the clamped window actually filled.** 0.44.2 logged the clamp at `Warning` the moment the requested window passed the ceiling, so a store holding a handful of chunks reported "results may starve" for every query wide enough to trip it — the search had in fact returned everything it held. The clamp is now judged by what the KNN returns: a full window keeps the warning (the answer really is drawn from the nearest 4,096 chunks, and a filter applied after the KNN step can starve inside it), while a window the store could not fill is recorded at `Debug` naming the row count. Nothing about which results are returned changes.

---

## [0.44.2]

### Fixed
- **`SQLiteVecVectorStore`: a KNN window past sqlite-vec's `k` ceiling failed the whole search.** vec0 rejects `k > 4096` (`k value in knn query too large`), and the store widens the caller's `topK` on its own — ×3 under a metadata filter, ×2 more on the vector leg of `HybridSearchAsync` — so a scoped hybrid search with `topK` as low as 683 threw instead of answering. The window is now clamped to the ceiling; the search answers from the nearest 4,096 chunks and logs a warning naming the requested and clamped sizes (a filter applied after the KNN step may starve in a clamped window, which the existing saturation warning also reports).

---

## [0.44.1]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.23.14 -> 0.23.15, `FluxCurator` 0.8.2 -> 0.8.3, `FluxGuard.Remote` 0.15.0 -> 0.15.1, `FluxImprover` 0.12.10 -> 0.12.11 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.44.0]

### Added
- **Graph store partitions.** One `IGraphStore` instance can hold several tenants' graphs: `GraphEntity.Partition` and `GraphCommunity.Partition`, `GraphRAGBuildOptions.Partition` / `GraphRAGLoadOptions.Partition` / `GraphRAGIndex.Partition`, `EntityGraphBuildOptions.Partition`, `EntityGraphResult.Partition`, `LeidenOptions.GraphPartition`, and `GraphPartition.Default` (the empty string). Entities merge only within their partition, community ids are derived inside it, and `UpdateIndexAsync` writes into the index's partition. A build whose entity-graph or community options name a different partition than `GraphRAGBuildOptions.Partition` is refused.
- `IGraphStore.GetEntitiesByNormalizedNamesAsync(normalizedNames, partition)` — exact normalized-name lookup in one round trip.
- `GraphRAGBuildOptions.WithPartition(partition)` — a copy in another partition, for a host that assigns the partition to options its caller supplied.

### Changed
- **Breaking:** the multi-result `IGraphStore` reads — `GetEntitiesByNameAsync`, `GetEntitiesByTypeAsync`, `GetRelationshipsByTypeAsync`, `GetEntitiesByChunkIdsAsync`, `GetTopCommunitiesAsync`, `GetCommunitiesByChunkIdsAsync`, `GetStatisticsAsync` — take `string partition = GraphPartition.Default` before the cancellation token, and return that partition only. The default is the default partition, not every partition. Callers passing the token positionally must name it (`ct: ct`); implementers add the parameter.
- An entity extracted from one document now joins the stored entity of the same identity (normalized name, type, declared subtype) that another document produced, even when the two share no chunk — previously the join only reached entities already attached to the build's own chunks, so the graph gained one duplicate node per document mentioning an entity. Existing duplicates are not merged; re-index to collapse them.
- **`AddLMSupplyReranker()` and `LMSupplyRerankerOptions` default to the `auto` reranker** instead of `default`. `default` is an English-only cross-encoder: over a non-English query it ranks an unrelated passage in the query's language above one in another language that answers it. `auto` picks by hardware tier — `quality` (multilingual) on mid-range machines, `multilingual` above, `default` on low-spec machines — so search results change for consumers that relied on the old default; pass `"default"` to keep it.
- LMSupply dependency raised to 0.68.0, where the `large` and `multilingual` rerankers load (external weights, ONNX export) and `auto` resolves to `multilingual` on High and Ultra tiers.
- SQLite and PostgreSQL entity graph stores add a `partition` column (default `''`) to entity and community tables; start-up provisioning adds it to existing databases, whose rows read as the default partition. Neo4j nodes without a `partition` property read as the default partition.

### Fixed
- Neo4j: `StoreEntitiesBatchAsync` did not write an entity's `Properties`, `Embedding` or `ExternalLinks` (the single-entity write did), so every entity the entity graph build persisted lost them — including the declared subtype its identity is keyed on. Both writes now share one upsert.
- Documentation snippets for `LoadIndexAsync`, `LocalSearchAsync` and `GetEntitiesByChunkIdsAsync` passed the cancellation token into an options parameter.
- SQLite entity graph store registered through `AddSQLiteEntityGraphStore`: re-storing an entity, relationship or community that already existed (`StoreEntityAsync`, `StoreEntitiesBatchAsync`, `UpdateEntityAsync`, `StoreRelationshipAsync`, `StoreRelationshipsBatchAsync`, `StoreCommunityAsync`) changed nothing and reported success, because the registration's no-tracking context left the looked-up row detached. Inserts were unaffected, so the loss showed only as updates that never landed — an entity joined by a second document kept the first document's chunks and mention count. Existing rows are now loaded tracked.

---

## [0.43.2]

### Changed
- Microsoft.Extensions.* / Microsoft.Data.Sqlite / EF Core pins raised to 10.0.12 (September 2026 .NET servicing).
- Re-pinned sibling package(s) `FileFlux` 0.23.12 -> 0.23.14, `LMSupply.Embedder` 0.66.1 -> 0.67.0, `LMSupply.Generator` 0.66.1 -> 0.67.0, `LMSupply.Reranker` 0.66.1 -> 0.67.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.43.1]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.23.11 -> 0.23.12, `FluxGuard.Remote` 0.14.2 -> 0.15.0, `FluxImprover` 0.12.9 -> 0.12.10 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.43.0]

### Removed

- **`IAdvancedEntityExtractionService.LinkEntitiesAsync`, `EntityLinkingOptions`, `LinkedEntityGraph`,
  `LinkedEntity`, `EntityLinkingStats`.** Nothing in the library called it: the indexing pipeline links
  entities through `EntityGraphService` (one identity - normalized name, type, subtype - shared with the
  stored merge), so this method was a second, unused implementation of linking that keyed on the
  lower-cased normalized text and, with `RequireSameType = false`, collapsed every type into one. A
  consumer that replaces the extractor no longer has to implement it. Options-roster baseline entry
  for `EntityLinkingOptions` (three never-read properties) removed with it.

### Fixed

- **Entity identity is defined in one place, and every stage that needs it uses that one.** Four
  stages had drifted apart, and nothing failed when they disagreed - the graph simply came out
  differently depending on which path ran:
  - In-build linking grouped entities by the normalized text **alone**. One name carried by two types
    (`Apple` the organisation and `Apple` the product) collapsed into a single node, named after
    whichever member scored the higher confidence.
  - The merge against stored extractions keyed on **(normalized name, type)**. So the pair the first
    stage had just collapsed was looked up as two - and a re-index whose confidences ordered
    differently stopped matching the nodes the first index wrote.
  - Whether the extractor's own `ExtractedEntity.NormalizedText` was honoured depended on
    `EntityGraphBuildOptions.LinkEntitiesAcrossChunks`: the linking path recomputed the name and
    dropped it, the non-linking path read it. Toggling that option changed stored identity.
  - `NormalizedText` is a non-nullable string that defaults to empty, so the `??` the non-linking path
    was written with could never reach its fallback: **every node built through that path was named the
    empty string**, and because the merge keys on that name, all entities of one type collapsed onto a
    single stored node.
  Identity is now one function used by the grouping, the stored merge, both node-building paths and the
  query-side match. **Behavior change after re-index**: entities that share a name but not a type are
  now separate nodes (that is the point), and an extractor-supplied `NormalizedText` is honoured
  whatever the linking option says.
- **The declared subtype is part of entity identity.** An extractor that classifies with its own
  vocabulary - two `Custom` subtypes sharing a name - had the second merged into the first and its
  label dropped, because identity was (normalized name, type) alone. Identity is now (normalized name,
  type, subtype): each subtype is its own node carrying its label under `"subtype"` in the node's
  properties, and `GetEntitiesByChunkIdsAsync` returns each with its own label. The subtype is trimmed
  and compared ordinally (a declared key, not free text); null or whitespace keys as none. A query
  entity that declares no subtype matches every subtype of that name and type, and `SearchByEntitiesAsync`
  now returns all of them rather than the first. **Behavior change after re-index**: subtyped and
  non-subtyped entities of one name are separate nodes.
- **Cache entries declare a size.** `HierarchicalSummarizationService` (community summaries) and the SDK's
  `InMemoryCacheService` wrote entries to the host's shared `IMemoryCache` without `Size`. A host that
  sets `SizeLimit` on that cache - which any library sharing it is entitled to do - made `Set` throw,
  and the whole memorize failed on the summary. Every entry now counts as one unit.
- **Property values read back from a store are the values the build wrote.** Every store keeps node
  properties as JSON and deserialized them to `JsonElement`s, so `Properties["subtype"]` on a
  reconstituted node was never equal to the string the build compared it with - and a re-index would
  have written a second node beside every subtyped one. Reconstitution now unwraps JSON primitives
  (string, number, boolean); objects and arrays stay elements.

---

## [0.42.1]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.23.10 -> 0.23.11, `FluxGuard.Remote` 0.14.1 -> 0.14.2, `FluxImprover` 0.12.8 -> 0.12.9, `LMSupply.Embedder` 0.66.0 -> 0.66.1, `LMSupply.Generator` 0.66.0 -> 0.66.1, `LMSupply.Reranker` 0.66.0 -> 0.66.1 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.42.0]

### Added
- Relational keyword index (SQLite, PostgreSQL): chunk **metadata fields are scored with BM25F**. `KeywordFieldOptions` names the metadata keys the index treats as fields and their weights; a query term that appears only in a document's title or file name now retrieves the chunk. Register an instance (`services.AddSingleton(new KeywordFieldOptions { Fields = [new KeywordField("title", 2.0)] })`) before `AddSQLiteKeywordSearch` / `AddPostgreSQLKeywordSearch`; `KeywordFieldOptions.None` is body-only. The index path and the query path share one instance and the body's `ITextAnalyzer`. Fields are stored in a new relation (`bm25_field_postings`) created `IF NOT EXISTS`, so an existing database needs no migration.
- `FluxIndex.SDK.HybridSearchOptions.FusionMethod` and `RrfK` pass through to the hybrid search service; previously only the two-value `RerankingStrategy` reached it, so relative-score fusion, product, maximum and harmonic mean were unreachable from the SDK.
- `Indexer` writes `Document.FileName` into each chunk's `file_name` metadata when the chunk does not already carry one, so the default file-name field applies to documents indexed through the SDK, not only through FluxFeed.

### Changed
- **Behavior change after re-index**: the default field set is `title` and `file_name` at weight 1.0, on. A chunk carrying either key ranks differently once its keyword leg is re-indexed on 0.42.0 (that is the point) and exactly as before until then (an index with no field postings takes the previous scoring expression verbatim). Changing the field set of an existing index is like changing the analyzer: re-index the keyword leg afterwards. Document frequency counts a chunk once however many fields carry the term, and only the configured fields count.
- `KeywordIndexStatistics.TotalTermOccurrences` is documented as a body count; field occurrences are not included.

---

## [0.41.2]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.23.9 -> 0.23.10, `LMSupply.Embedder` 0.65.1 -> 0.66.0, `LMSupply.Generator` 0.65.1 -> 0.66.0, `LMSupply.Reranker` 0.65.1 -> 0.66.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.41.1]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.23.8 -> 0.23.9, `FluxCurator` 0.8.1 -> 0.8.2, `FluxImprover` 0.12.7 -> 0.12.8, `WebFlux` 0.7.1 -> 0.7.2 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.
- Raised `Microsoft.Extensions.*` package references from 10.0.8 to 10.0.12 (latest servicing release). The re-pinned `FluxCurator`, `FileFlux` and `WebFlux` releases declare `Microsoft.Extensions.*` floors above 10.0.8.

---

## [0.41.0]

### Added
- `TextCompletionServiceBase.CompleteJsonAsync` passes the caller's `TextCompletionOptions.ResponseSchema`
  (Flux.Abstractions 0.25.0) through, so an implementation over a schema-capable provider can enforce the shape the
  caller parses.

### Fixed
- `FluxIndex.Providers.LMSupply`: `LMSupplyTextCompletionService` forwarded only `MaxTokens` and `Temperature` and
  silently dropped the rest of `TextCompletionOptions`. `TopP`, `FrequencyPenalty`, `PresencePenalty`,
  `StopSequences` and `ResponseSchema` (as `GenerationOptions.JsonSchema`) now reach the generator when set, unset
  members keep the generator's defaults, and `SystemPrompt` is sent as the system message on the chat path.
  `ResponseFormat = "json"` without a schema has no LMSupply counterpart and is documented as not forwarded.

### Changed
- Requires Flux.Abstractions 0.25.0.

---

## [0.40.1]

### Fixed
- **GraphRAG: rebuilding an unchanged document no longer adds communities.** `LeidenCommunity.Id` was a new GUID on
  every build and `LeidenCommunityService` shuffled nodes with an unseeded `Random`, so each re-index persisted a fresh
  set of communities next to the previous ones (the graph store upserts by id, and no id ever matched) and paid for
  every community summary again. Community ids are now derived from the level and the member chunk ids, detection
  without `LeidenOptions.RandomSeed` uses a seed derived from the input chunk ids and no longer depends on input order,
  and a stored community with the same id and a summary is reused instead of summarized again. A document whose text
  changed still gets new communities; removing the previous build's communities is not part of this release.

### Changed
- `LeidenOptions.RandomSeed = null` now means "derived from the input" rather than "a different random seed each call":
  the same chunks always produce the same communities.

---

## [0.40.0]

### Added
- **`IKeywordSearchService.DeleteChunksAsync(IEnumerable<string> chunkIds)`** — removes a set of chunks
  as one operation, the removal counterpart of `IndexChunksAsync`. A generation swap that drops a
  document's superseded chunks should call it once instead of `DeleteChunkAsync` per chunk: a
  relational index rewrites the document frequency of every term the chunks hold, and per-chunk calls
  rewrite the same shared term rows once for every chunk holding them. Blank and unknown ids are
  ignored, a repeated id is removed once. Implemented by the SQLite, PostgreSQL and in-memory indexes;
  held by the shared keyword contract suite. **Breaking for custom `IKeywordSearchService`
  implementations**, which must add the member.

### Removed
- **Twelve public service implementations that nothing in the library registered, constructed or called —
  and the capability clusters around them.** Each compiled and passed its own tests, and none of it
  ran: no DI registration, no caller, and in most cases no registration or caller for its interface
  either. Removed with their interfaces and the models, options and tests only they used:
  - `ColBERTService` / `IColBERTService` (+ `ColBERTOptions`, `ColBERTCompressionOptions` and the ColBERT
    result records)
  - `CommunityDetectionService` / `ICommunityDetectionService` — community detection runs through the
    registered `LeidenCommunityService` / `ILeidenCommunityService`, which `GraphRAGService` consumes
  - `BM25Service` / `IBM25Service` (+ `BM25Result`) — keyword search is `IKeywordSearchService`
    (SQLite, PostgreSQL, in-memory `BM25SparseRetriever`)
  - `QueryTransformationService` / `IQueryTransformationService` (+ both `QueryTransformationOptions`,
    `HyDEOptions`, `QuOTEOptions`, `HyDEResult`, `QuOTEResult`, `QueryIntentResult`, `QueryComplexity`).
    `docs/REFERENCE.md` showed resolving `IQueryTransformationService` from DI; it was never registered,
    so that example threw — the section is removed
  - evaluation tooling with no registration: `EvaluationJobManager`, `GoldenDatasetManager`,
    `QualityGateService` and their interfaces (+ `QueryLog`, `DatasetValidationResult`,
    `DatasetStatistics`, `QualityGateResult`, `PerformanceComparisonResult`, `EvaluationJob`, `EvaluationStatus`), `KeywordOverlapEvaluator` /
    `IResponseEvaluator` (+ `QATestCase`, `EvalCaseResult`, `EvalRunResult`), `InMemoryEvaluationResultCache` /
    `IEvaluationResultCache`, `MockEvaluationSearchProvider` / `IEvaluationSearchProvider`
  - `HNSWParameterOptimizer` / `IVectorIndexOptimizer` (+ `HNSWOptimizerOptions`, `HNSWParameters`,
    `HNSWPerformanceProfile`, `ParameterValidationResult`, `QualityTarget`, the Core `DistanceMetric` enum) —
    no store accepted the parameters it produced
  - `AlgorithmicReranker` — the registered `IReranker` implementations are unchanged
  - `RuleBasedMetadataExtractor` / `IRuleBasedMetadataExtractor`
  - `IRAGEvaluationService` and its models (`RAGEvaluationResult`, `GoldenDatasetItem`, `EvaluationDifficulty`,
    `EvaluationConfiguration`, `BatchEvaluationResult`, `EvaluationCriteria`, the Domain `QualityThresholds`) — no
    implementation anywhere, and the builder's `WithEvaluationSystem` / `WithEvaluationSystemForDevelopment` /
    `WithEvaluationSystemForProduction` returned the builder unchanged; those three methods are removed too
    (the monitoring `QualityThresholds` used by `SetQualityThresholdsAsync` is unaffected; FluxIndex.Integrations.FluxImprover
    keeps its own `RAGEvaluationService`)
  - `QueryDecompositionResult`, `QueryRelationshipType` and the Domain `QueryIntent` (the `QueryIntent` enums used by
    the complexity analyzer and token-aware search are separate types and stay)
  - both unread `MetadataExtractionOptions` (Domain and Core.Options), `Core.Options.BatchProcessingOptions`, `MetadataExtractionStatistics` and `MetadataExtractionErrorType`
- **SDK settings that nothing read.**
  - `FluxIndexOptions.Indexing` (`IndexingConfiguration`, `ChunkingDefaults`), `FluxIndexOptions.Search`
    (`SearchConfiguration`) and `FluxIndexOptions.RAGEnhancement` (`RAGEnhancementOptions`, `RAGEnhancementMode`,
    `LateChunkingConfiguration`, `MultiHyDEConfiguration`, `ContextualRetrievalConfiguration`). The builder wrote
    the first two and nothing read either; the third was never wired to the Core services it names.
    Chunking and search defaults are `IndexerOptions` / `RetrieverOptions`, as before.
  - `WithChunking(string strategy, int chunkSize, int chunkOverlap)` is now `WithChunking(int chunkSize, int chunkOverlap)`:
    the SDK splitter has one algorithm, so `strategy` was parsed and discarded (`IndexerOptions.ChunkingStrategy` and the
    `ChunkingStrategy` enum are removed with it).
  - `IndexingOptions.ChunkingStrategy`, `MaxChunkSize`, `OverlapSize` and `EnableOCR` — the `Document` overloads they
    were passed to do not chunk or parse files. (`GenerateEmbeddings` and `ExtractMetadata` remain, still not read,
    pending a design decision.)
  - `ISearchService`, which had no implementation, and the models only it used: `SearchRequest`,
    `SemanticSearchOptions`, the SDK `KeywordSearchOptions`, `FacetSearchOptions`, `FacetedSearchResponse`,
    `FacetValue`, `SimilarityOptions`, `RerankingOptions`. The Core `KeywordSearchOptions` used by
    `IKeywordSearchService` is unaffected.
- **Store, cache and service options that nothing read.** Setting any of these had no effect:
  - `SQLiteOptions.AllowDuplicates`, `BatchSize`, `DefaultSearchThreshold`, `DefaultVectorWeight`, `EnableVectorCache`,
    `VectorCacheSize` — the store upserts by chunk id, takes thresholds and weights per call, writes a batch in one
    `SaveChanges`, and has no vector cache (it scans every row per search; use the sqlite-vec store for large
    collections, now stated on `SQLiteOptions`)
  - `QdrantOptions.HttpPort` (the store speaks gRPC only) and the `CollectionName` alias — use `BaseCollectionName`
  - 17 `RedisSemanticCacheOptions` properties the Redis semantic cache never read (`KeyPrefix`, `DefaultSimilarityThreshold`,
    the timeout/retry/compaction/metrics/normalization/warm-up settings, …)
  - `AgenticRetrievalRouterOptions.DefaultMaxResults`, `EnableAdaptiveRouting`, `EnableDetailedExplanations`,
    `EnablePerformanceTracking`, `MaxFallbackAttempts`, `MinRoutingConfidence`, `StrategyTimeout`
  - `SelfRAGOptions.EnableDetailedLogging` and `UserContext`
  - `GraphRAGBuildOptions.GenerateEntityEmbeddings` and `LocalSearchOptions.UseEntityEmbeddings` — local search matches
    entities by name and never used entity embeddings
  - the Core `SemanticCacheOptions` and `ValueObjects.CacheOptions` types (every property unread; FluxImprover's own
    `CacheOptions` is unaffected), and the SDK `FluxIndexOptions.QualityMonitoring` block — `WithQualityMonitoring()`
    still registers the monitoring service and loses its unread `enableRealTimeAlerts` parameter
  README and the philosophy page no longer promise an algorithmic reranking fallback (there is none) or
  HyDE/QuOTE query transformation.
  **Breaking** for code that constructed these types directly or called the evaluation builder methods. The unreferenced-implementation roster
  test is now empty, so the next public implementation nothing references fails the build.

### Fixed
- **Deleting keyword-index chunks no longer deadlocks against concurrent indexing.** Every
  transaction that writes the shared term rows now acquires them first, in the one order indexing
  already used: deletion (`DeleteChunkAsync`, `DeleteByDocumentIdAsync`, `DeleteByFilterAsync`) and
  the terms of chunks an indexing call replaces. Deletion used to update those rows in whatever order
  the database chose, so a re-index that removed a document's previous chunks while another document
  was being indexed could fail with PostgreSQL's `40P01`. Measured against a real PostgreSQL server:
  six writers running re-index swaps over a shared vocabulary surfaced `40P01` before the change, and
  complete with zero deadlocks after it.
- **Deletion now retries a lost lock conflict** (`40P01`/`40001`) the way indexing does, instead of
  failing its caller on the first one.
- The cleanup of zero-frequency terms is scoped to the rows the transaction touched instead of
  scanning every term on every write.

### Changed
- Re-pinned `FileFlux` 0.23.7 -> 0.23.8. `AddFileFlux` no longer sets `SizeLimit` on the host's shared `IMemoryCache`, which made GraphRAG community summaries (stored without a `Size`) throw and fail indexing when `FluxIndex.Integrations.FileFlux` shared a container with GraphRAG.

---

## [0.39.1]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.23.6 -> 0.23.7 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.39.0]

### Added
- `IKeywordSearchService.GetChunkIdsByDocumentIdAsync(documentId)` — the ids the keyword index holds for a document, the counterpart of `IVectorStore.GetChunkIdsByDocumentIdAsync`. A pipeline swapping a document's generation can now capture the previous keyword generation from the keyword index itself instead of assuming it shares ids with the vector store. Implemented by the relational backends (SQLite, PostgreSQL) and the in-memory BM25 index; `KeywordSearchChunkIdentityContractSuite` covers all of them.

### Changed
- **Breaking for implementers**: `IKeywordSearchService` gained an abstract member; a custom keyword backend must implement it (answer from your own table — an unknown document yields an empty list).

---

## [0.38.1]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.23.5 -> 0.23.6, `FluxImprover` 0.12.6 -> 0.12.7 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.38.0]

### Changed

- **`Indexer.IndexDocumentAsync(string content, …)` now splits the text** into chunks of
  `IndexerOptions.ChunkSize` characters (default 512, at the nearest sentence/paragraph/word boundary,
  overlapping by `ChunkOverlap`). The builder had registered a splitter from these options and injected it
  into the indexer since the option existed, but this overload never called it and stored the whole text as
  one chunk — `WithChunking(...)`/`WithIndexerOptions(o => o.ChunkSize = …)` changed nothing. A document
  indexed through the quick-start path now yields several chunks where it used to yield one; text that fits
  in one chunk is stored verbatim as before. `ChunkOverlap >= ChunkSize` throws `ArgumentOutOfRangeException`.
- **Breaking**: the SDK-local `FluxIndex.SDK.Services.SimpleChunkingService` is removed. It duplicated
  `FluxIndex.Core.Services.SimpleChunkingService` with a worse algorithm (collapsed whitespace, overlapped by
  `overlap / 10` words); the builder now registers the core one. Register your own `IChunkingService` if you
  relied on the removed type.
- **Breaking**: the second, never-registered `FluxIndex.Core.Services.SelfRAG.SelfRAGService` is removed. It
  was a public type with no DI registration and no caller; `AddSelfRAG…` has always registered
  `FluxIndex.Core.Application.Services.SelfRAGService`, which is the one every option and document describes.
  With it gone, `SelfRAGOptions.MinResults` / `EnableDetailedLogging` / `UserContext` — which only that dead
  class read — are now visibly unread (their XML docs say so); wiring or removing them is a follow-up decision.
- **Binary-breaking, source-compatible**: the `maxResults`/`minScore` parameters of `Retriever.SearchAsync`,
  `HybridSearchAsync`, `KeywordSearchAsync`, `FindSimilarAsync`, `SearchQuantizedAsync`, `SearchWithRerankAsync`
  and the `FluxIndexContext`/`IFluxIndexContext` facades are now nullable; an omitted argument takes
  `RetrieverOptions.DefaultMaxResults`/`DefaultMinScore`. `WithSearchOptions(...)` wrote those options and no
  search method read them — each overload carried its own constant (10 and 0.2; the `IFluxIndexContext`
  interface declared 0.5, so the same call had a different threshold through the interface than through the
  class). `RetrieverOptions.DefaultMinScore` now defaults to the 0.2 that actually ran, so callers who never
  used `WithSearchOptions` see no change; interface callers who omitted `minScore` move from 0.5 to 0.2.
- **Breaking**: `AddPostgreSQLVectorStore` no longer takes an `enableAutoMigration` parameter. Schema
  provisioning follows `PostgreSQLOptions.AutoMigrate` (the same shape every other component uses; the
  connection-string overload exposes it as `autoMigrate`). The builder maps `VectorStore.EnableAutoMigration`
  onto it, so the builder-level opt-out is unchanged.
- `Retriever.SearchAsync(query, SearchOptions)` **throws** when `SearchOptions.UseGraphRAG` is `true` instead
  of ignoring it: `InvalidOperationException` when no `IGraphRAGService` is registered, `NotSupportedException`
  otherwise — free-text search has no chunk set to build a graph index from; use
  `IGraphRAGService.BuildIndexAsync`/`LoadIndexAsync` + `QueryAsync`. `null` never auto-activated GraphRAG here;
  the XML docs that said it did are corrected.

### Fixed

- **`AdaptiveSearchOptions.Timeout` is honoured.** It was declared (default 30 s) and never read, so
  an adaptive search ran as long as it liked. It now bounds the whole search; a search that exceeds it
  fails with a `TimeoutException` (a caller's own cancellation still surfaces as
  `OperationCanceledException`). A search that used to run past 30 s now fails at 30 s unless
  `Timeout` is raised or set to `TimeSpan.Zero`.
- **`SelfRAGOptions.SearchTimeout` is honoured** by the registered Self-RAG service (default 2 min,
  never read before): an iterative search that overruns it ends as an unsuccessful `SelfRAGResult`
  whose `TerminationReason` starts with `Timeout:` — the shape every other failure takes there. The
  caller's own cancellation still propagates.
- **LLM entity and relation extraction discarded every result.** `EntityExtractionService` asked the
  model for lowercase keys (`"text"`, `"type"`, `"source"`, ... — its own prompt example) and then
  deserialized the reply into PascalCase records with case-sensitive matching, so every field stayed at its
  default and every item was dropped as empty — no exception, no log. With `UseLlm = true` a GraphRAG index
  therefore contained zero LLM-derived entities and relations; only the Latin-capitalisation pattern
  fallback ever contributed. Both replies now bind case-insensitively, and a reply that parses but carries
  no usable item (wrong keys) is logged as a warning instead of vanishing.
- `AutoMigrate = false` — the operator's "I manage this schema" switch — was never read by four
  provisioners: the SQLite graph store and entity-graph store (the builder copied
  `GraphStore.AutoMigrate` into their options and both ignored it, so the documented opt-out provisioned
  anyway), and the PostgreSQL vector stores (plain and quantized), where `PostgreSQLOptions.AutoMigrate`
  did nothing because a separate parameter was the gate. All four now honour it; with it off nothing is
  touched, not even a connection.
- Per-call `IndexingOptions.CustomOptions` were discarded by the indexer, so
  `new IndexingOptions().WithAIMetadataExtraction(...)` passed to `IndexDocumentAsync(document, options)` had
  no effect — only the builder-level `IndexerOptions.CustomOptions` were read. Both are read now; the caller's
  keys win.
- The core splitter could loop forever when a sentence boundary sat within `chunkOverlap` characters of the
  window start (the next window moved backwards). The window now always advances.
- `DocumentChunk.TotalChunks` — the "of N" in a "chunk i of N" citation — was lost twice on the way to a
  search result: the SDK indexer's embedding step rebuilt each chunk by hand in three places and none of them
  copied the field, and the SQLite, sqlite-vec and PostgreSQL vector stores (plain and quantized) had no
  column for it, so `Metadata["totalChunks"]` read `0` for every row. Chunks are now embedded in place, and
  the stores persist the field: the column is added to an existing database at start-up and rows written
  before it existed are backfilled with the per-document count, so nothing reads `0` afterwards. The shared
  vector-store contract suite now round-trips chunk position on every read path (in-memory, SQLite ×3,
  PostgreSQL, Qdrant).
- The indexer's safety split for chunks over ~8,000 tokens threw on its second piece (the pieces were created
  against the pre-split total) and never renumbered the document; the pieces and the chunks after them are
  now numbered 0..N-1 with `TotalChunks = N`.
- `SQLiteVectorStore` and the sqlite-vec store provision their tables through the shared per-table provisioner
  instead of `EnsureCreated` plus a hand-written `CREATE TABLE` (which had already drifted from the model), so
  they also pick up columns added later.
- `Indexer.ExtractMetadataBatchAsync` named two builder methods that do not exist in its "not configured"
  error; it now points at `ConfigureServices`.
- Documentation: `IndexingOptions.ChunkingStrategy/MaxChunkSize/OverlapSize/GenerateEmbeddings/ExtractMetadata/EnableOCR`,
  `IndexerOptions.ChunkingStrategy`, `ChunkingDefaults`, `SearchOptions.IncludeVectors`,
  `SQLiteOptions.AllowDuplicates/DefaultSearchThreshold/DefaultVectorWeight/BatchSize/EnableVectorCache/VectorCacheSize`,
  `QdrantOptions.HttpPort`, `GraphRAGBuildOptions.GenerateEntityEmbeddings` and
  `LocalSearchOptions.UseEntityEmbeddings` now say that nothing reads them and where the effective setting
  lives (the SQLite store scans every row per search — the "vector cache" it names does not exist; use
  sqlite-vec for large collections). `FluxIndexOptions` documents which of its blocks the builder reads:
  `Indexing` and `Search` are only written to (by `WithChunking`/`WithSearchOptions`), and the whole
  `RAGEnhancement` subtree is not wired to anything. The options-reachability roster now scans
  `*Configuration` and `*Defaults` types as well as `*Options`.
- Documentation examples that named API which does not exist are corrected in the README, GUIDE and
  REFERENCE (`UseOpenAIEmbedding`/`AddOpenAIEmbedding` → `AddOpenAICompatibleEmbedding`,
  `SearchOptions.MetadataFilter` → `MetadataFilters`, a GraphRAG query written against a method and type
  that never shipped, and wrong member names on `SelfRAGOptions`, `CorrectiveRAGOptions` and
  `GlobalSearchOptions`; method names on `IGraphTraversalService`, `IDynamicFusionService`,
  `IQueryTransformationService` and `ITextCompletionService`). The REFERENCE "LocalReranker" section, which
  described an options type and a registration that never existed, now documents the real
  `AddLMSupplyReranker` + `LMSupplyRerankerOptions` surface. A test checks every C# block in `README.md`
  and `docs/*.md` — option initialisers and every method call — against the public surface; the examples
  still known to be wrong are listed in it until their sections are rewritten. `docs/GUIDE.md` no longer lists chunking
  strategies the splitter does not have (`WithChunking("Sliding")` threw on `Enum.Parse`).
- Five options that were declared but never read now do what their documentation says (found by the new
  options-reachability roster, which pins that every public `*Options` property has a reader):
  - `EntityExtractionOptions.Language` — the default extractor tells the LLM the text's language and to
    return entity text exactly as written, in both the entity and the relation prompt. Pattern extraction
    is unaffected.
  - `EntityExtractionOptions.CustomPatterns` — each `key → regex` pair now extracts; matches are emitted as
    `NamedEntityType.Custom` with the key as `Subtype`, at pattern confidence, and honour `EntityTypes` and
    `MinConfidence` like built-in patterns. An invalid expression throws `ArgumentException` naming the key.
  - `GraphRAGQueryOptions.IncludeContext` — false returns `Documents` without chunk text (answer generation
    still sees it). `IncludeRelationships` — the relationships the local search traversed are returned in
    the new `GraphRAGQueryResult.Relationships` (local/hybrid scope). `IncludeCommunityContext` — false
    keeps community summaries out of the answer context and `RelatedCommunities`.
- Entity deduplication rebuilt the merged entity without `Subtype` and `ExternalLink`, so every
  `CustomPatterns` match (and any extractor-supplied subtype) arrived as a bare `Custom`. Both are carried
  now, and `Subtype` is part of the merge key.

### Added

- `GraphRAGQueryResult.Relationships`.

---

## [0.37.2]

### Fixed

- GraphRAG: `GraphRAGBuildOptions.EntityOptions` now reaches the entity extractor. It was declared but
  never read — the entity graph build composed its own `EntityExtractionOptions` from
  `EntityGraphBuildOptions` alone, so a consumer's `Language`, `UseLlm`, `CustomPatterns`,
  `IncludeContext` and `ContextWindowSize` silently stayed at their defaults. They are now the base the
  build lays its own knobs over (`MinEntityConfidence`, `MaxEntitiesPerChunk`, `ExtractRelations`
  always; `EntityTypes` when set). New `EntityGraphBuildOptions.ExtractionOptions` is the seam the
  build reads them through; consumers calling `IEntityGraphService` directly can set it themselves.
- GraphRAG: an extractor that returns fewer graphs than inputs from `ExtractBatchAsync` is rejected
  (`InvalidOperationException`) instead of the tail chunks silently indexing with no entities. The
  contract — exactly one `EntityGraph` per input, in input order; a batch may be resolved as one set —
  is now stated on the interface.
- GraphRAG: what an extractor says about an entity beyond name/type/confidence now survives to the
  graph store. `ExtractedEntity.Metadata` entries and `Subtype` (as `"subtype"`) land in
  `EntityNode.Properties` → `GraphEntity.Properties`; they were dropped at the node conversion, with
  and without cross-chunk linking.

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.23.4 -> 0.23.5, `LMSupply.Embedder` 0.65.0 -> 0.65.1, `LMSupply.Generator` 0.65.0 -> 0.65.1, `LMSupply.Reranker` 0.65.0 -> 0.65.1 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`.

---

## [0.37.1]

### Fixed

- Vector stores: an id generated for a chunk that carried none is now written onto the chunk instance in
  every store, not only returned. Qdrant already did; the SQLite, PostgreSQL and in-memory stores left the
  caller's object with an empty `Id` (the in-memory store stored a copy), so a consumer that kept the
  instance could not address the row it had just written. Pinned by two new facts in the shared
  `IVectorStore` chunk-identity contract suite. `docs/REFERENCE.md` "Chunk identity" states the rule.

---

## [0.37.0]

### Added
- **`EntityGraphBuildOptions.ReuseStoredExtractions` (default true).** `IEntityGraphService.BuildEntityGraphAsync` no longer sends a chunk to the entity extractor when the graph store already holds entities extracted from it (matched by chunk id): those entities, their provenance and their relationships are reconstituted from the store, only the remaining chunks are extracted, and an entity extracted again is joined to its stored counterpart (same normalized name and type) instead of being persisted a second time — the stored entity's provenance grows rather than being replaced. Building the same chunks twice therefore costs one round of extraction, not two; a consumer that keys its chunks deterministically pays for extraction once per passage across re-indexes. `EntityGraphStats` reports `ChunksExtracted` / `ChunksReused`. Needs a graph store; without one every chunk is extracted, as before. A chunk that was extracted before but yielded no entity leaves no trace in the store and is extracted again.

### Fixed
- **Re-indexing a chunk id on the in-memory BM25 index replaces its postings.** `BM25SparseRetriever.IndexChunkAsync`/`IndexChunksAsync` overwrote the chunk in the document index but appended a second posting per term and counted the chunk twice, so a re-indexed chunk scored with doubled term frequencies and still matched the wording it no longer had. The relational keyword indexes (SQLite, PostgreSQL) already replaced. Re-indexing an id now removes what the previous version contributed (postings, term totals, length) before indexing the new one; deleting a chunk does the same bookkeeping. `KeywordSearchChunkIdentityContractSuite` (in `FluxIndex.Core.Tests`, next to the vector-store suite) pins the contract for the in-memory and SQLite indexes.
- **The Qdrant store generates an id for a chunk stored without one.** `FluxIndex.Storage.Qdrant` keyed such a point on the hash of the empty string and returned `""` as its id, so the caller could never find or delete it by id; it now generates a GUID and returns it, as the other stores do. Found by running `VectorStoreChunkIdentityContractSuite` on a Qdrant container (the suite now also runs on a PostgreSQL container).
- **The SQLite stores now honour `DocumentChunk.Id`.** `FluxIndex.Storage.SQLite`'s sqlite-vec store (the README default) and its in-process fallback store generated a `Guid` of their own in `StoreAsync`/`StoreBatchAsync` and returned that, discarding the id the caller set — while `docs/REFERENCE.md` promised that every store keys on the caller's id (the PostgreSQL and Qdrant stores were fixed to do so in 0.28.8). A consumer that recorded the ids it wrote could therefore never find those rows again: a partial-write rollback that deletes by recorded id removed nothing, and graph provenance written with the caller's chunk ids never matched the chunks a search returned. Ids are now kept as given (an empty one is still generated and returned), and re-storing an id is an update rather than a second row on all three indexes — the row (`ON CONFLICT DO UPDATE` on the batch path), the vec0 vector (deleted and re-inserted; dropped when the re-store carries no embedding) and the FTS5 keyword index (via the existing update trigger). Existing rows are unaffected: they were keyed on the generated UUID, which reads back verbatim.
- **Re-storing a chunk id on the PostgreSQL stores is an update.** `FluxIndex.Storage.PostgreSQL`'s two vector stores added a fresh row for every `StoreAsync`/`StoreBatchAsync`, so storing an id that was already stored failed — with an EF "already being tracked" error in the scope that wrote it, or a primary-key violation from a fresh scope — even though 0.28.8 documented re-storing as an update. Both stores now update the existing row (the quantized store also drops the previous quantized embedding before re-quantizing). The integration test that carried this promise in its name stored only once; it now stores twice, through both paths.
- **Re-storing a chunk id is an update on every store — and a shared contract suite now says so.** The same call disagreed across implementations: the SQLite quantized store added a duplicate row (and treated an empty `Id` as a real id), the SDK's `InMemoryVectorStore` silently kept the first write (`TryAdd`), the SQLite stores minted their own ids and the PostgreSQL stores failed on the second write, while only Qdrant upserted. All of them now update in place, keep the document index consistent, and generate an id only when none was given. `VectorStoreChunkIdentityContractSuite` (in `FluxIndex.Core.Tests`, next to the filter-contract suite) pins the contract and runs against every in-process store; the container-backed stores carry the same cases in their integration suites.

---

## [0.36.1]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.23.3 -> 0.23.4, `FluxImprover` 0.12.5 -> 0.12.6 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.36.0]

### Fixed
- **Persisted graph entities now carry their provenance.** `EntityGraphService.PersistGraphAsync` stored each entity with empty `GraphEntity.ChunkIds` and `GraphEntity.DocumentIds`, even though the build result already held the entity↔chunk mapping. Every chunk- or document-scoped read on the graph store (`IGraphStore.GetEntitiesByChunkIdsAsync`, and any tenant/document isolation built on it) therefore matched nothing — silently, since storing succeeded and querying returned an empty list without error. Entities are now stored with the distinct chunk ids and document ids they were extracted from, so scoped queries match. (Neo4j, PostgreSQL and SQLite stores already persisted both lists; only the producer was empty.)

- **Relations no longer dangle after cross-chunk entity linking.** With `EntityGraphBuildOptions.LinkEntitiesAcrossChunks` (the default), linking replaced every per-chunk entity id with a canonical one but left the extracted relations pointing at the old ids. Every edge therefore referenced entities that did not exist: unreachable in in-memory traversal, silently dropped by Neo4j (`MATCH` on absent nodes), and rejected by the SQLite store's foreign key on persist. Relations are now re-keyed with the linked ids; a relation whose two ends merged into one entity is dropped.
- **Chunk-scoped entity lookup is exact for any graph size.** `IGraphStore.GetEntitiesByChunkIdsAsync` — the root of `LoadIndexAsync` and of every document- or tenant-scoped read — inspected only the first 1,000 entities on SQLite (`DefaultPageSize * 10`, then filtered in memory), so a graph beyond that size returned a silently shorter scope; on PostgreSQL it matched chunk ids by substring (`c1` matched an entity of `c10`). SQLite now stores the entity's chunk ids as a primitive collection in the same `chunk_ids` TEXT column (existing rows are read unchanged) and the lookup translates to `json_each` over every row; PostgreSQL uses the `jsonb ?|` element test. No schema change on either store.
- **SQLite graph store round-trips `NormalizedName`.** It was derived from `Name` on write and never read back, so an entity loaded from SQLite had an empty normalized name and matched every query in text-based entity matching.
- **`LocalSearchAsync` applies `MinEntityScore` to what it documents.** The option ("minimum entity match score") was used as a floor on the PageRank-weighted chunk score — probability mass spread over the whole graph — which filtered out every chunk of any graph beyond a few entities. It now filters on the share of query entities a chunk mentions (`EntitySearchHit.EntityMatchScore`, 0..1). Local search documents also carry their chunk `Content` (taken from the index's chunks; entity search never had the text).
- **Community membership round-trips the graph store — and persisting communities to a relational store no longer throws.** GraphRAG communities are clusters of chunks, but they were persisted with chunk ids in `GraphCommunity.EntityIds`. On the PostgreSQL and SQLite stores the member table has a foreign key to the entities table, so every `BuildIndexAsync` that detected a community failed at `StoreCommunityAsync` with a foreign-key violation (SQLite error 19); the Neo4j store tried to link each community to *entity* nodes by those chunk ids (a `MATCH` that never matched, so it silently created nothing); and the SQLite `GetTopCommunitiesAsync` returned communities with an empty member list. `GraphCommunity` now carries both memberships honestly: `EntityIds` are the entities extracted from the community's chunks (derived from the entity↔chunk mappings at persist; this is what the member tables and Neo4j `CONTAINS` edges store, and what `GetCommunitiesForEntityAsync` answers from), and `ChunkIds` are the chunks the community groups. The Neo4j store persists `ChunkIds` as a node property; the PostgreSQL and SQLite stores add a `chunk_ids` column to the community table (an EF primitive collection, so `GetCommunitiesByChunkIdsAsync` translates to an array overlap / `json_each` rather than a substring match — the first graph column shaped that way). SQLite returns members from every community read and replaces a re-stored community's member set; Neo4j still reads the legacy `entityIds` property as chunk ids for nodes written before this version.

### Changed
- **Schema provisioning adds columns the model gained since the database was created** (`FluxIndex.Storage.PostgreSQL`, `FluxIndex.Storage.SQLite`). Start-up provisioning used to know only tables: a database with every owned table present was reported up to date even when the current model declared a column it lacked, and the first write then failed with "no such column" / `42703`. When every owned table exists, provisioning now compares the model's columns with the database's and issues `ALTER TABLE … ADD COLUMN` for the ones it can add without inventing data for existing rows — nullable columns, or columns with a default (the DDL comes from the provider's own migrations SQL generator, so it matches what a fresh create would produce). A missing column that is required with no default is refused with the same actionable error as a half-built schema; columns the database has that the model does not, and type differences, are left alone. This is what lets an existing entity-graph database pick up the community `chunk_ids` column above. It runs on every provisioning path (vector store, quantized store, keyword index, graph, entity graph, semantic cache) and respects the existing per-component auto-migration opt-outs.
- Pattern-only entity extraction (no `ITextCompletionService`, or `EntityExtractionOptions.UseLlm = false`) now logs a warning once per service instance: its named-entity pattern recognises Latin capitalised sequences only, so a corpus without letter case yields no organisations or people — previously that degradation was silent. `UseLlm` documents the limitation.

### Added
- `IGraphRAGService.LoadIndexAsync(chunks, options)` — reconstructs a `GraphRAGIndex` for the given chunks from the persisted `IGraphStore`, so an index built in one process can be queried and updated (`UpdateIndexAsync`) after a restart. The chunks define the scope: entities extracted from them, relationships between those entities, every persisted community that groups at least one of them (with its summary, so `GlobalSearchAsync` works on a loaded index), and the chunks themselves as `GraphRAGIndex.Chunks`. Requires a graph store (throws `InvalidOperationException` otherwise). `GraphRAGLoadOptions.LoadRelationships` skips the per-entity relationship reads.
- `IGraphStore.GetCommunitiesByChunkIdsAsync(chunkIds)` — every community that groups at least one of the given chunks; the community-side counterpart of `GetEntitiesByChunkIdsAsync`. Implemented by the Neo4j, PostgreSQL and SQLite stores (a new interface member: custom `IGraphStore` implementations must add it).
- `EntityChunkMapping.DocumentId` — the document of the mapped chunk, filled during `BuildEntityGraphAsync` and preserved through cross-chunk entity linking and graph merging. Consumers reading `EntityGraphResult.ChunkMappings` get document provenance without a second lookup.

---

## [0.35.4]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.23.2 -> 0.23.3, `FluxImprover` 0.12.4 -> 0.12.5, `LMSupply.Embedder` 0.64.0 -> 0.65.0, `LMSupply.Generator` 0.64.0 -> 0.65.0, `LMSupply.Reranker` 0.64.0 -> 0.65.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.35.3]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.23.1 -> 0.23.2, `FluxImprover` 0.12.3 -> 0.12.4, `LMSupply.Embedder` 0.63.0 -> 0.64.0, `LMSupply.Generator` 0.63.0 -> 0.64.0, `LMSupply.Reranker` 0.63.0 -> 0.64.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.35.2]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.23.0 -> 0.23.1, `FluxImprover` 0.12.2 -> 0.12.3, `LMSupply.Embedder` 0.62.0 -> 0.63.0, `LMSupply.Generator` 0.62.0 -> 0.63.0, `LMSupply.Reranker` 0.62.0 -> 0.63.0, `WebFlux` 0.7.0 -> 0.7.1 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.35.1]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.22.13 -> 0.23.0, `FluxImprover` 0.12.1 -> 0.12.2, `LMSupply.Embedder` 0.61.0 -> 0.62.0, `LMSupply.Generator` 0.61.0 -> 0.62.0, `LMSupply.Reranker` 0.61.0 -> 0.62.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.35.0]

### Added
- `QdrantOptions.FailOnCollectionMismatch` (default `false`) — turns the new startup warning below
  into a startup failure, for deployments that would rather not start than serve empty results.
- `IVectorStore.GetChunkIdsByDocumentIdAsync` — returns a document's chunk ids without loading their
  content, metadata or embeddings. Callers that resolve which points belong to a document (a
  generation to delete, an indexed-state check) used to fetch every chunk in full and discard all but
  the id. The interface carries a default implementation deriving the ids from
  `GetByDocumentIdAsync`, so every store answers it; Qdrant overrides it to select only the chunk-id
  payload field and no vectors.
- `QdrantOptions.ScrollPageSize` (default `256`) — bounds the size of a scroll response. A value
  below 1 is rejected at construction rather than silently substituted.

  > ⚠️ **If you mock `IVectorStore` in your tests, stub `GetChunkIdsByDocumentIdAsync` too.** The
  > default implementation makes this additive for real stores, but a mocking framework intercepts
  > the member instead of running the default — so a substitute that stubs only
  > `GetByDocumentIdAsync` returns an empty id list here, and any delete path driven by it silently
  > stops deleting. This repository's own test harnesses hit exactly that; four suites needed the
  > extra stub.

### Fixed
- **Concurrent keyword indexing no longer deadlocks against itself.**
  `RelationalKeywordSearchService.IndexChunksAsync` upserted term rows one statement at a time, in
  whatever order each chunk's token stream produced them. An upsert holds that row's lock for the
  rest of the transaction, so two transactions indexing different documents that share vocabulary
  acquired the same rows in opposite orders — the textbook deadlock, and one that needs no unusual
  input: any two documents in the same language share common words, so it becomes near-certain as
  concurrency grows. Reported from a deployment with four indexing workers where it fired
  continuously, and each occurrence failed the entire indexing job because nothing retried
  PostgreSQL's `40P01`.
  The term rows a transaction *upserts* are now acquired in one globally sorted pass before any chunk
  is written — batch-wide, not per chunk, since the transaction spans the batch. That removes the
  first reported cycle (two concurrent upserts) outright.
  The second reported cycle is **retried, not eliminated**: the document-frequency recompute updates
  rows for terms whose postings are being *replaced*, which are not all in this run's upsert set, and
  the database chooses its own row order for that statement. A serialization failure (`40P01`/
  `40001`) now retries the whole transaction with jittered backoff instead of failing the job, so it
  surfaces as a `Warning` log line under load rather than a dead job. Backends whose driver signals
  contention differently can override `IsTransientConcurrencyFailure`.
  Consumers that dropped indexing concurrency to 1 can raise it again — expect occasional retry
  warnings rather than none. Moving the recompute out of the indexing transaction would remove the
  remaining contention and is proposed separately.
- **Reading a document from Qdrant no longer fails once its chunks exceed the gRPC receive limit.**
  `GetByDocumentIdAsync` issued a single unpaged scroll with `limit: 10000`, requesting full payload
  *and* vectors, so the response grew with the document rather than with a page size. Past the
  channel's 4 MB default the call threw `ResourceExhausted`, and `QdrantOptions` exposed no way to
  raise the limit — an undocumented ceiling on indexable document size, reached by an ordinary
  few-MB spreadsheet. The same call also truncated silently at 10 000 chunks. It now pages through
  `NextPageOffset`, so response size is bounded by `ScrollPageSize` and no document is truncated.
  A scroll whose reported next offset does not advance now throws instead of looping forever.

### Changed
- **Qdrant now warns when it is about to serve an empty collection while a sibling of the same base
  name holds data.** Changing `CollectionNamingStrategy`, or the bound embedding identity, resolves
  the same logical index to a different collection name. The store used to create the new (empty)
  collection, log "ready", and return zero results for every search while the previous collection
  still held the data — nothing threw, so the deployment looked healthy. The check is the emptiness
  of the collection being served, not merely the existence of siblings: one collection per embedding
  model (what `ModelFingerprint` is for) never warns, the warning survives a restart, and it stops on
  its own once the new collection is indexed.
- `NamingStrategy` documentation now states that changing it means re-indexing or migrating
  explicitly; it previously said only that `ModelFingerprint` was recommended.
- Corrected the collection-initialization failure log, which still claimed the store was "assuming it
  exists" after that behaviour was replaced by propagating the failure.

---

## [0.34.2]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.22.12 -> 0.22.13, `FluxImprover` 0.12.0 -> 0.12.1, `LMSupply.Embedder` 0.60.0 -> 0.61.0, `LMSupply.Generator` 0.60.0 -> 0.61.0, `LMSupply.Reranker` 0.60.0 -> 0.61.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.34.1]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.22.11 -> 0.22.12, `FluxImprover` 0.11.9 -> 0.12.0, `LMSupply.Embedder` 0.59.1 -> 0.60.0, `LMSupply.Generator` 0.59.1 -> 0.60.0, `LMSupply.Reranker` 0.59.1 -> 0.60.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.34.0]

### Added
- `FluxIndex.Integrations.FluxImprover`: `ContextualEnrichmentServiceWrapper` now also implements FluxIndex.Core's
  `IContextualEnrichmentService` (`GenerateContextAsync` / `GenerateContextBatchAsync`, one context per chunk in order),
  and `AddContextualEnrichmentWrapper()` registers it under that port as well. Consumers that hold plain chunk text —
  FluxFeed's ingestion pipeline, the FileFlux integration's document pipeline — can now get FluxImprover-backed
  contextual retrieval by registering FluxImprover plus this wrapper, with no FluxImprover types in their own code.
  Previously the Core port had no shipped implementation at all.

### Changed
- `FluxIndex.Core`: the three no-op defaults that shipped under "Mock" names in the production assembly are renamed
  — `MockContextualEnrichmentService` → `NoOpContextualEnrichmentService`, `MockQAGenerationService` →
  `NoOpQAGenerationService`, `MockTextCompletionService` → `NoOpTextCompletionService` (source-breaking rename;
  0.x). `FluxIndex.Integrations.FileFlux.AddDocumentProcessingPipeline()` now registers them with `TryAdd`, so a real
  implementation the consumer registered first is no longer shadowed by the no-op default (it used `Add`).

---

## [0.33.1]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.22.9 -> 0.22.11, `FluxImprover` 0.11.7 -> 0.11.9, `LMSupply.Embedder` 0.59.0 -> 0.59.1, `LMSupply.Generator` 0.59.0 -> 0.59.1, `LMSupply.Reranker` 0.59.0 -> 0.59.1 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.33.0]

### Added
- `ITextAnalyzer` (`FluxIndex.Core`): the keyword (BM25) index's notion of a term — tokenization,
  stop words and minimum token length as one unit — is now injectable. `RelationalKeywordSearchService`
  and both backends (`SQLiteKeywordSearchService`, `PostgresKeywordSearchService`) take an optional
  analyzer (constructor parameter; the DI registrations resolve a registered `ITextAnalyzer`), and use
  the same instance on the index path and the query path. `DefaultTextAnalyzer` is the previous
  behaviour, unchanged; `CjkBigramTextAnalyzer` is a dependency-free opt-in for Korean/Japanese/Chinese
  text that emits overlapping character bigrams for CJK runs so a bare stem matches its inflected forms.
  Before this the analyzer was a private static: a CJK consumer could not change it without
  re-implementing the whole `IKeywordSearchService`.

## [0.32.1]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.22.8 -> 0.22.9, `FluxImprover` 0.11.6 -> 0.11.7, `LMSupply.Embedder` 0.58.0 -> 0.59.0, `LMSupply.Generator` 0.58.0 -> 0.59.0, `LMSupply.Reranker` 0.58.0 -> 0.59.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.32.0]

### Added
- `INativeHybridSearch.HybridSearchAsync` takes a `filters` parameter (same vocabulary as
  `IVectorStore.SearchAsync`) and `SQLiteVecVectorStore` applies it to **both** legs before fusion —
  the vec leg through its existing over-fetch window, the FTS5 leg on the matched rows. A scoped
  hybrid request is now answered by the fused ranking of the in-scope chunks; before, the native path
  could only run unscoped, so a consumer that needed a document-id scope had to give up fusion
  entirely. Breaking for external implementers of the interface (one parameter, optional at the
  call site); the only implementation ships in this repository.
- `FluxIndex.Providers.LMSupply`: `AddLMSupplyEmbedding`, `AddLMSupplyReranker` and `AddLMSupplyTextCompletion`
  now register **lazily loading** services — building the container and resolving the service never
  loads or downloads a model. The load happens on first use, with the caller's `CancellationToken`, or
  at host start with `WarmUpOnStart`. New `Action<…Options>` overloads expose `Progress`
  (`IProgress<DownloadProgress>`), `LoadTimeout`, `WarmUpOnStart` and the LMSupply loader options;
  `LMSupplyEmbeddingOptions` adds `Dimensions`/`ModelName` to announce the identity of a non-catalog
  model before it is loaded (verified against the loaded model). The services implement
  `ILazilyLoadedModel` (`IsLoaded`, `EnsureLoadedAsync`). Previously the DI factories blocked on
  `CreateAsync(...).GetAwaiter().GetResult()` inside container resolution — no progress, no
  cancellation, a deadlock candidate under a `SynchronizationContext`, and load failures surfaced as
  "Error while validating the service descriptor".

### Behaviour change
- `FluxIndex.Providers.LMSupply`: for **catalog** models (`default`, `fast`, `quality`, `large`, …)
  the embedding identity is announced from the LMSupply registry before the load, so `BindIdentity`
  and collection naming work exactly as before without loading anything. For a HuggingFace repo id or a
  local path the dimension is not known up front: `GetEmbeddingDimension()`/`GetIdentity()` throw an
  actionable `InvalidOperationException` until the model is loaded (`EnsureLoadedAsync`,
  `WarmUpOnStart`) or `LMSupplyEmbeddingOptions.Dimensions` is set. `FluxIndexContext` builds its
  indexer eagerly, so such consumers must warm up or announce the dimension before `Build()`.

### Fixed
- `FluxIndex.Storage.SQLite`: the hosted startup initializer registered by `AddSQLiteVecVectorStore`
  required an embedding identity before any consumer could bind one. With the default
  `FallbackToInMemoryOnError = true` every host start logged an initialization error and a misleading
  "continuing in fallback mode"; with `false` the host failed to start with
  "EmbeddingFingerprint is required for vec table naming". The identity-dependent part of startup
  (legacy vec table migration, vec0 table creation, warmup) is now deferred until the first access
  after `IVectorStore.BindIdentity`, where it already ran for late-bound scopes. The native extension is
  still validated at startup, so a missing sqlite-vec library surfaces as before. Consumers that set
  `SQLiteVecOptions.EmbeddingFingerprint` explicitly keep the eager startup path.
- `FluxIndex.Storage.SQLite`: a store used without the hosted initializer (plain `ServiceCollection`, no
  host start) created the vec0 virtual table before the EF model tables, so `EnsureCreated` saw a
  database with tables and skipped them — the first write failed with "no such table: vector_chunks".
  The store now creates the model tables first on its own first access, and repairs a database already
  left in that state.

---

## [0.31.2]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.22.7 -> 0.22.8, `FluxImprover` 0.11.5 -> 0.11.6, `LMSupply.Embedder` 0.57.0 -> 0.58.0, `LMSupply.Generator` 0.57.0 -> 0.58.0, `LMSupply.Reranker` 0.57.0 -> 0.58.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.31.1]

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.22.6 -> 0.22.7, `FluxImprover` 0.11.4 -> 0.11.5, `LMSupply.Embedder` 0.56.0 -> 0.57.0, `LMSupply.Generator` 0.56.0 -> 0.57.0, `LMSupply.Reranker` 0.56.0 -> 0.57.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

---

## [0.31.0]

### Fixed
- `FluxIndex.Storage.SQLite`, `FluxIndex.Storage.PostgreSQL`: deleting a chunk in the same scope it
  was stored in threw `InvalidOperationException` ("another instance with the same key value is
  already being tracked"). Same root cause as the update defect below, in the opposite direction:
  the delete paths read through a `NoTracking` query and then called `Remove`, which tries to attach
  the detached instance and collides with the one a preceding `Store` left in the change tracker.
  Every delete path across the five vector stores — by id, by document id, by filter, and clear —
  now reads with `AsTracking()`.
- `FluxIndex.Storage.SQLite`, `FluxIndex.Storage.PostgreSQL`: `UpdateAsync` returned `true` while
  persisting nothing. These contexts are registered with `QueryTrackingBehavior.NoTracking` for read
  performance, so the entity the method queried was never in the change tracker and
  `SaveChangesAsync` found no changes to write — the row was never touched, and no error said so.
  Affected `SQLiteVecVectorStore.UpdateAsync`, `SQLiteQuantizedVectorStore.UpdateAsync` and
  `UpdateQuantizedEmbeddingAsync`, and the same two methods on
  `PostgreSQLQuantizedVectorStore`; a re-quantized vector was discarded the same way a metadata
  edit was. The lookup in each now runs `AsTracking()`.
- `FluxIndex.Storage.SQLite`, `FluxIndex.Storage.PostgreSQL`: the non-quantized
  `SQLiteVectorStore.UpdateAsync` and `PostgreSQLVectorStore.UpdateAsync` had the same shape and
  worked only because their contexts happen to be registered with tracking left on. They no longer
  depend on that: enabling `NoTracking` on those registrations is now a performance decision rather
  than a silent data-loss one.

### Changed
- **`IVectorStore.UpdateAsync` can now return `false`.** Previously it returned `true` whenever the
  chunk existed, regardless of whether anything was written. It now reports `false` when the update
  had changes that reached no row. An update whose values are identical to what is stored is still
  a successful no-op and returns `true`. Callers that ignored the return value should start reading
  it — a discarded `false` is the shape that turned this defect into a silent one downstream.

### Dependencies
- `LMSupply.Embedder`, `LMSupply.Generator`, `LMSupply.Reranker`: 0.55.4 → 0.56.0.

---

## [0.30.0]

### Added
- `FluxIndex.Core`: `EmbeddingIdentity.Revision` — an optional, opaque pipeline revision supplied by
  the consumer. `Provider + Model` cannot express the case where the *same* model starts producing
  vectors that are incomparable with what is already stored: a tokenizer fix, a pooling or
  normalisation change, a quantization switch, or an ONNX re-export all change the numbers while
  leaving both names untouched. Raising `Revision` declares a new vector space, so the fingerprint
  changes and the collections and tables named after it separate instead of silently mixing.
- `FluxIndex.Core`: `EmbeddingServiceBase.Revision` (an `init` property) and
  `EmbeddingServiceBase.GetRevision()` (a `protected virtual` seam) — two ways to declare it, because
  every embedding service this repository ships is `sealed` and so cannot override anything. Set the
  property in an object initializer, or override the method when the revision is computed.
  `GetIdentity()` carries whichever is used; services that override `GetIdentity()` outright are
  unaffected.
- `FluxIndex.Providers.LMSupply`: `LMSupplyEmbeddingService.CreateAsync` and `AddLMSupplyEmbedding`
  take an optional `revision`, so the revision can be set through the registration a consumer
  already uses rather than by constructing the service by hand.

### Changed
- `LMSupplyEmbeddingService.CreateAsync`'s parameter order — `revision` sits before
  `cancellationToken`, which analyzer rules require to come last. Callers passing the token
  positionally must name it; callers using the default are unaffected.

### Compatibility
- Additive apart from that parameter order. With no `Revision` set the fingerprint is byte-identical to every previous release, so
  no existing collection or table is renamed by upgrading — a regression test pins the pre-revision
  hash to keep it that way.
- A blank or whitespace-only `Revision` normalises to unset, so a configuration binding that yields
  an empty value cannot split a vector space by accident.
- `Revision` is case-sensitive, unlike `Provider` and `Model`. It is an opaque token whose whole
  purpose is to distinguish; folding case would let two different revisions share one fingerprint.

---

## [0.29.2]

### Fixed
- `FluxIndex.Storage.PostgreSQL`: `PostgreSQLQuantizedVectorStore` now applies a metadata filter as
  part of the query instead of over rows already selected by distance, so a scope narrow relative
  to the table no longer loses matches. `0.29.1` made that loss visible; this removes it. The
  warning it added is gone with it — there is no filter shape left that runs after the candidate
  window, and a warning that can never fire is worse than none.
- The predicate builder both PostgreSQL stores use now lives in one place, so they cannot drift on
  where a filter runs. Behaviour of `PostgreSQLVectorStore` is unchanged; it already pushed the
  filter down.

> `FluxIndex.Storage.SQLite`'s `SQLiteVecVectorStore` still filters after its KNN step and still
> warns — vec0 can only pre-filter on columns declared on the virtual table, which is a schema
> change rather than a query change.

---

## [0.29.1]

### Changed
- `FluxIndex.Storage.PostgreSQL`: `PostgreSQLQuantizedVectorStore` now reports the same recall loss
  `0.29.0` made visible in the SQLite vec store. It fetches a `topK * 3` candidate window from the
  database and applies the metadata filter afterwards — the metadata is a jsonb column the query
  does not constrain — so a scope narrow relative to the table loses matches that never enter the
  window. A search that requested `topK`, ran a filter, saw a full window and still came up short
  now warns; one that fills its request, or has no filter, stays silent. The non-quantized
  `PostgreSQLVectorStore` is unaffected: it constrains the query itself, so its filter is a real
  pre-filter. An audit of every `IVectorStore` implementation found these two stores are the only
  ones that filter after a candidate window; Qdrant filters server-side and the remaining SQLite
  stores filter before any trim.

---

## [0.29.0]

> Numbered as a minor, not a patch, to correct the version line: `0.28.9` removed a public enum
> member (`CollectionNamingStrategy.DimensionSuffix`), which is a breaking change and should not
> have shipped in a patch. `FluxIndex.Storage.Qdrant` `0.28.9` is being unlisted; the other packages
> at that version are unaffected and stay available. Nothing in this release re-adds the removed
> member — migrate to `ModelFingerprint` as `0.28.9` describes.

### Changed
- `FluxIndex.Storage.SQLite`: a vector search whose metadata filter could not be satisfied from
  within the candidate window now logs a warning instead of returning a quietly short result. The
  filter is applied after the KNN step — the metadata lives in `vector_chunks`, not in the vec0
  table — so a scope narrow enough relative to the store loses matches that never enter the
  `topK * 3` window. That recall loss was previously indistinguishable from "the store held
  nothing else". A search that fills its request, or one with no filter, stays silent.
- `FluxIndex.Storage.SQLite`: corrected the comment claiming vec0 cannot filter on metadata. It can
  — sqlite-vec has supported metadata columns and partition keys since 0.1.6 and this project pins
  0.1.7 — but only for columns declared on the vec0 table, which this store does not do. Pushing
  the filter down properly is a schema change and is tracked separately.

---

## [0.28.9]

### Removed
- `FluxIndex.Storage.Qdrant`: `CollectionNamingStrategy.DimensionSuffix`, deprecated in `0.28.8`,
  is gone. Set `NamingStrategy` to `ModelFingerprint` (the default) instead — it distinguishes
  models that share a dimension, and until an embedding identity is bound it falls back to the same
  `{baseName}_{dimension}` collection name the removed member produced, so an existing deployment
  keeps addressing its collections. The remaining members keep the numeric values they shipped with
  (`Fixed = 1`, `ModelFingerprint = 2`) so a configuration that binds this strategy as a number does
  not silently change meaning.

### Fixed
- `FluxIndex.Storage.PostgreSQL`, `FluxIndex.Storage.SQLite`: `AddPostgreSQLQuantizedVectorStore`
  and `AddSQLiteQuantizedVectorStore` now register a default `IVectorQuantizer`, so a quantized
  store can be activated from the registration alone. Both stores take the quantizer as a required
  constructor dependency and neither registration supplied one, which made direct registration fail
  with `Unable to resolve service for type 'IVectorQuantizer'`; the SDK builder resolved it as
  optional and so never supplied one either. The default is the library's existing
  `QuantizationOptions` default (`ScalarInt8`), not a new choice, and an explicit
  `AddVectorQuantization` still wins in either registration order.

---

## [0.28.8]

### Fixed
- `FluxIndex.Storage.PostgreSQL`: `AddPostgreSQLVectorStore` now registers its schema initializer,
  so a direct registration provisions the pgvector extension and tables like the quantized overload
  already did. Previously only the SDK builder registered it, and a direct caller's first write
  failed with `relation "vectors" does not exist`. The `EnableAutoMigration` opt-out moved onto the
  registration as an optional `enableAutoMigration` argument (default true).
- `FluxIndex.Storage.PostgreSQL`: installing the pgvector extension no longer leaves the store
  unable to write on a fresh database. The extension was created through the same data source the
  store then wrote with, and Npgsql had already cached that database's type catalogue from before
  `vector` existed, so the first write failed with "Cannot resolve 'vector' to a fully qualified
  datatype name". It is now installed over a separate short-lived connection.
- `FluxIndex.Storage.PostgreSQL`: the vector stores now honour `DocumentChunk.Id`. `StoreAsync`
  previously generated an id of its own and returned that, discarding the one the caller set, while
  every read path ran the caller's id through `Guid.Parse` and threw on anything that was not a
  UUID — so a consumer with its own id scheme could neither store under it nor look a chunk up by
  it. Ids are now kept as given (a non-UUID one is mapped to a deterministic key and preserved
  alongside the row), and reads return the id that was stored. A UUID id is still used as the key
  verbatim, leaving existing rows readable.
- `FluxIndex.Storage.Qdrant`: chunks whose `Id` is not a UUID can now be stored, retrieved and
  deleted. Qdrant accepts only UUIDs or unsigned integers as point ids, and the adapter previously
  handed `DocumentChunk.Id` straight to `Guid.Parse` — so any consumer using its own id scheme hit
  a `FormatException` on the first store, even though every other `IVectorStore` implementation
  treats the id as a free string. A non-UUID id is now mapped to a deterministic name-based UUID
  (RFC 9562 version 8) and the original id is preserved in the point payload, so reads return the
  id the caller supplied. UUID ids are still used verbatim, leaving existing collections readable.

### Changed
- Re-pinned sibling package(s) `FileFlux` 0.22.2 -> 0.22.6, `FluxImprover` 0.11.2 -> 0.11.4, `LMSupply.Embedder` 0.55.2 -> 0.55.4, `LMSupply.Generator` 0.55.2 -> 0.55.4, `LMSupply.Reranker` 0.55.2 -> 0.55.4 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`.

---

## [0.28.7]

### Fixed
- Re-pinned `LMSupply.Embedder`/`.Generator`/`.Reranker` from `0.55.0` to `0.55.2` — picks up the
  embedder's provider fallback on inference timeout and the tokenizer fix that stops padding every
  input to the model maximum (a short sentence on CPU went from ~38s to ~0.4s).
- CLI (`FluxIndex.CLI` 0.4.1): `fluxindex <file>` now exits with code 1 when processing fails
  (previously 0, so scripted callers saw a silent success); failures from local embedding print a
  hint with actions that exist on this CLI (`--no-embeddings`, retry, `fluxindex set GPUSTACK_*`);
  the banner version is read from the assembly instead of a hard-coded string; the output summary
  lists what is actually in the `-o` directory; settings moved to `~/.fluxindex/settings.json`
  (legacy `~/.vault/settings.json` is still read until the next save).

### Deprecated
- `CollectionNamingStrategy.DimensionSuffix` is now documented as planned for removal in a future
  release. It has been superseded by `ModelFingerprint` (the default since the deprecation) for
  every consumer we are aware of; if your configuration sets `NamingStrategy` to `DimensionSuffix`
  explicitly, migrate before upgrading past that release.

---

## [0.28.6]

### Fixed
- Re-pinned `LMSupply.Embedder`/`.Generator`/`.Reranker` from `0.54.0` to `0.55.0` — `0.54.0` was
  never actually published to nuget.org for these packages (their published history jumps
  `0.45.0` -> `0.55.0`), so `0.28.5`'s restore failed with NU1603 escalated to error and no nupkg
  for `0.28.5` was ever produced.

---

## [0.28.5]

### Changed
- Re-pinned `LMSupply.Embedder`/`.Generator`/`.Reranker` from `0.42.10` to `0.54.0` — patch
  re-consumption of already-consumed sibling packages (cold-GPU-kernel-hang protection propagated
  to all ONNX-backed lm-supply modules). No source changes.

---

## [0.28.4]

### Changed
- Re-pinned `FileFlux` from `0.22.1` to `0.22.2` — patch re-consumption of an already-consumed
  sibling package. No source changes.

---

## [0.28.3]

### Changed
- Re-pinned `FluxImprover` from `0.11.1` to `0.11.2` — patch re-consumption of an already-consumed
  sibling package. No source changes.

---

## [0.28.2]

### Changed
- Re-pinned already-consumed sibling packages to their latest patch/minor: `FileFlux`
  (`0.20.1`→`0.22.1`), `FluxGuard.Remote` (`0.13.0`→`0.14.1`), `LMSupply.Embedder`/`.Generator`/
  `.Reranker` (`0.42.2`→`0.42.10`). No source changes.

---

## [0.28.1]

### Changed
- Updated `ModelContextProtocol`/`ModelContextProtocol.AspNetCore` to 2.2.0 (previously 1.3.0 /
  0.9.0-preview.2). No public API changes — this package's MCP server only registers tools over
  stdio and does not use any of the capabilities the 2.0 protocol revision deprecated (roots,
  sampling, logging) or `DiscoverResult.ServerInfo`.

---

## [0.28.0]

### Added
- `Retriever` accepts an optional `FluxGuard.Remote.RAG.IRAGSecurityPipeline` (opt-in RAG
  poisoning / indirect prompt injection detection). When supplied, `SearchAsync(query,
  SearchOptions?, ...)` validates retrieved documents before returning them — a document the
  pipeline suggests blocking is dropped from the result set, one it suggests sanitizing has its
  content replaced with the pipeline's sanitized version. Off by default (constructor parameter
  defaults to `null`); no behavior change for existing consumers. Applied to the unified
  `SearchAsync(SearchOptions)` entry point only, not every legacy overload.

---

## [0.27.3]

### Changed
- Bumped `FileFlux` 0.19.1 → 0.20.1, `FluxCurator` 0.7.5 → 0.8.1, `FluxImprover` 0.9.1 → 0.11.1,
  `LMSupply.*` 0.42.1 → 0.42.2, `WebFlux` 0.6.0 → 0.7.0 (dependency freshness, no known breaking
  changes consumed).

---

## [0.27.2] - 2026-08-23

### Fixed
- `tests/Directory.Build.props` and `samples/Directory.Build.props` did not chain-import the root
  `Directory.Build.props`, so MSBuild's nearest-file lookup silently shadowed every root-level
  setting (local package feed, analyzer configuration, package metadata) for all test and sample
  projects. Added the explicit import.

### Changed
- `LMSupply.Embedder`/`.Generator`/`.Reranker` dependencies raised `0.34.17` → `0.42.1`.
- `FileFlux` dependency raised `0.11.0` → `0.19.1`.

---

## [0.27.1] - 2026-08-17

### Fixed

- Test-only dependency: lifted the `Testcontainers` family (`Testcontainers`,
  `.Neo4j`, `.PostgreSql`, `.Qdrant`, `.Redis`) from 4.10.0-4.12.0 to 4.14.0.
  Earlier versions pull a transitive `SSH.NET` release with a High-severity
  advisory (GHSA-q939-rpr3-3284); 4.14.0 is the first to move past it.
  Does not affect any published package — `Testcontainers.*` is referenced
  only by test projects.

---

## [0.27.0] - 2026-08-06

### Changed — `FluxIndex.Integrations.WebFlux` 의 웹 청크 경계가 바뀐다

WebFlux 핀을 `0.5.3` → `0.6.0` 으로 올렸다. WebFlux 가 범용 청킹을 FluxCurator 에 위임하면서
계약 위반 몇 건이 해소됐고, 그 결과 **웹 콘텐츠의 청크 경계가 달라진다.**

**웹 인덱싱을 쓰는 소비자는 재인덱싱이 필요하다.** 특히:

- `MaxChunkSize` 가 문서대로 **토큰 수**로 동작한다. 이전에는 문자 수로 강제됐으므로, 같은
  설정에서 청크가 더 커진다(영어 문서는 대략 4배). 오차 크기가 언어마다 달랐으므로 변화 폭도
  콘텐츠에 따라 다르다.
- `ChunkOverlap` 이 실제로 적용된다. 이전에는 어떤 전략도 이 값을 읽지 않아 겹침이 0 이었다.
- `Semantic` 전략은 임베더를 요구한다. 이전에는 조용히 문단 분할로 폴백했다.

**파일 인덱싱 경로(FileFlux)는 영향 없다.** 이 변경은 `Integrations.WebFlux` 에 한정된다.
전략 이름은 그대로이므로 `ChunkingStrategyType` 로 고르는 코드는 수정이 필요 없다.
상세는 WebFlux `CHANGELOG.md` 의 `0.6.0` 항목.

---

## [0.26.0] - 2026-08-06

### Added

- **The keyword (sparse) leg has storage options of its own** — `FluxIndexOptions.KeywordSearch`
  (`Provider`, `ConnectionString`, `UseVectorStoreConnection`, `EnableAutoMigration`).

  It was the only storage component without them: registration lived inside the vector store's
  provider block, so the leg could only ever be placed wherever the vectors were. The split
  deployment this library itself recommends — vectors in Qdrant, metadata in PostgreSQL — therefore
  had no way to express a persistent keyword index at all, and consumers responded by copying the
  registration out of the library.

  ```csharp
  var builder = FluxIndexContext.CreateBuilder()
      .UseQdrant("localhost").AddQdrantStorage()
      .UseOpenAIEmbedding(apiKey);

  builder.Options.KeywordSearch.Provider = "PostgreSQL";
  builder.Options.KeywordSearch.UseVectorStoreConnection = false;
  builder.Options.KeywordSearch.ConnectionString = metadataConnectionString;

  var context = builder.AddPostgreSQLStorage().Build();   // contributes the keyword leg only
  ```

  The provider must be named. Left unset the leg follows the vector store, which is what the old
  gate did — and in this configuration that means Qdrant, so `AddPostgreSQLStorage()` would
  contribute nothing.

- **`services.AddPostgreSQLKeywordSearch(connectionString, autoMigrate)`** — registers the leg
  directly on a service collection. `IKeywordSearchService` is resolved from consumers' own root
  containers by pipelines built on top of FluxIndex, not only from inside `FluxIndexContext`, and
  without a public entry point those consumers had to reproduce the singleton lifetime the indexer
  and the retriever depend on sharing.

- **Naming a keyword provider whose package is not registered now throws at `Build()`**, with the
  call that is missing. Falling through to the in-memory index is the silent failure this option
  could otherwise introduce: hybrid search goes on returning vector-only results, so nothing
  reports the loss and the sparse leg is simply empty after every restart. Same guard the vector
  store and the cache already had.

### Compatibility

Defaults reproduce the previous behavior, with one deliberate exception: asking for a connection of
the leg's own (`UseVectorStoreConnection = false`) and not supplying one now throws at registration.
It previously registered and failed later — or worse, indexed into whatever database the vector
store happened to use. An unset `Provider` means "follow the vector store", which is what the
vector-gated registration did. `EnableAutoMigration` is nullable on
purpose: the two backends did not agree before — PostgreSQL gated keyword provisioning on
`VectorStore.EnableAutoMigration` while SQLite always provisioned — so a non-nullable `true` would
have turned DDL back on for a caller who had switched it off, which is the one case that flag
exists to prevent.

---

## [0.25.0] - 2026-08-05

*Backfilled — this release shipped without a changelog entry.*

### Added

- **The keyword leg takes metadata filters**, in the same vocabulary as the vector store, so one
  filter object scopes both legs of a hybrid index: `KeywordSearchOptions.MetadataFilter` and
  `IKeywordSearchService.DeleteByFilterAsync`, symmetric with `IVectorStore.DeleteByFilterAsync`.

  Filter dimensions are stored in a normalized side table rather than through JSON functions, so
  the predicate is an ordinary `EXISTS (…)` on both backends — no per-dialect hook, and the
  backend-equivalence guarantee introduced in 0.23.0 stays intact. Existing rows can be backfilled
  without reindexing; the original `metadata` column is retained for return values.

### Fixed

- **A scope declared on a hybrid query reached the keyword leg.** `HybridSearchService` did not
  forward the filter to the keyword leg, and the SDK's `Retriever` dropped `MetadataFilters`
  entirely on the hybrid path while applying them on the vector-only path — so *enabling hybrid
  search silently widened the scope*. Qdrant's hybrid path filtered neither leg. Scope is now
  declared once per query and reaches both legs.
- **`DocumentIdFilter` was ignored by the in-memory store.**
- **`EnablePhraseSearch` was ignored on the hybrid path.**

---

## [0.24.0] - 2026-08-02

### Changed

- **`Flux.Abstractions` is no longer built here.** It now ships from its own repository and
  versions independently, starting at `0.24.0`. This repository consumes it as a package like
  any other consumer.

  The contract was previously produced here while also being consumed by FileFlux, FluxCurator,
  FluxImprover and WebFlux — all four of which this repository consumes in turn. That is a cycle
  in the dependency graph, and its practical effect was that those four could only ever reference
  a contract release older than the one they were being built against: raising the pin required a
  new release here, which immediately made the pin stale again. Freezing those pins was what kept
  the graph buildable.

  Nothing about the contract's API changes — same types, same namespace, same package id, and the
  version line continues forward.

- **For consumers of this repository, nothing changes.** `Flux.Abstractions` still arrives
  transitively through `FluxIndex.Core`. Only its origin and version line moved.

---

## [0.23.0] - 2026-07-30

The keyword leg is now persistent on **PostgreSQL** as well, and the BM25 implementation is shared by
every SQL backend so ranking cannot drift between them.

### Added — persistent keyword index on PostgreSQL

`AddPostgreSQLStorage()` registers `PostgresKeywordSearchService`, which keeps the BM25 inverted index
in the same database as the vectors. `KeywordSearchAsync`/`HybridSearchAsync` therefore keep working
after a restart and across processes, instead of degrading to vector-only (the degradation 0.21.5
started warning about). Schema provisioning follows `VectorStore.EnableAutoMigration`, like the vector
store's.

**One reindex is needed** to populate the keyword index for documents indexed earlier.

Verified against a live PostgreSQL (Testcontainers): **35 integration tests pass**, covering the
restart roundtrip, ranking, deletion propagation, re-indexing without document-frequency drift,
document-scoped search, Korean whole-token matching, a 6,000-term batch through the array predicate,
and provisioning into a database that already holds other applications' tables. Six of them index one
corpus into **both** SQL backends and compare the results — order, scores, matched terms, term
frequencies and document lengths — so "ranking does not depend on the store" is asserted by execution
rather than inferred from the shared code. A nightly `Integration Tests (PostgreSQL)` workflow keeps
them running.

### Changed — one shared BM25 implementation behind the SQL backends

Scoring, tokenization, index maintenance and the index schema now live in
`FluxIndex.Core.Application.Services.KeywordSearch.RelationalKeywordSearchService`; each storage
package supplies only its SQL dialect (DDL, upsert syntax, id-list predicate). Consequences:

- Keyword scores are comparable between SQLite and PostgreSQL, and a fix in the shared code applies to
  both. A second hand-written copy of BM25 was the alternative, and it would have drifted silently.
- `SQLiteKeywordSearchService` is unchanged in behavior and public shape; its SQLite schema DDL is
  byte-identical, so existing databases are untouched.
- Removed `FluxIndex.Storage.SQLite.KeywordSearch.BM25TermEntity` / `BM25PostingEntity` /
  `BM25StatisticsEntity` — unused EF entity types with no `DbContext` mapping (the service uses raw
  SQL). Breaking only for code that referenced the types themselves.

### Fixed — `KeywordSearchOptions.DocumentIdFilter` was ignored

The option existed on the contract but no implementation read it: a caller scoping a keyword search to
one document received global results. It is now applied to the postings themselves, not to the result
set — filtering after the top-N cut would have returned nothing when the scoped document's matches sat
below the global top N.

### Fixed — keyword `document_frequency` update could exceed SQLite's statement limit

Recomputing document frequency inlined every affected term id into one statement. A batch touching
enough distinct terms would exceed `SQLITE_MAX_SQL_LENGTH`; the ids are now sent in bounded batches.
PostgreSQL passes them as one array parameter instead, so its statement size is independent of the
batch.

### Fixed — null parameters were sent without a type

A null bound as a bare `DBNull` carries no type information and fails on providers that require one.
The only nullable column in the keyword schema is text (`bm25_chunks.metadata`), which is the common
case — most chunks have no metadata.

### Fixed — intermittent failures in the SQLite concurrency tests

Two concurrency tests resolved one `IVectorStore` and used it from several tasks at once. The store is
`DbContext`-backed and EF Core forbids that; the corrupted connection state surfaced later as a
`NullReferenceException` while the service provider was disposed, failing roughly half of full-solution
runs. Each worker now resolves from its own scope. Test-only change — no product behavior is affected.
Fixture teardown also no longer calls the process-global `SqliteConnection.ClearAllPools()`.

---

## [0.22.0] - 2026-07-30

The hybrid keyword leg is now populated by indexing and persisted alongside the vectors — on the
**SQLite** path. See "Not covered yet" below for PostgreSQL and Qdrant.

### Fixed — indexing never populated the keyword index

`Indexer` wrote to the vector store and nothing else. No indexing API touched the keyword (sparse)
index, so the hybrid keyword leg only ever held what the running process happened to search: empty
after a restart, and empty in any process that did not itself index. Hybrid search returned results
and looked fine while ranking by vector similarity alone (0.21.5 added the warning that made this
visible). Every mutation path now keeps the keyword index in step — `IndexDocumentAsync`,
`AddChunksAsync`, `UpdateDocumentAsync`, `ReindexDocumentAsync`, `DeleteByDocumentIdAsync`,
`DeleteChunkAsync`.

`IndexerOptions.IndexKeyword = false` stops the indexer *adding* to the keyword index. It is **not** a
compatibility switch: with nothing in the index, keyword search returns no results and hybrid search
ranks by vector similarity alone — whereas before 0.22.0 the keyword leg scanned chunk content and did
return something. Turn it off only if you do not use keyword or hybrid search. Deletions are still
propagated while a keyword index exists, so the option cannot leave postings for deleted documents
behind.

**Consumers may need one reindex** to build the keyword index for documents indexed before 0.22.0.

### Fixed — common terms were silently dropped from keyword results

BM25 used the unsmoothed Robertson IDF `log((N-df+0.5)/(df+0.5))`, which is **negative** once a term
appears in more than half the documents. Combined with the default `MinScore` of 0, every such result
was discarded: the more common a term was in the corpus, the more certainly the keyword leg
contributed nothing. Now uses the smoothed form Lucene uses, `log(1 + (N-df+0.5)/(df+0.5))`, which is
always positive. Keyword **recall improves** (results that were being thrown away now appear) and
scores change. The default fusion method (`RelativeScoreFusion`) min-max normalises each leg before
applying the weights, and RRF is rank-based, so `VectorWeight`/`SparseWeight` keep their meaning on
those paths; the raw-score methods (`Product`, `Maximum`, `HarmonicMean`) do see an absolute-scale
shift in the sparse leg.

### Fixed — the SQLite keyword search service could not run on its own

`SQLiteKeywordSearchService` read chunk content back from the vector store's private `vectors` table
instead of storing it, which made it unusable without a co-located SQLite vector store and, worse,
made `DeleteByDocumentIdAsync` a silent no-op whenever the vector rows had already been dropped —
the natural order when deleting a document, leaving keyword postings that still matched. It now owns
its payload (`bm25_chunks`). Re-indexing a chunk also replaces its postings instead of layering new
ones on top, so document frequency can no longer drift. Batch indexing commits once instead of once
per chunk.

### Fixed — SQLite schema provisioning on the builder path used `EnsureCreated()`

Same defect class as 0.21.1 (PostgreSQL), still present for the SQLite vector store: the initializer
`AddSQLiteStorage()` registers called `EnsureCreated()`, which skips schema creation entirely if the
database holds any relation. Pointing FluxIndex at a database that already has your own tables meant
`Build()` succeeded and the first write failed with "no such table: vectors". Now provisions per
owned table, like every other component since 0.21.3.

### Changed — one keyword backend, two entry points *(breaking)*

- `ISparseRetriever` **removed**. `IKeywordSearchService` is the single keyword contract; it is a
  superset (it also has the index-management and delete operations). `BM25SparseRetriever` still
  implements it, and its previously-explicit `SearchAsync`/`GetStatisticsAsync` are now public.
- `IHybridSearchService` implementations take `IKeywordSearchService` instead of `ISparseRetriever`.
  This is what lets a persistent backend serve the sparse leg at all.
- `IDocumentRepository.SearchByKeywordAsync` **removed**. `Retriever.KeywordSearchAsync` (unchanged
  as a public method) now reads the keyword index instead of scanning each document's chunks for a
  substring, so its results are **BM25-ranked** rather than substring-matched.
- `QdrantHybridSearchService` takes `IKeywordSearchService` instead of the concrete
  `BM25SparseRetriever`, so a registered persistent backend reaches that path too.
- `Indexer` and `Retriever` take an optional trailing `IKeywordSearchService`. Builder users are
  unaffected; callers constructing them by hand are not broken (the parameter is optional).

### Added

- `IndexerOptions.IndexKeyword` (default `true`) and `FluxIndexContextBuilder.WithIndexerOptions(...)`
  — the builder previously had no way to configure the indexer at all, which would have left the new
  option unreachable.
- `SQLiteKeywordSearchService` is registered by `AddSQLiteStorage()` on the same database as the
  vector store, and its schema is provisioned during `Build()` like every other component.
- `SQLiteKeywordSearchService.EnsureSchemaAsync()`.

### Fixed — the in-memory keyword index was registered per scope

The default in-memory BM25 index was registered `Scoped`, so each scope got its own empty index —
harmless while nothing wrote to it, a silent "no results" now that indexing does. Registered
`Singleton`, and with `TryAdd` so a storage package's persistent backend wins (storage registrations
run before the SDK's defaults, so a plain `Add` would have discarded them).

### Not covered yet

- **PostgreSQL and Qdrant have no persistent keyword backend.** On those paths the keyword leg is now
  correctly *populated by indexing* and benefits from the IDF fix, but it still lives in process
  memory and is empty after a restart. The PostgreSQL backend is the next piece of this work.
- CJK tokenisation is unchanged: the tokenizer splits on `\W+` and Hangul is `\w`, so a Hangul run is
  never split — `착수계` does not match `착수계약서`. Whole-token queries work and are covered by tests.

---

## [0.21.5] - 2026-07-28

### Fixed — `Retriever.SearchAsync` discarded the caller's hybrid weights

`SearchOptions` has a `HybridSearchOptions` subclass carrying `VectorWeight` / `KeywordWeight` /
`RerankingStrategy`, and `FluxIndexContext.HybridSearchV2Async` honoured them — but
`Retriever.SearchAsync` built its Core options inline with hardcoded `0.7` / `0.3`, so passing
`HybridSearchOptions` to the main search entry point changed nothing. Both paths now map through one
place (`HybridSearchOptionsMapper`); plain `SearchOptions` still gets 0.7/0.3, so default behaviour
is unchanged.

### Added — hybrid search warns when it degrades to vector-only

The keyword leg is process-local and no indexing API populates it, so after a restart — or in any
process that did not itself index — hybrid search silently ranks by vector similarity alone. That
limitation was documented in 0.19.0 but invisible at runtime: results came back and looked fine.
Both hybrid paths now emit one warning when the keyword/sparse leg contributes nothing while the
vector leg matched, naming the reason.

This is diagnostics only. Making the keyword leg survive a restart is the 0.22.0 work
(the indexing API will populate the sparse index and persistent backends land with it).

---

## [0.21.4] - 2026-07-28

### Fixed — the PostgreSQL quantized vector store provisioned no schema at all

`AddPostgreSQLQuantizedVectorStore(...)` registered the DbContext and the store but no provisioning
whatsoever — no initializer, no migration — so `vectors` and `quantized_vectors` were never created
and the first write failed even against an empty database. It now provisions through the same shared
routine as the other components, exposed both as an `IStorageInitializer` and as a hosted service.
The store is reachable only by direct registration, never from the SDK builder, which is why nothing
had surfaced it.

### Fixed — remaining `EnsureCreated` provisioning on shared databases

The PostgreSQL entity graph (`EnsureEntityGraphSchemaAsync`) and the SQLite vector, quantized and
main migration paths still created their schema with `EnsureCreated`, which does nothing once the
database holds any table — including tables another FluxIndex component put there. They now
provision per owned table like everything else.

### Fixed — schema provisioning inside an open transaction

The provisioner's existence probe issued a raw ADO command without enlisting the ambient EF
transaction, so provisioning from a context with a transaction in flight failed with *"Execute
requires the command to have a transaction object"*. Caught by the SQLite native-extension
concurrency test. The probe now enlists `CurrentTransaction` when one is open.

**Known remaining gap.** `SQLiteVecDbContext` keeps its own bespoke initialization (vec0 virtual
tables plus a fingerprint-based re-init added in 0.20.2) and is deliberately left alone — its schema
is not fully EF-modelled, so the shared provisioner does not apply.

**Upgrade note — when the partial-schema guard can fire.** The components swept in 0.21.2–0.21.4 own
two or more tables each, so the "partially present" error is now reachable where it was not in
0.21.1. Upgrading alone cannot trigger it: no release has ever shipped one of these components with
fewer tables than it has today, so an older database is either complete or empty for a given
component. The realistic trigger is a **name collision** in a database shared with your own schema —
an existing `cache_stats`, `chunk_relationships` or similarly named table makes that component see a
partial schema and refuse to start. The message names the tables it found and the ones it wants; the
remedies are to give the index its own database or schema, rename the colliding table, or turn that
component's auto-migration off (`EnableAutoMigration` for the vector store, `AutoMigrate` for graph
and cache) and manage its schema yourself.

---

## [0.21.3] - 2026-07-28

### Fixed — the SQLite graph store, entity graph and semantic cache were never provisioned by the SDK builder

The SQLite side had the same defect 0.21.2 fixed for PostgreSQL, and it reaches further because SQLite
is the default local stack: `UseSQLite(path)` / `UseLocalStorage(path)` enable the vector store, the
graph store, the entity graph and the semantic cache, but only the vector store was provisioned by
`Build()`. The other three migrated from `IHostedService` implementations, which the builder never
starts. A freshly built database contained exactly one table — `vectors` — and the first graph,
GraphRAG or semantic-cache operation failed on a missing table.

Each component's migration now lives in one routine shared by both paths: an `IStorageInitializer`
the builder runs at `Build()`, wrapped by the existing hosted service for consumers registering the
stores directly. Provisioning creates only the tables each component owns (`SQLiteSchemaProvisioner`),
so components sharing one database file no longer suppress each other — `EnsureCreated` skipped
schema creation as soon as whichever component ran first had created anything, which is also why a
database shared with the consumer's own tables got nothing.

Regression coverage runs a real `UseSQLite(...).AddSQLiteStorage().Build()` and asserts each enabled
component's tables exist, including the derived entity-graph database file. It needs no container, so
unlike the PostgreSQL equivalent it runs in CI.

**Known remaining gap.** `AddSQLiteVecVectorStore`, `AddSQLiteQuantizedVectorStore`,
`AddPostgreSQLEntityGraph` and `AddPostgreSQLQuantizedVectorStore` are reachable only by direct
registration, not from the builder, and are still hosted-service-only (the PostgreSQL quantized store
has no provisioning at all). Tracked separately.

---

## [0.21.2] - 2026-07-28

### Fixed — the PostgreSQL graph store and semantic cache were never provisioned by the SDK builder

`UsePostgreSQL(conn)` enables the vector store, the graph store **and** the semantic cache on one
connection. The graph and cache schemas, however, were created by `IHostedService` migrations, and
`FluxIndexContextBuilder.Build()` never starts a host — it builds its own service provider and runs
the registered `IStorageInitializer` instances. So on the builder path those two components were
never provisioned on **any** database, fresh or shared, and the first graph or cache write failed
with `42P01`. Only consumers who registered the stores directly into an application's service
collection (where the host runs the migration at start-up) were unaffected.

Each component's migration now lives in one routine that both paths share: an `IStorageInitializer`
the builder runs at `Build()`, wrapped by the existing hosted service for the direct-registration
path. Schema creation goes through the same owned-relation provisioner introduced in 0.21.1, so the
components no longer skip each other's tables when they share a database — which `EnsureCreated`
did as soon as any one of them had been provisioned first.

Also fixed: the vector store's provisioning is now reused rather than duplicated
(`RelationalSchemaProvisioner`).

**Known remaining gap.** The SQLite graph store, entity graph and semantic cache have the same
shape and are still hosted-service-only; `UseSQLite(path)` enables graph and cache the same way.
Tracked separately. PostgreSQL entity graph (`AddPostgreSQLEntityGraph`, not reachable from the
builder) and `AddPostgreSQLQuantizedVectorStore` (no provisioning at all) are also still open.

---

## [0.21.1] - 2026-07-28

### Fixed — PostgreSQL auto-init no longer skips schema creation on a non-empty database

`AddPostgreSQLStorage()` provisioned the vector schema through EF's `EnsureCreated()`, which skips
schema creation entirely once the database contains **any** relation. Pointing FluxIndex at a
database that already held the consumer's application tables therefore created nothing: `Build()`
reported success and the first index write failed with `42P01: relation "vectors" does not exist`.
Fresh databases were unaffected, so the failure appeared only in production.

The initializer now enumerates the relations its EF model owns, probes each with `to_regclass`, and
provisions through `IRelationalDatabaseCreator` when none are present — leaving unrelated relations
in the database untouched. The database itself is created when absent. A partial schema (some owned
relations present, some missing) is refused with an actionable exception instead of being silently
half-repaired; with the current single-relation model this guard cannot yet trigger, and it becomes
live as soon as the context owns more than one relation.

Reported by All.Manual. No API change — upgrading is enough.

**Known adjacent gap (not fixed here).** PostgreSQL graph, entity-graph, and semantic-cache still
initialize with `EnsureCreatedAsync` and, by default, on the vector store's connection. Tracked
separately; use `AutoMigrate`-off plus an externally managed schema until it lands.

> Correction (0.21.2): that gap was worse than described here. Those components migrate from hosted
> services, and the SDK builder never starts a host — so on the builder path they were not merely
> skipped after `vectors` existed, they never ran at all. Fixed in 0.21.2.

---

## [0.21.0] - 2026-07-28

### Changed — BREAKING: pipeline integrations split out of `FluxIndex.SDK`

`FluxIndex.SDK` no longer depends on FileFlux, WebFlux, FluxCurator, or FluxImprover.
Each integration now ships as its own opt-in package:

| New package | Contains |
|---|---|
| `FluxIndex.Integrations.FileFlux` | FileFlux DI wiring + `DocumentProcessingPipeline` |
| `FluxIndex.Integrations.WebFlux` | WebFlux DI wiring + context builder extensions |
| `FluxIndex.Integrations.FluxCurator` | FluxCurator DI wiring + embedding adapters |
| `FluxIndex.Integrations.FluxImprover` | FluxImprover DI wiring + enrichment pipeline |

**Why.** The bundled graph made unrelated transitive vulnerabilities block FluxIndex CI:
0.19.0 failed `restore` on `NU1902` (AngleSharp mXSS) reached through `FluxIndex.SDK` → WebFlux →
AngleSharp, in a library that does not use AngleSharp at all. Fixing WebFlux removed that symptom
but not the shape, so the next transitive advisory would have repeated it. Consumers now pay only
for the pipelines they use.

**Migration.** Add the packages you actually use and update namespaces:

```diff
  <PackageReference Include="FluxIndex.SDK" Version="0.21.0" />
+ <PackageReference Include="FluxIndex.Integrations.FileFlux" Version="0.21.0" />
```

```diff
- using FluxIndex.SDK.Extensions.FileFlux;
- using FluxIndex.SDK.Processing;
+ using FluxIndex.Integrations.FileFlux;
+ using FluxIndex.Integrations.FileFlux.Processing;
```

Namespace mapping is mechanical — `FluxIndex.SDK.Extensions.<X>` → `FluxIndex.Integrations.<X>`
(same for `.Adapters` / `.Services` sub-namespaces), and `FluxIndex.SDK.Processing` →
`FluxIndex.Integrations.FileFlux.Processing`. No type names, signatures, or behavior changed.
Extension methods (`AddFileFluxIntegration`, `AddDocumentProcessingPipeline*`, `AddWebFlux*`, …)
keep their names.

### Removed

- `samples/ChunkingQualityTest` and `samples/FileFluxIndexSample` — both referenced projects and
  packages that no longer exist (`src/FluxIndex.Extensions.FileFlux`, a bare `FluxIndex` package),
  were outside the solution so nothing built them, and had been untouched since 2025-11-29.
  README linked to both. Available in git history.

---

## [0.20.2] - 2026-07-24

### Fixed
- **SQLite-vec: writes silently broke after the effective embedding fingerprint drifted on a
  latched store instance** — once `SQLiteVecVectorStore.EnsureInitializedAsync` succeeded it
  short-circuited on `_initialized` alone, so a later fingerprint change (e.g. a `BindIdentity`
  in another scope mutating the shared `SQLiteVecOptions`) left subsequent writes targeting a
  `chunk_embeddings_{fingerprint}` table that was never created (`no such table`). The store now
  tracks the table name captured at init and re-initializes when the current effective name
  diverges, creating the new vec0 table (`CREATE VIRTUAL TABLE IF NOT EXISTS`) before writing.
  Regression guard: `SQLiteVecBindIdentityDriftTests`.

### Docs
- `SQLiteVecOptions.EmbeddingFingerprint` doc corrected: a null fingerprint throws
  `InvalidOperationException` from `GetVecTableName()` — there is no automatic
  `chunk_embeddings_{dimension}` fallback (the comment contradicted the throw contract).

---

## [0.20.1] - 2026-07-22

### Fixed
- **EntityGraph (PostgreSQL): `EnsureCreated` failed whenever `EmbeddingDimension > 0`** —
  the entity/community `Embedding` columns were mapped as dimensionless `vector`, which pgvector
  rejects for any vector index ("column does not have dimensions"). Columns now declare
  `vector(EmbeddingDimension)`. Latent since the ivfflat era; exposed by the new schema
  integration tests.
- EntityGraph vector indexes converted **ivfflat → HNSW** (entity + community), matching the main
  vector store: ivfflat trains centroids at CREATE INDEX time, so an index created on an empty
  table silently loses recall for data inserted afterwards. `EntityGraphOptions.IvfflatLists` is
  now `[Obsolete]` and has no effect (removal in a future minor).

### Removed
- Expired `NU1903` (CVE-2025-6965) build suppression — SQLitePCLRaw 2.1.12 has shipped and src
  projects pin it directly; restore is warning-clean without it.

---

## [0.20.0] - 2026-07-21

### Added
- **Multi-value (MatchAny) metadata filters** across every `IVectorStore` implementation: a
  collection-valued filter entry (`List<string>`, arrays, JSON arrays) now matches when the chunk's
  metadata value equals ANY element — Qdrant `Match.Keywords`, PostgreSQL per-element jsonb `@>`
  OR-combined (each branch GIN-indexable), in-memory stores via the shared backstop. One query
  replaces the N-way per-value fan-out consumers previously had to run
  (`filters: new() { ["document_id"] = fileHashes }`).
- `VectorStoreBase.ExpandFilterValue` / `VectorStoreBase.ValidateFilters` — public helpers that
  define and enforce the filter-value contract for store implementations.
- Shared filter-contract regression suite (`VectorStoreFilterContractSuite`) run against InMemory,
  SQLite, and SQLite-quantized stores; PostgreSQL/Qdrant cover the same cases in their own suites.

### Changed (behavioral)
- **Unsupported filter values now throw `ArgumentException`** at call time instead of silently
  matching nothing. Previously e.g. a `List<string>` filter value degraded to its `ToString()`
  type name and returned zero results with no signal; empty collections, nested collections, and
  arbitrary objects are rejected loudly. Validation is eager (at `SearchAsync` call), not deferred
  to result enumeration.

### Fixed
- `PostgreSQLQuantizedVectorStore.SearchAsync` and `SQLiteQuantizedVectorStore.SearchAsync`
  silently **ignored the `filters` parameter entirely**, leaking chunks across filter scope
  (e.g. other tenants). Both now apply the shared match semantics before the topK trim.

---

## [0.16.0] - 2026-07-02

### Changed (BREAKING)

- **`FluxIndex.Extensions.FileVault` extracted to the [FluxFeed](https://github.com/iyulab/FluxFeed) repository.** File-to-vector synchronization (git-like file tracking, folder monitoring, background ingestion) is now the FluxFeed document-pipeline surface (④b), which feeds into FluxIndex (④a). The `FluxIndex.Extensions.FileVault` package is no longer published from the FluxIndex family (family: 12 → 11 packages). The public API surface (`IVault`, `AddFileVaultWithFluxIndex`, etc.) is preserved, so consumer migration is a package-id + namespace swap (`FluxIndex.Extensions.FileVault` → `FluxFeed`), not an API rewrite. See [docs/FILEVAULT_GUIDE.md](./docs/FILEVAULT_GUIDE.md) for the migration note.

---

## [0.15.0] - 2026-06-29

### Added
- `FluxIndex.Extensions.FileVault` (MU-2): **terminal-await for background memorize**. The facade previously
  discarded the queued job id and returned an early-stage `VaultEntry`, so consumers in background mode polled
  entry stage / queue status to know when memorize actually finished ("success lie"). Two additive members:
  - `IVaultQueueService.WaitForJobAsync(jobId, ct)` — signal-driven (no polling) wait that resolves on the
    Completed/Failed/Cancelled transition and immediately for an already-terminal job (race-free).
  - `IVault.MemorizeAsync(filePath, bool waitForCompletion, ct)` — when `true`, awaits terminal completion and
    returns the entry at its Memorized stage; a failed/cancelled job surfaces as an exception rather than a
    silently-incomplete entry. `false` is identical to the existing single-arg overload (zero regression).
  Reported via umbrella MU-2 (rule-of-three: AIMS, Filer, textree all hand-rolled completion polling).

## [0.13.19] - 2026-06-10

### Fixed
- `FluxIndex.Extensions.FileVault`: a removed entry could persist in `ListAsync(null)` indefinitely after a preceding hybrid `SearchAsync`. Root cause: `VaultEntry.Load`/`SaveMetadata` opened `meta.json` without `FileShare.Delete`, so a concurrent `ListAsync` enumeration read blocked the background remove job's `Directory.Delete` (Windows `ERROR_SHARING_VIOLATION`), leaving the entry directory on disk (and growing it unboundedly). Now opened with `FileShare.ReadWrite | FileShare.Delete`, and `VaultStorageService.DeleteEntryStorageAsync` retries the directory delete (5×, 100 ms backoff) to absorb the residual `RemoveDirectory` race and transient foreign locks. Entries stuck in `RemovalPartial` from before the fix self-heal via `RecoverPartialRemovalsAsync` on next host start. Reported by Filer (golden gate `SC-RAG-1`).

---

## [0.13.15] - 2026-05-28

### Fixed
- `FluxIndex.Extensions.FileVault`: `VaultBackgroundService` — replaced polling `Task.Delay` loop with event-driven wake signal via `IVaultQueueService.JobEnqueued`. Job scheduling latency drops from 5–10s (idle poll interval) to < 1ms after enqueue.

### Changed
- `FluxIndex.Extensions.FileVault.Tests`: Added `[Trait("Category", "Integration")]` to `FileVaultPipelineSimulationTests` and `VaultSubfolderScenariosTests` — these were missing the trait despite living in the `Integration/` folder, causing them to run with unit tests.

---

## [0.13.14] - 2026-05-22

### Added
- `FluxIndex.Providers.OpenAI`: `WellKnownOpenAIModels` — static lookup for 30+ model embedding dimensions
- `FluxIndex.Providers.OpenAI`: `OpenAICompatibleEmbeddingService(endpoint, apiKey, model, logger)` constructor — auto-resolves dimension for well-known models
- `FluxIndex.Providers.OpenAI`: `AddOpenAICompatibleEmbedding(endpoint, apiKey, model)` DI overload — no dimension required for well-known models

### Changed
- `build-and-release.yml`: Pack step now uses `dotnet pack FluxIndex.slnx` (solution-wide) instead of per-project loop — ensures all family packages are published together at the same version
- `build-and-release.yml`: `version_check` step now uses correct `fluxindex.sdk` package ID (was `fluxindex` which doesn't exist on NuGet)

### Docs
- Added `CHANGELOG.md` (historical breaking changes from 0.2.x → 0.13.x)
- Added `docs/MIGRATION.md` (step-by-step upgrade guide for 0.2.x → 0.13.x consumers)
- Updated `docs/AI_PROVIDER_INTEGRATION.md` with `FluxIndex.Providers.OpenAI` official package usage
- Updated `docs/README.md` with MIGRATION.md quick link

---

## [0.13.12] - 2026-05-22

### Changed
- `FluxIndex.Core`: Remove `TokenMeter.Abstractions` dependency — `ITokenCounter` is now defined locally

---

## [0.13.10] - 2026-04-xx

### Fixed
- `FluxIndex.Core`: Remove unnecessary `FileFlux` dependency

### Added
- `FluxIndex.Extensions.FileVault`: `IVault.RemoveAsync(IEnumerable<string>)` batch overload

---

## [0.13.7] - 2026-03-xx

### Fixed
- `FluxIndex.Storage.SQLite`: Clean legacy-fingerprint vec0 orphans on delete and startup sweep
- `FluxIndex.Storage.SQLite`: Pass `CancellationToken` correctly to `ExecuteSqlRawAsync`

---

## [0.13.3] - 2026-03-xx

### Changed
- `FluxIndex.Core`: All `[LoggerMessage]` strings translated from Korean to English (ASCII-only)
- Added `LogLanguageConventionTests` regression test to prevent Korean log string regressions

---

## [0.13.0] - 2026-02-xx

### Added
- `ProcessingStage.Error` — new terminal error stage for vault pipeline
- `VaultStatus`: `RefinedCount`, `StaleCount`, `ErrorStageCount` counters
- `IVault.GetErrorEntriesAsync` — query entries in Error stage

---

## [0.12.0] - 2026-02-xx

### Added
- `EmbeddingIdentity` / `ModelFingerprint` for model-aware vector collection naming
- `IVectorStoreManager` interface with collection listing support
- **Require `EmbeddingFingerprint` for vec table naming** (breaking for custom `IEmbeddingService` implementations that do not return a stable model name)

---

## [0.11.0] — 2026-03-20

### Removed (BREAKING)
- `FluxIndex.SDK`: `AddOpenAIEmbedding()`, `AddAzureOpenAIEmbedding()` extension methods removed.
  These were no-ops in prior versions. Use `FluxIndex.Providers.OpenAI` package instead:

  ```csharp
  // Before (0.10.x, was already a no-op)
  services.AddOpenAIEmbedding(apiKey);

  // After (0.11.0+)
  // Install: dotnet add package FluxIndex.Providers.OpenAI
  services.AddOpenAICompatibleEmbedding(
      "https://api.openai.com/v1", apiKey, "text-embedding-3-small", dimension: 1536);
  ```

### Changed
- `FluxIndex.SDK`: Storage provider registration decoupled from SDK (was already separate but stubs removed)

---

## [0.10.1] - 2026-02-xx

### Added
- Dimension-aware vault + SQLite-vec table naming (breaking if using raw `IVectorStore` without dimension)

---

## [0.9.0] - 2026-01-xx

### Added
- `FluxIndex.Providers.OpenAI` — new package for OpenAI-compatible embedding and reranking
  - `OpenAICompatibleEmbeddingService(endpoint, apiKey, model, dimension, logger)`
  - `AddOpenAICompatibleEmbedding(endpoint, apiKey, model, dimension)` DI extension
  - `OpenAICompatibleRerankerService` + `AddOpenAICompatibleReranker(endpoint, apiKey, model)` DI extension
- `FluxIndex.Providers.LMSupply` — new package for LMSupply local embedding and reranking

---

## [0.6.0] - 2025-xx-xx

### Added
- SQLite Entity Graph Store for local GraphRAG
- Unified storage provider architecture (auto-maximize)

---

## [0.4.0] — 2025-12-15

### Removed (BREAKING)
**Package consolidation — several packages were merged or renamed.**

#### Removed packages
| Old package | Replacement |
|-------------|-------------|
| `FluxIndex.AI.OpenAI` | `FluxIndex.Providers.OpenAI` (added in 0.9.0) |
| `FluxIndex.AI.Anthropic` | Implement `IEmbeddingService` directly in your app |
| `FluxIndex.AI.Google` | Implement `IEmbeddingService` directly in your app |
| `FluxIndex.AI.Local` | Merged into `FluxIndex.SDK` |
| `FluxIndex.Extensions.FileFlux` | Merged into `FluxIndex.SDK` |
| `FluxIndex.Extensions.FluxCurator` | Merged into `FluxIndex.SDK` |
| `FluxIndex.Extensions.FluxImprover` | Merged into `FluxIndex.SDK` |
| `FluxIndex.Extensions.WebFlux` | Merged into `FluxIndex.SDK` |

#### Namespace changes (SDK extensions)
| Old namespace | New namespace |
|---------------|---------------|
| `FluxIndex.Extensions.WebFlux` | `FluxIndex.SDK.Extensions.WebFlux` |
| `FluxIndex.Extensions.FileFlux` | `FluxIndex.SDK.Extensions.FileFlux` |

#### Builder API changes
- `FluxIndexContextBuilder.UseOpenAI(apiKey, model)` — removed
- `FluxIndexContextBuilder.UseAzureOpenAI(endpoint, apiKey, model)` — removed
- `FluxIndexContextBuilder.UseLocalAI()` — still available (ONNX local model)

  Migration:
  ```csharp
  // Before (0.2.x)
  var ctx = FluxIndexContext.CreateBuilder()
      .UseLocalStorage("index.db")
      .UseOpenAI(apiKey, "text-embedding-3-small")
      .Build();

  // After (0.9.0+, using FluxIndex.Providers.OpenAI)
  var ctx = FluxIndexContext.CreateBuilder()
      .UseLocalStorage("index.db")
      .UseEmbeddingService(new OpenAICompatibleEmbeddingService(
          "https://api.openai.com/v1", apiKey, "text-embedding-3-small", 1536, logger))
      .Build();

  // Or with DI
  services.AddOpenAICompatibleEmbedding(
      "https://api.openai.com/v1", apiKey, "text-embedding-3-small", dimension: 1536);
  ```

---

## [0.3.1] - 2025-xx-xx

### Changed
- Namespace reorganization: `FluxIndex.Domain.Entities` → `FluxIndex.Core.Domain.Entities`

  **Migration:**
  ```csharp
  // Before (0.2.x)
  using FluxIndex.Domain.Entities;

  // After (0.3.x+)
  using FluxIndex.Core.Domain.Entities;
  ```

  Affected types: `DocumentChunk`, `Document`, `SearchResult` and all other domain entities.

---

## [0.2.16] - 2025-xx-xx

Last version with:
- `FluxIndex.Domain.Entities` namespace (use `FluxIndex.Core.Domain.Entities` in 0.3.x+)
- `FluxIndex.AI.OpenAI` package (use `FluxIndex.Providers.OpenAI` in 0.9.x+)
- `FluxIndex.Extensions.WebFlux` separate package (merged into `FluxIndex.SDK` in 0.4.0)
- `UseOpenAI()` / `UseAzureOpenAI()` builder methods (removed in 0.4.0)

---

## [0.2.x] - 2025

Initial public versions. Feature development.
