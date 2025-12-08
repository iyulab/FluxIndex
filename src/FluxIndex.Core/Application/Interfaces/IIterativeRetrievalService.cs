using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Iterative Retrieval Service interface implementing advanced retrieval patterns.
/// Supports IRCOT (Interleaving Retrieval with Chain-of-Thought), multi-hop reasoning,
/// and agentic retrieval with planning and execution.
///
/// References:
/// - IRCOT: "Interleaving Retrieval with Chain-of-Thought Reasoning" (2023)
/// - ReAct: "Reasoning and Acting in Language Models" (2022)
/// - Self-Ask: "Measuring and Narrowing the Compositionality Gap" (2022)
/// </summary>
public interface IIterativeRetrievalService
{
    /// <summary>
    /// Performs iterative retrieval with chain-of-thought reasoning.
    /// Alternates between retrieval and reasoning until the query is answered.
    /// </summary>
    /// <param name="query">User query</param>
    /// <param name="options">Iterative retrieval options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Iterative retrieval result with full reasoning trace</returns>
    Task<IterativeRetrievalResult> RetrieveWithReasoningAsync(
        string query,
        IterativeRetrievalOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decomposes a complex query into sub-questions and retrieves for each.
    /// Implements Self-Ask pattern for compositional queries.
    /// </summary>
    /// <param name="query">Complex query to decompose</param>
    /// <param name="options">Decomposition options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Decomposition result with sub-question retrieval</returns>
    Task<IterativeDecompositionResult> DecomposeAndRetrieveAsync(
        string query,
        QueryDecompositionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs multi-hop retrieval following entity relationships.
    /// Retrieves, extracts entities, then retrieves related documents.
    /// </summary>
    /// <param name="query">Initial query</param>
    /// <param name="options">Multi-hop options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Multi-hop retrieval result with hop trace</returns>
    Task<MultiHopRetrievalResult> MultiHopRetrieveAsync(
        string query,
        MultiHopOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs agentic retrieval with planning and execution loop.
    /// Plans retrieval steps, executes, evaluates, and adapts.
    /// </summary>
    /// <param name="query">User query</param>
    /// <param name="options">Agent options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Agentic retrieval result with execution trace</returns>
    Task<AgenticRetrievalResult> AgenticRetrieveAsync(
        string query,
        AgenticRetrievalOptions? options = null,
        CancellationToken cancellationToken = default);
}

#region Options Classes

/// <summary>
/// Options for iterative retrieval with reasoning
/// </summary>
public class IterativeRetrievalOptions
{
    /// <summary>
    /// Maximum reasoning-retrieval iterations
    /// </summary>
    public int MaxIterations { get; set; } = 5;

    /// <summary>
    /// Maximum documents to retrieve per iteration
    /// </summary>
    public int MaxDocsPerIteration { get; set; } = 5;

    /// <summary>
    /// Whether to use LLM for reasoning
    /// </summary>
    public bool UseLlmReasoning { get; set; } = true;

    /// <summary>
    /// Confidence threshold to stop iterations (0-1)
    /// </summary>
    public float ConfidenceThreshold { get; set; } = 0.8f;

    /// <summary>
    /// Whether to include reasoning trace in results
    /// </summary>
    public bool IncludeReasoningTrace { get; set; } = true;

    /// <summary>
    /// Temperature for LLM reasoning
    /// </summary>
    public float ReasoningTemperature { get; set; } = 0.3f;

    /// <summary>
    /// Whether to deduplicate retrieved documents across iterations
    /// </summary>
    public bool DeduplicateAcrossIterations { get; set; } = true;

    /// <summary>
    /// Maximum total documents across all iterations
    /// </summary>
    public int MaxTotalDocs { get; set; } = 20;
}

/// <summary>
/// Options for query decomposition
/// </summary>
public class QueryDecompositionOptions
{
    /// <summary>
    /// Maximum sub-questions to generate
    /// </summary>
    public int MaxSubQuestions { get; set; } = 5;

    /// <summary>
    /// Maximum depth for recursive decomposition
    /// </summary>
    public int MaxDecompositionDepth { get; set; } = 2;

    /// <summary>
    /// Whether to retrieve for each sub-question
    /// </summary>
    public bool RetrievePerSubQuestion { get; set; } = true;

    /// <summary>
    /// Maximum documents per sub-question
    /// </summary>
    public int MaxDocsPerSubQuestion { get; set; } = 3;

    /// <summary>
    /// Whether to synthesize answers for sub-questions
    /// </summary>
    public bool SynthesizeSubAnswers { get; set; } = true;

    /// <summary>
    /// Whether to compose final answer from sub-answers
    /// </summary>
    public bool ComposeFinalAnswer { get; set; } = true;
}

/// <summary>
/// Options for multi-hop retrieval
/// </summary>
public class MultiHopOptions
{
    /// <summary>
    /// Maximum number of hops
    /// </summary>
    public int MaxHops { get; set; } = 3;

    /// <summary>
    /// Maximum entities to follow per hop
    /// </summary>
    public int MaxEntitiesPerHop { get; set; } = 3;

    /// <summary>
    /// Maximum documents per hop
    /// </summary>
    public int MaxDocsPerHop { get; set; } = 5;

    /// <summary>
    /// Entity types to follow (null = all types)
    /// </summary>
    public IReadOnlyList<NamedEntityType>? EntityTypesToFollow { get; set; }

    /// <summary>
    /// Minimum entity confidence to follow
    /// </summary>
    public float MinEntityConfidence { get; set; } = 0.7f;

    /// <summary>
    /// Whether to stop when answer is found
    /// </summary>
    public bool StopOnAnswerFound { get; set; } = true;

    /// <summary>
    /// Whether to track entity relationships across hops
    /// </summary>
    public bool TrackRelationships { get; set; } = true;
}

/// <summary>
/// Options for agentic retrieval
/// </summary>
public class AgenticRetrievalOptions
{
    /// <summary>
    /// Maximum planning-execution iterations
    /// </summary>
    public int MaxIterations { get; set; } = 5;

    /// <summary>
    /// Available tools for the agent
    /// </summary>
    public IReadOnlyList<RetrievalTool>? AvailableTools { get; set; }

    /// <summary>
    /// Whether to reflect on execution results
    /// </summary>
    public bool EnableReflection { get; set; } = true;

    /// <summary>
    /// Whether to adapt plan based on results
    /// </summary>
    public bool EnableAdaptivePlanning { get; set; } = true;

    /// <summary>
    /// Maximum documents total
    /// </summary>
    public int MaxTotalDocs { get; set; } = 30;

    /// <summary>
    /// Success criteria for early termination
    /// </summary>
    public string? SuccessCriteria { get; set; }
}

/// <summary>
/// Available retrieval tools for agentic retrieval
/// </summary>
public enum RetrievalTool
{
    /// <summary>Vector similarity search</summary>
    VectorSearch,
    /// <summary>Keyword/BM25 search</summary>
    KeywordSearch,
    /// <summary>Hybrid search</summary>
    HybridSearch,
    /// <summary>Entity-based search</summary>
    EntitySearch,
    /// <summary>Graph traversal</summary>
    GraphTraversal,
    /// <summary>Community search</summary>
    CommunitySearch,
    /// <summary>Query reformulation</summary>
    QueryReformulation,
    /// <summary>Reranking</summary>
    Reranking
}

#endregion

#region Result Classes

/// <summary>
/// Search result for iterative retrieval operations.
/// This is a dedicated type to avoid conflicts with Domain.Entities.SearchResult
/// and Application.Interfaces.SearchResult.
/// </summary>
public class IterativeSearchResult
{
    /// <summary>
    /// Unique identifier (chunk ID)
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Document ID
    /// </summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>
    /// Chunk ID
    /// </summary>
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>
    /// Document content
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Search/relevance score
    /// </summary>
    public float Score { get; init; }

    /// <summary>
    /// Chunk index within document
    /// </summary>
    public int ChunkIndex { get; init; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates an IterativeSearchResult from a HybridSearchResult
    /// </summary>
    public static IterativeSearchResult FromHybridResult(Domain.Models.HybridSearchResult result)
    {
        return new IterativeSearchResult
        {
            Id = result.Chunk.Id,
            DocumentId = result.Chunk.DocumentId,
            ChunkId = result.Chunk.Id,
            Content = result.Chunk.Content,
            Score = (float)result.FusedScore,
            ChunkIndex = result.Chunk.ChunkIndex,
            Metadata = result.FusionMetadata
        };
    }
}

/// <summary>
/// Result from iterative retrieval with reasoning
/// </summary>
public class IterativeRetrievalResult
{
    /// <summary>
    /// Final retrieved documents
    /// </summary>
    public IReadOnlyList<IterativeSearchResult> Documents { get; init; } = Array.Empty<IterativeSearchResult>();

    /// <summary>
    /// Reasoning iterations
    /// </summary>
    public IReadOnlyList<ReasoningIteration> Iterations { get; init; } = Array.Empty<ReasoningIteration>();

    /// <summary>
    /// Final answer if synthesized
    /// </summary>
    public string? FinalAnswer { get; init; }

    /// <summary>
    /// Confidence in the final result (0-1)
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Whether the query was fully answered
    /// </summary>
    public bool IsComplete { get; init; }

    /// <summary>
    /// Reason for stopping iterations
    /// </summary>
    public string? StopReason { get; init; }

    /// <summary>
    /// Total processing time
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// Statistics about the retrieval process
    /// </summary>
    public IterativeRetrievalStats Stats { get; init; } = new();
}

/// <summary>
/// A single reasoning iteration
/// </summary>
public class ReasoningIteration
{
    /// <summary>
    /// Iteration number (1-based)
    /// </summary>
    public int IterationNumber { get; init; }

    /// <summary>
    /// Reasoning thought for this iteration
    /// </summary>
    public string Thought { get; init; } = string.Empty;

    /// <summary>
    /// Action taken (e.g., "retrieve", "conclude")
    /// </summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>
    /// Action input (e.g., refined query)
    /// </summary>
    public string ActionInput { get; init; } = string.Empty;

    /// <summary>
    /// Retrieved documents in this iteration
    /// </summary>
    public IReadOnlyList<IterativeSearchResult> RetrievedDocs { get; init; } = Array.Empty<IterativeSearchResult>();

    /// <summary>
    /// Observation from retrieval results
    /// </summary>
    public string Observation { get; init; } = string.Empty;

    /// <summary>
    /// Confidence after this iteration
    /// </summary>
    public float Confidence { get; init; }
}

/// <summary>
/// Result from iterative query decomposition (Self-Ask pattern)
/// Note: This is distinct from Domain.Models.QueryDecompositionResult which is simpler
/// </summary>
public class IterativeDecompositionResult
{
    /// <summary>
    /// Original query
    /// </summary>
    public string OriginalQuery { get; init; } = string.Empty;

    /// <summary>
    /// Generated sub-questions with retrieval results
    /// </summary>
    public IReadOnlyList<SubQuestion> SubQuestions { get; init; } = Array.Empty<SubQuestion>();

    /// <summary>
    /// Final composed answer
    /// </summary>
    public string? ComposedAnswer { get; init; }

    /// <summary>
    /// All retrieved documents across sub-questions
    /// </summary>
    public IReadOnlyList<IterativeSearchResult> AllDocuments { get; init; } = Array.Empty<IterativeSearchResult>();

    /// <summary>
    /// Confidence in the composed answer
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Processing time
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }
}

/// <summary>
/// A sub-question from decomposition
/// </summary>
public class SubQuestion
{
    /// <summary>
    /// Sub-question text
    /// </summary>
    public string Question { get; init; } = string.Empty;

    /// <summary>
    /// Dependency on other sub-questions (indices)
    /// </summary>
    public IReadOnlyList<int> Dependencies { get; init; } = Array.Empty<int>();

    /// <summary>
    /// Retrieved documents for this sub-question
    /// </summary>
    public IReadOnlyList<IterativeSearchResult> Documents { get; init; } = Array.Empty<IterativeSearchResult>();

    /// <summary>
    /// Synthesized answer for this sub-question
    /// </summary>
    public string? Answer { get; init; }

    /// <summary>
    /// Confidence in the answer
    /// </summary>
    public float Confidence { get; init; }
}

/// <summary>
/// Result from multi-hop retrieval
/// </summary>
public class MultiHopRetrievalResult
{
    /// <summary>
    /// All hops performed
    /// </summary>
    public IReadOnlyList<RetrievalHop> Hops { get; init; } = Array.Empty<RetrievalHop>();

    /// <summary>
    /// Final aggregated documents
    /// </summary>
    public IReadOnlyList<IterativeSearchResult> FinalDocuments { get; init; } = Array.Empty<IterativeSearchResult>();

    /// <summary>
    /// Entity relationship graph discovered
    /// </summary>
    public EntityGraph? DiscoveredGraph { get; init; }

    /// <summary>
    /// Answer if found
    /// </summary>
    public string? Answer { get; init; }

    /// <summary>
    /// Reasoning path taken
    /// </summary>
    public string ReasoningPath { get; init; } = string.Empty;

    /// <summary>
    /// Processing time
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }
}

/// <summary>
/// A single hop in multi-hop retrieval
/// </summary>
public class RetrievalHop
{
    /// <summary>
    /// Hop number (0-based)
    /// </summary>
    public int HopNumber { get; init; }

    /// <summary>
    /// Query used for this hop
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Entities that triggered this hop
    /// </summary>
    public IReadOnlyList<ExtractedEntity> TriggerEntities { get; init; } = Array.Empty<ExtractedEntity>();

    /// <summary>
    /// Documents retrieved in this hop
    /// </summary>
    public IReadOnlyList<IterativeSearchResult> Documents { get; init; } = Array.Empty<IterativeSearchResult>();

    /// <summary>
    /// Entities extracted from this hop's documents
    /// </summary>
    public IReadOnlyList<ExtractedEntity> ExtractedEntities { get; init; } = Array.Empty<ExtractedEntity>();

    /// <summary>
    /// Reasoning for this hop
    /// </summary>
    public string Reasoning { get; init; } = string.Empty;
}

/// <summary>
/// Result from agentic retrieval
/// </summary>
public class AgenticRetrievalResult
{
    /// <summary>
    /// Execution trace
    /// </summary>
    public IReadOnlyList<AgentAction> ExecutionTrace { get; init; } = Array.Empty<AgentAction>();

    /// <summary>
    /// Final retrieved documents
    /// </summary>
    public IReadOnlyList<IterativeSearchResult> Documents { get; init; } = Array.Empty<IterativeSearchResult>();

    /// <summary>
    /// Final answer if generated
    /// </summary>
    public string? FinalAnswer { get; init; }

    /// <summary>
    /// Whether the goal was achieved
    /// </summary>
    public bool GoalAchieved { get; init; }

    /// <summary>
    /// Reflection on the execution
    /// </summary>
    public string? Reflection { get; init; }

    /// <summary>
    /// Processing time
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }
}

/// <summary>
/// An action taken by the retrieval agent
/// </summary>
public class AgentAction
{
    /// <summary>
    /// Action number (1-based)
    /// </summary>
    public int ActionNumber { get; init; }

    /// <summary>
    /// Tool used
    /// </summary>
    public RetrievalTool Tool { get; init; }

    /// <summary>
    /// Thought/reasoning before action
    /// </summary>
    public string Thought { get; init; } = string.Empty;

    /// <summary>
    /// Action input/parameters
    /// </summary>
    public Dictionary<string, object> Input { get; init; } = new();

    /// <summary>
    /// Action result/observation
    /// </summary>
    public string Observation { get; init; } = string.Empty;

    /// <summary>
    /// Documents retrieved by this action
    /// </summary>
    public IReadOnlyList<IterativeSearchResult> Documents { get; init; } = Array.Empty<IterativeSearchResult>();

    /// <summary>
    /// Whether this action was successful
    /// </summary>
    public bool Success { get; init; }
}

/// <summary>
/// Statistics from iterative retrieval
/// </summary>
public class IterativeRetrievalStats
{
    /// <summary>
    /// Total iterations performed
    /// </summary>
    public int TotalIterations { get; init; }

    /// <summary>
    /// Total documents retrieved
    /// </summary>
    public int TotalDocuments { get; init; }

    /// <summary>
    /// Unique documents after deduplication
    /// </summary>
    public int UniqueDocuments { get; init; }

    /// <summary>
    /// Total LLM calls made
    /// </summary>
    public int LlmCalls { get; init; }

    /// <summary>
    /// Total retrieval calls made
    /// </summary>
    public int RetrievalCalls { get; init; }

    /// <summary>
    /// Average documents per iteration
    /// </summary>
    public float AvgDocsPerIteration { get; init; }
}

#endregion
