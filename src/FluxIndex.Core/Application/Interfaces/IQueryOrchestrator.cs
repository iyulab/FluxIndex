using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Orchestrates query transformation and routing strategies for advanced RAG
/// </summary>
public interface IQueryOrchestrator
{
    /// <summary>
    /// Analyzes a query to determine its complexity and optimal search strategy
    /// </summary>
    /// <param name="query">The user query to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Query analysis plan with recommended strategy</returns>
    Task<QueryPlan> AnalyzeQueryAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transforms a query using the specified strategy
    /// </summary>
    /// <param name="query">The original query</param>
    /// <param name="strategy">The transformation strategy to apply</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Transformed query or queries</returns>
    Task<TransformedQuery> TransformQueryAsync(
        string query,
        QueryStrategy strategy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes HyDE (Hypothetical Document Embeddings) transformation
    /// </summary>
    /// <param name="query">The original query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generated hypothetical document</returns>
    Task<string> GenerateHypotheticalDocumentAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decomposes a complex query into multiple simpler queries
    /// </summary>
    /// <param name="query">The complex query</param>
    /// <param name="maxQueries">Maximum number of sub-queries to generate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of decomposed queries</returns>
    Task<IEnumerable<string>> DecomposeQueryAsync(
        string query,
        int maxQueries = 3,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a step-back query for broader context retrieval
    /// </summary>
    /// <param name="query">The specific query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generalized step-back query</returns>
    Task<string> GenerateStepBackQueryAsync(
        string query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Query transformation strategies
/// </summary>
public enum QueryStrategy
{
    /// <summary>
    /// No transformation, use query as-is
    /// </summary>
    Direct,
    
    /// <summary>
    /// Hypothetical Document Embeddings
    /// </summary>
    HyDE,
    
    /// <summary>
    /// Multi-query decomposition
    /// </summary>
    MultiQuery,
    
    /// <summary>
    /// Step-back prompting for generalization
    /// </summary>
    StepBack,
    
    /// <summary>
    /// Combination of multiple strategies
    /// </summary>
    Adaptive
}

/// <summary>
/// Query analysis and execution plan
/// </summary>
public class QueryPlan
{
    public string OriginalQuery { get; set; } = string.Empty;
    public QueryComplexity Complexity { get; set; }
    public QueryStrategy RecommendedStrategy { get; set; }
    public List<string> DetectedEntities { get; set; } = new();
    public QueryIntent Intent { get; set; }
    public bool RequiresMultiHop { get; set; }
    public float ConfidenceScore { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Query complexity levels
/// </summary>
public enum QueryComplexity
{
    /// <summary>
    /// Simple factual query
    /// </summary>
    Simple,
    
    /// <summary>
    /// Moderate complexity requiring some inference
    /// </summary>
    Moderate,
    
    /// <summary>
    /// Complex query requiring multiple steps or reasoning
    /// </summary>
    Complex,
    
    /// <summary>
    /// Very complex query requiring multi-hop reasoning
    /// </summary>
    VeryComplex
}

/// <summary>
/// Query intent classification
/// </summary>
public enum QueryIntent
{
    /// <summary>
    /// Factual information retrieval
    /// </summary>
    Factual,
    
    /// <summary>
    /// Procedural or how-to query
    /// </summary>
    Procedural,
    
    /// <summary>
    /// Analytical or comparison query
    /// </summary>
    Analytical,
    
    /// <summary>
    /// Exploratory or open-ended query
    /// </summary>
    Exploratory,
    
    /// <summary>
    /// Definition or explanation query
    /// </summary>
    Definitional
}

/// <summary>
/// Result of query transformation
/// </summary>
public class TransformedQuery
{
    public string OriginalQuery { get; set; } = string.Empty;
    public QueryStrategy Strategy { get; set; }
    public List<string> Queries { get; set; } = new();
    public string PrimaryQuery { get; set; } = string.Empty;
    public Dictionary<string, float> QueryWeights { get; set; } = new();
    public Dictionary<string, object> TransformationMetadata { get; set; } = new();
}