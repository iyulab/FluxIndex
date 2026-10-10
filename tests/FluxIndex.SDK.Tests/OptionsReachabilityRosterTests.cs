using System.Reflection;
using System.Runtime.Loader;
using Iyu.Conventions.Testing;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Every public option a caller can set must be read by the library. An option nothing reads is a
/// promise the library does not keep: setting it changes nothing and says nothing. In 0.37.1
/// <c>GraphRAGBuildOptions.EntityOptions</c> had no reader at all — a consumer's <c>Language</c> or
/// <c>UseLlm</c> never reached the extractor, and no build, test or review noticed for two minor versions.
/// </summary>
/// <remarks>
/// <para>
/// This scans the IL of every FluxIndex library assembly for a call to each option property's getter,
/// outside the options type itself, and pins the properties that have none. Wiring one up, or adding a
/// new option that nothing reads, then shows up here as a deliberate change to the roster.
/// </para>
/// <para>
/// It lives in this project because it is the one that references the most library assemblies. It
/// carries no category trait, so it runs in CI.
/// </para>
/// <para>
/// Two limits, both deliberate. Reading is necessary, not sufficient — an option can be read and still
/// have no effect; that has no static signal. And a property is attributed to the type that declares it:
/// a base-class option honoured through a helper the derived type hands itself to is counted on the base.
/// </para>
/// </remarks>
public class OptionsReachabilityRosterTests
{
    // Each entry has an open issue draft: wire the option, or remove it as a deliberate decision.
    // Filled by this roster's first run; see the issue draft named in the cycle log that introduced it.
    private static readonly Dictionary<string, string[]> KnownUnread = new(StringComparer.Ordinal)
    {
        // First run, 2026-09-13 (FluxIndex 0.37.2 tree): 83 types; 0.37.3 wired EntityExtractionOptions.Language/
        // CustomPatterns and GraphRAGQueryOptions.Include* (81 types remain). Each line is a set of promises the
        // library does not keep today; the issue draft that introduced this roster lists them by type
        // with a wire-or-remove call for each. Shrink this list, never grow it silently.
        ["FluxIndex.Core.Application.Interfaces.AdaptiveSearchOptions"] = ["EnableDetailedLogging", "UserContext"],
        ["FluxIndex.Core.Application.Interfaces.AgenticRetrievalOptions"] = ["EnableAdaptivePlanning"],
        ["FluxIndex.Core.Application.Interfaces.AnswerSynthesisOptions"] = ["StructuredAnswer"],
        ["FluxIndex.Core.Application.Interfaces.CacheMaintenanceOptions"] = ["CompactStorage", "TargetMemoryUsagePercent", "UpdateStatistics"],
        ["FluxIndex.Core.Application.Interfaces.CacheWarmupOptions"] = ["MaxDuration", "TopHotChunksCount", "WarmupEmbeddings", "WarmupEntities"],
        ["FluxIndex.Core.Application.Interfaces.ContextExpansionOptions"] = ["MaxExpansionDistance"],
        ["FluxIndex.Core.Application.Interfaces.ContextualHeaderOptions"] = ["UsePromptCaching"],
        ["FluxIndex.Core.Application.Interfaces.DynamicFusionConfiguration"] = ["Complexity", "Reasoning", "TechnicalDomains"],
        ["FluxIndex.Core.Application.Interfaces.GlobalSearchOptions"] = ["ScoreConfidence"],
        ["FluxIndex.Core.Application.Interfaces.IterativeRetrievalOptions"] = ["IncludeReasoningTrace"],
        ["FluxIndex.Core.Application.Interfaces.ListwiseRerankOptions"] = ["IncludeExplanation", "UseAttentionScoring", "UseLlm"],
        ["FluxIndex.Core.Application.Interfaces.PathFindingOptions"] = ["TimeoutMs", "UseRelationshipStrength", "WeightType"],
        ["FluxIndex.Core.Application.Interfaces.QuantizationOptions"] = ["NormalizeVectors", "TrainingSamples"],
        ["FluxIndex.Core.Application.Interfaces.QueryDecompositionOptions"] = ["MaxDecompositionDepth"],
        ["FluxIndex.Core.Application.Interfaces.RerankOptions"] = ["Model", "ModelParameters"],
        // MinResults · EnableDetailedLogging · UserContext were read only by a second, never-registered
        // SelfRAGService removed in 0.38.0 — the registered service never honoured them (roster blind spot ④).
        ["FluxIndex.Core.Application.Interfaces.SelfRAGOptions"] = ["EnableContextExpansion", "EnableMultiPerspectiveSearch", "MinResults"],
        ["FluxIndex.Core.Application.Interfaces.VerificationOptions"] = ["CustomCriteria", "IncludeDetailedReasoning", "MaxHallucinationRisk"],
        ["FluxIndex.Core.Application.Models.ClassificationOptions"] = ["CacheExpirationHours", "Enabled"],
        ["FluxIndex.Core.Application.Models.ClassificationValidationOptions"] = ["DuplicateThreshold"],
        ["FluxIndex.Core.Application.Services.ContextualEmbeddingOptions"] = ["GenerateDualEmbeddings", "MaxCombinedLength"],
        ["FluxIndex.Core.Application.Services.QuantizedVectorStoreOptions"] = ["DefaultCandidateMultiplier", "StoreOriginalEmbeddings"],
        ["FluxIndex.Core.Application.Services.SelfRAGServiceOptions"] = ["DefaultMaxIterations", "DefaultQualityThreshold"],
        ["FluxIndex.Core.Domain.Models.SmallToBigOptions"] = ["MaxWindowSize", "TimeoutMs"],
        ["FluxIndex.Core.Application.Models.AIMetadataExtractionOptions"] = ["CacheTTL", "ContinueOnFailure", "CustomPrompt", "EnableAdaptiveSampling", "EnableCaching", "MaxRetries", "MaxTokens", "MinConfidence", "RetryDelayMs", "Strategy", "TimeoutMs"],
        ["FluxIndex.Integrations.FileFlux.FileFluxOptions"] = ["EnableLlmRefine", "LlmRefineOptions"],
        ["FluxIndex.SDK.SearchOptions"] = ["IncludeVectors"],
        // Moving to Iyu.Conventions.Testing 0.3.0 (2026-10-03) found options whose only reads copied the value into the
        // same property of another instance. EntityGraphOptions.AutoMigrate is now honoured (0.72.0); IvfflatLists,
        // RedisCacheStoreOptions.EnableDetailedLogging and SQLiteVecOptions.DefaultMinScore were removed. The HNSW
        // auto-tuning surface (IVectorIndexBenchmark and the tuner/monitor built on it, with HnswBenchmarkOptions and
        // HnswAutoTuningOptions) was removed in 0.73.0: nothing implemented the benchmark, so none of it could run.
        // 2026-10-10: the semantic cache thresholds (SDK SemanticCacheOptions, SQLiteCacheOptions, PostgresCacheOptions)
        // and HybridSearchOptions.TimeoutMs are honoured. Removed: HybridSearchOptions.EnableDiversity/DiversityThreshold
        // (no diversity step exists), VectorSearchOptions.SimilarityMetric (fixed by the store) and its keyword knobs
        // BooleanOperator/EnablePhraseSearch/EnableTermExpansion (SparseSearchOptions carries the read ones),
        // SearchOptions.GraphRAGOptions (graph queries go to IGraphRAGService.QueryAsync), and SQLiteVecOptions.IndexType
        // (vec0 is flat only), Fts5Bm25Weights (FTS5 bm25() takes column weights, not b/k1) and
        // BatchTransactionCommitInterval (each MaxBatchSize batch is its own transaction).
    };

    private static readonly Lazy<OptionsReachabilityReport> Result = new(() =>
        // Option-shaped types are named three ways in this tree: *Options, the builder's *Configuration blocks, and
        // *Defaults (ChunkingDefaults). The scan itself is Iyu.Conventions.Testing's, shared with the other repositories.
        OptionsReachability.Scan(LibraryAssemblies(), OptionsTypes.NamedWith("Options", "Configuration", "Defaults")));

    [Fact]
    public void EveryPublicOption_IsReadByTheLibrary_ExceptTheKnownRoster() =>
        Result.Value.ShouldMatchRoster(KnownUnread);

    // Positive controls: the scan must see reads it is known to have — a same-assembly read, a read
    // through the options merge introduced in 0.37.2, and a read from another assembly — or an empty
    // roster above would pass because the detector sees nothing.
    [Fact]
    public void Scan_SeesKnownReads()
    {
        var scan = Result.Value;
        Assert.True(scan.OptionTypes.Count > 40, $"the scan must find the library's options types (found {scan.OptionTypes.Count})");
        Assert.Contains("FluxIndex.Core.Application.Interfaces.EntityGraphBuildOptions.BatchSize", scan.Read);
        Assert.Contains("FluxIndex.Core.Application.Interfaces.GraphRAGBuildOptions.EntityOptions", scan.Read);
        Assert.Contains("FluxIndex.Core.Application.Interfaces.EntityExtractionOptions.UseLlm", scan.Read);
        Assert.Contains("FluxIndex.Core.Application.Interfaces.EntityExtractionOptions.Language", scan.Read);
        Assert.Contains("FluxIndex.Core.Application.Interfaces.GraphRAGQueryOptions.IncludeContext", scan.Read);
        Assert.NotEmpty(scan.CrossAssemblyReads);
    }

    // Every FluxIndex library assembly copied next to the tests — not the tests themselves. Loaded by
    // name because the compiler drops a project reference the test code never names.
    private static List<Assembly> LibraryAssemblies()
        => [.. Directory.EnumerateFiles(AppContext.BaseDirectory, "FluxIndex.*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && !name.EndsWith(".Tests", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name => AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(name!)))];
}
