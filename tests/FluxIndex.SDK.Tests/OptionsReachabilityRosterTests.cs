using System.Reflection;
using System.Runtime.Loader;
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
        ["FluxIndex.Cache.Redis.Configuration.RedisSemanticCacheOptions"] = ["AutoCompactionInterval", "CleanupRatio", "CleanupThreshold", "CommandTimeoutSeconds", "ConnectionTimeoutSeconds", "DefaultSimilarityThreshold", "DomainWeights", "EnableAutoCompaction", "EnableDetailedLogging", "EnableMetrics", "EnableQueryNormalization", "EnableVectorCompression", "KeyPrefix", "RetryCount", "RetryDelay", "StatisticsInterval", "WarmupQueries"],
        ["FluxIndex.Core.Application.Interfaces.AdaptiveSearchOptions"] = ["EnableDetailedLogging", "Timeout", "UserContext"],
        ["FluxIndex.Core.Application.Interfaces.AgenticRetrievalOptions"] = ["EnableAdaptivePlanning"],
        ["FluxIndex.Core.Application.Interfaces.AnswerSynthesisOptions"] = ["StructuredAnswer"],
        ["FluxIndex.Core.Application.Interfaces.CacheMaintenanceOptions"] = ["CompactStorage", "TargetMemoryUsagePercent", "UpdateStatistics"],
        ["FluxIndex.Core.Application.Interfaces.CacheWarmupOptions"] = ["MaxDuration", "TopHotChunksCount", "WarmupEmbeddings", "WarmupEntities"],
        ["FluxIndex.Core.Application.Interfaces.ColBERTCompressionOptions"] = ["ProductQuantizationCodebookSize", "ProductQuantizationSubvectors"],
        ["FluxIndex.Core.Application.Interfaces.ColBERTOptions"] = ["UseSimd"],
        ["FluxIndex.Core.Application.Interfaces.ContextExpansionOptions"] = ["MaxExpansionDistance"],
        ["FluxIndex.Core.Application.Interfaces.ContextualHeaderOptions"] = ["UsePromptCaching"],
        ["FluxIndex.Core.Application.Interfaces.CorrectiveRAGOptions"] = ["AmbiguousThreshold", "CorrectThreshold", "EnableDetailedLogging", "EnableWebSearch", "RetryCount", "Timeout"],
        ["FluxIndex.Core.Application.Interfaces.EmbeddingGenerationOptions"] = ["GenerateQuestionEmbeddings", "HyDEDocumentCount", "MaxQuestions", "ModelId", "Types", "UseCache"],
        ["FluxIndex.Core.Application.Interfaces.EnrichmentEntityOptions"] = ["EntityTypes", "LinkExternalKnowledge", "ResolveCoreferences"],
        ["FluxIndex.Core.Application.Interfaces.EnrichmentOptions"] = ["AnalyzeQuality", "CacheEmbeddings", "ExtractRelationships", "GenerateEntityEmbedding", "GraphBuildOptions", "MinEntityConfidence"],
        ["FluxIndex.Core.Application.Interfaces.EntityGraphMergeOptions"] = ["UseEmbeddingsForMatching"],
        ["FluxIndex.Core.Application.Interfaces.EntityLinkingOptions"] = ["SimilarityThreshold", "UseEmbeddings", "UseFuzzyMatching"],
        ["FluxIndex.Core.Application.Interfaces.EntitySearchOptions"] = ["PriorityEntityTypes"],
        ["FluxIndex.Core.Application.Interfaces.GlobalSearchOptions"] = ["ScoreConfidence"],
        ["FluxIndex.Core.Application.Interfaces.GraphBuildOptions"] = ["CalculateImportanceScores", "MaxCommunityIterations", "MergeThreshold"],
        ["FluxIndex.Core.Application.Interfaces.GraphRAGBuildOptions"] = ["GenerateEntityEmbeddings"],
        ["FluxIndex.Core.Application.Interfaces.GraphStoreTraversalOptions"] = ["EntityTypes", "IncludeEmbeddings", "IncludeEvidence"],
        ["FluxIndex.Core.Application.Interfaces.GraphTraversalOptions"] = ["DocumentIdFilter"],
        ["FluxIndex.Core.Application.Interfaces.IterativeRetrievalOptions"] = ["IncludeReasoningTrace"],
        ["FluxIndex.Core.Application.Interfaces.ListwiseRerankOptions"] = ["IncludeExplanation", "UseAttentionScoring", "UseLlm"],
        ["FluxIndex.Core.Application.Interfaces.LocalSearchOptions"] = ["UseEntityEmbeddings"],
        ["FluxIndex.Core.Application.Interfaces.PathFindingOptions"] = ["TimeoutMs", "UseRelationshipStrength", "WeightType"],
        ["FluxIndex.Core.Application.Interfaces.QuantizationOptions"] = ["NormalizeVectors", "TrainingSamples"],
        ["FluxIndex.Core.Application.Interfaces.QueryDecompositionOptions"] = ["MaxDecompositionDepth"],
        ["FluxIndex.Core.Application.Interfaces.RerankOptions"] = ["Model", "ModelParameters"],
        ["FluxIndex.Core.Application.Interfaces.SelfRAGOptions"] = ["EnableContextExpansion", "EnableMultiPerspectiveSearch", "SearchTimeout"],
        ["FluxIndex.Core.Application.Interfaces.SemanticCacheOptions"] = ["AutoOptimizationInterval", "CompressionThreshold", "DefaultExpiry", "DefaultSimilarityThreshold", "EnableAutoOptimization", "EnableCompression", "EnablePerformanceTracking", "MaxCacheSize", "MaxMemoryMB", "MaxQueryLength", "MinQueryLength", "SimilaritySearchBatchSize"],
        ["FluxIndex.Core.Application.Interfaces.VerificationOptions"] = ["CustomCriteria", "IncludeDetailedReasoning", "MaxHallucinationRisk"],
        ["FluxIndex.Core.Application.Models.ClassificationOptions"] = ["CacheExpirationHours", "Enabled"],
        ["FluxIndex.Core.Application.Models.ClassificationValidationOptions"] = ["DuplicateThreshold"],
        ["FluxIndex.Core.Application.Services.AgenticRetrievalRouterOptions"] = ["DefaultMaxResults", "EnableAdaptiveRouting", "EnableDetailedExplanations", "EnablePerformanceTracking", "MaxFallbackAttempts", "MinRoutingConfidence", "StrategyTimeout"],
        ["FluxIndex.Core.Application.Services.ContextualEmbeddingOptions"] = ["GenerateDualEmbeddings", "MaxCombinedLength"],
        ["FluxIndex.Core.Application.Services.CorrectiveRAGServiceOptions"] = ["MaxRetries"],
        ["FluxIndex.Core.Application.Services.QuantizedVectorStoreOptions"] = ["DefaultCandidateMultiplier", "StoreOriginalEmbeddings"],
        ["FluxIndex.Core.Application.Services.QueryTransformationOptions"] = ["CacheDurationMinutes", "EnableCaching"],
        ["FluxIndex.Core.Application.Services.SelfRAGServiceOptions"] = ["DefaultMaxIterations", "DefaultQualityThreshold"],
        ["FluxIndex.Core.Domain.Models.BatchProcessingOptions"] = ["BatchSize", "MaxRetries", "ReportProgress", "RetryDelay", "StopOnError"],
        ["FluxIndex.Core.Domain.Models.HnswAutoTuningOptions"] = ["MaxIterations", "MaxMemoryUsageBytes", "MaxTuningTimeMs"],
        ["FluxIndex.Core.Domain.Models.HnswBenchmarkOptions"] = ["AccuracyK", "MaxTestTimeMs", "ParameterSets", "TestQueryCount"],
        ["FluxIndex.Core.Domain.Models.HybridSearchOptions"] = ["DiversityThreshold", "EnableDiversity", "Filters", "TimeoutMs"],
        ["FluxIndex.Core.Domain.Models.MetadataExtractionOptions"] = ["AnalyzeSentiment", "CalculateImportance", "DetectLanguage", "EnableParallelProcessing", "ExtractEntities", "ExtractKeywords", "GenerateSummary", "MaxConcurrentRequests", "MaxKeywords", "MaxSummaryLength", "QualityThreshold"],
        ["FluxIndex.Core.Domain.Models.QuOTEOptions"] = ["DiversityLevel", "DomainWeights"],
        ["FluxIndex.Core.Domain.Models.SmallToBigOptions"] = ["MaxWindowSize", "TimeoutMs"],
        ["FluxIndex.Core.Domain.Models.VectorSearchOptions"] = ["BooleanOperator", "EnablePhraseSearch", "EnableTermExpansion", "SimilarityMetric"],
        ["FluxIndex.Core.Domain.ValueObjects.CacheOptions"] = ["BatchSize", "CacheKeyPrefix", "DefaultExpiry", "DefaultSimilarityThreshold", "EnableAutoOptimization", "EnableCompression", "EnableStatistics", "EnableWarmup", "MaxCacheSize", "MaxMemoryUsageBytes", "OptimizationInterval", "RedisConnectionString"],
        ["FluxIndex.Core.Models.AIMetadataExtractionOptions"] = ["CacheTTL", "ContinueOnFailure", "CustomPrompt", "EnableAdaptiveSampling", "EnableCaching", "MaxRetries", "MaxTokens", "MinConfidence", "RetryDelayMs", "Strategy", "TimeoutMs"],
        ["FluxIndex.Core.Options.BatchProcessingOptions"] = ["ContinueOnFailure", "DelayBetweenBatches", "ProgressCallback", "Size"],
        ["FluxIndex.Core.Options.MetadataExtractionOptions"] = ["BatchSize", "EnableCostTracking", "EnableDebugLogging", "EnableQualityScoring", "IsValid", "MaxConcurrency", "MaxEntities", "MaxKeywords", "MaxQuestions", "MaxRetries", "MinQualityThreshold", "PromptTemplate", "Timeout"],
        ["FluxIndex.Core.Options.QueryTransformationOptions"] = ["CacheExpiration", "DefaultTimeout", "EnableCaching", "EnableParallelProcessing", "EnableQualityFiltering", "IsValid", "MaxConcurrentRequests", "MinQualityThreshold"],
        ["FluxIndex.Core.Services.HNSWOptimizerOptions"] = ["EnableAdvancedOptimizations", "Weights"],
        ["FluxIndex.Integrations.FileFlux.FileFluxOptions"] = ["EnableLlmRefine", "LlmRefineOptions"],
        ["FluxIndex.Integrations.FileFlux.Processing.DocumentProcessingOptions"] = ["EnableTextCleaning"],
        ["FluxIndex.Integrations.WebFlux.WebFluxOptions"] = ["DefaultIncludeImages"],
        ["FluxIndex.SDK.Configuration.CacheOptions"] = ["CacheDuration", "CacheTTL", "MaxCacheSize"],
        ["FluxIndex.SDK.Configuration.EmbeddingOptions"] = ["ApiKey", "BatchSize", "EnableCache", "MaxRetries", "ModelName", "ProviderSpecificOptions", "RetryDelay"],
        ["FluxIndex.SDK.Configuration.FluxIndexOptions"] = ["RAGEnhancement"],
        ["FluxIndex.SDK.Configuration.QualityMonitoringOptions"] = ["AlertCheckInterval", "EnableMonitoring", "EnableRealTimeAlerts", "MaxMetricsHistory", "MetricsInterval"],
        ["FluxIndex.SDK.Configuration.RAGEnhancementOptions"] = ["ContextualRetrieval", "IsAutoMode", "IsEnabled", "LateChunking", "Mode", "MultiHyDE"],
        ["FluxIndex.SDK.Configuration.SemanticCacheOptions"] = ["SimilarityThreshold"],
        ["FluxIndex.SDK.Configuration.VectorStoreOptions"] = ["ConnectionTimeout", "MaxConnections", "ProviderSpecificOptions", "QdrantHttpPort", "QdrantUseHttps"],
        ["FluxIndex.SDK.FacetSearchOptions"] = ["FacetFields", "MaxFacetValues"],
        ["FluxIndex.SDK.IndexerOptions"] = ["ChunkingStrategy"],
        ["FluxIndex.SDK.IndexingOptions"] = ["ChunkingStrategy", "EnableOCR", "ExtractMetadata", "GenerateEmbeddings", "MaxChunkSize", "OverlapSize"],
        ["FluxIndex.SDK.KeywordSearchOptions"] = ["CaseSensitive", "SearchFields", "UseFullTextSearch"],
        ["FluxIndex.SDK.RerankingOptions"] = ["RerankingModel", "Strategy", "TopK"],
        ["FluxIndex.SDK.RetrieverOptions"] = ["DefaultMaxResults", "DefaultMinScore"],
        ["FluxIndex.SDK.SearchOptions"] = ["GraphRAGOptions", "IncludeVectors"],
        ["FluxIndex.SDK.SemanticSearchOptions"] = ["EmbeddingModel", "UseCache"],
        ["FluxIndex.SDK.SimilarityOptions"] = ["ExcludeSelf", "SimilarityThreshold"],
        ["FluxIndex.Storage.Neo4j.Neo4jOptions"] = ["Encrypted", "NodeLabelPrefix"],
        ["FluxIndex.Storage.PostgreSQL.Cache.PostgresCacheOptions"] = ["SimilarityThreshold"],
        ["FluxIndex.Storage.Qdrant.QdrantOptions"] = ["CollectionName", "HttpPort"],
        ["FluxIndex.Storage.SQLite.Cache.SQLiteCacheOptions"] = ["SimilarityThreshold"],
        ["FluxIndex.Storage.SQLite.SQLiteOptions"] = ["AllowDuplicates", "BatchSize", "DefaultSearchThreshold", "DefaultVectorWeight", "EnableVectorCache", "VectorCacheSize"],
        ["FluxIndex.Storage.SQLite.SQLiteVecOptions"] = ["BatchTransactionCommitInterval", "Fts5Bm25Weights", "IndexType"],
    };

    private static readonly Lazy<Scan> Result = new(Run);

    [Fact]
    public void EveryPublicOption_IsReadByTheLibrary_ExceptTheKnownRoster()
    {
        // One line per type, so a failure prints the whole roster it found — not just which keys differ.
        var unread = Result.Value.Unread
            .Where(kv => kv.Value.Length > 0)
            .Select(kv => $"{kv.Key}: {string.Join(",", kv.Value)}")
            .Order(StringComparer.Ordinal)
            .ToList();
        var expected = KnownUnread
            .Select(kv => $"{kv.Key}: {string.Join(",", kv.Value.Order(StringComparer.Ordinal))}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(expected.SequenceEqual(unread),
            "a public option nothing in the library reads is a promise it does not keep. Wire it, or change " +
            "this roster as a deliberate decision and keep the option's documentation honest about it.\n" +
            "found:\n  " + string.Join("\n  ", unread) + "\nexpected:\n  " + string.Join("\n  ", expected));
    }

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

    private sealed record Scan(
        IReadOnlyList<Type> OptionTypes,
        IReadOnlyDictionary<string, string[]> Unread,
        IReadOnlySet<string> Read,
        IReadOnlySet<string> CrossAssemblyReads);

    private static Scan Run()
    {
        var assemblies = LibraryAssemblies();
        var optionTypes = assemblies
            .SelectMany(SafeTypes)
            .Where(t => t is { IsPublic: true, IsClass: true, IsAbstract: false } || t is { IsNestedPublic: true, IsClass: true, IsAbstract: false })
            .Where(t => t.Name.EndsWith("Options", StringComparison.Ordinal))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        // (module, getter token) -> "Type.Property", for properties each type declares itself.
        var getters = new Dictionary<(Module, int), (Type Type, string Name)>();
        foreach (var type in optionTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (property.GetMethod is { IsPublic: true } getter)
                {
                    getters[(getter.Module, getter.MetadataToken)] = (type, property.Name);
                }
            }
        }

        var read = new HashSet<string>(StringComparer.Ordinal);
        var crossAssembly = new HashSet<string>(StringComparer.Ordinal);
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                 BindingFlags.Static | BindingFlags.DeclaredOnly;

        // Reads inside an options type count only through a member the library calls from outside it —
        // a Validate() or a computed property the library consults is how such an option is honoured.
        // Copies and constructors are the exception: a copy is not a use.
        var readsInside = new Dictionary<(Module, int), List<string>>();
        var calledFromOutside = new HashSet<(Module, int)>();
        foreach (var assembly in assemblies)
        {
            foreach (var type in SafeTypes(assembly))
            {
                var owner = optionTypes.FirstOrDefault(o => IsWithin(type, o));
                IEnumerable<MethodBase> bodies = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
                foreach (var method in bodies)
                {
                    foreach (var target in Calls(method, type.Module))
                    {
                        var targetKey = (target.Module, target.MetadataToken);
                        if (getters.TryGetValue(targetKey, out var option))
                        {
                            var key = $"{option.Type.FullName}.{option.Name}";
                            if (owner == option.Type)
                            {
                                if (method is MethodInfo && !IsCopy(method) && owner == method.DeclaringType)
                                {
                                    var methodKey = (method.Module, method.MetadataToken);
                                    if (!readsInside.TryGetValue(methodKey, out var list))
                                        readsInside[methodKey] = list = [];
                                    list.Add(key);
                                }
                                continue;
                            }
                            read.Add(key);
                            if (type.Assembly != option.Type.Assembly)
                                crossAssembly.Add(key);
                        }
                        else if (target.DeclaringType is { } declaring
                                 && optionTypes.Contains(declaring)
                                 && !IsWithin(type, declaring))
                        {
                            calledFromOutside.Add(targetKey);
                        }
                    }
                }
            }
        }
        foreach (var (method, keys) in readsInside)
        {
            if (calledFromOutside.Contains(method))
                read.UnionWith(keys);
        }

        var unread = optionTypes.ToDictionary(
            t => t.FullName!,
            t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.GetMethod is { IsPublic: true })
                .Select(p => p.Name)
                .Where(name => !read.Contains($"{t.FullName}.{name}"))
                .Order(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
        return new Scan(optionTypes, unread, read, crossAssembly);
    }

    private static bool IsCopy(MethodBase method) =>
        method.Name is "Clone" or "Copy" or "<Clone>$" || method.Name.StartsWith("With", StringComparison.Ordinal);

    // Every method a body calls: call (0x28) / callvirt (0x6F) followed by a MethodDef (0x06) or
    // MemberRef (0x0A) token. A byte that merely looks like the opcode inside another operand yields a
    // token that resolves to something else, or to nothing; callers match exact methods only.
    private static IEnumerable<MethodBase> Calls(MethodBase method, Module module)
    {
        byte[]? il;
        try { il = method.GetMethodBody()?.GetILAsByteArray(); }
        catch (Exception) { yield break; }
        if (il is null) yield break;
        for (var i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] is not (0x28 or 0x6F)) continue;
            var token = BitConverter.ToInt32(il, i + 1);
            if ((token >> 24) is not (0x06 or 0x0A)) continue;
            MethodBase? target;
            try { target = module.ResolveMethod(token); }
            catch (Exception) { continue; }
            if (target is not null)
                yield return target;
        }
    }

    private static bool IsWithin(Type type, Type container)
    {
        for (var t = type; t is not null; t = t.DeclaringType)
        {
            if (t == container) return true;
        }
        return false;
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
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
