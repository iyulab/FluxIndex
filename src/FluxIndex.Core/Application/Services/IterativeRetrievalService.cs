using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Implementation of iterative retrieval service supporting IRCOT,
/// multi-hop retrieval, query decomposition, and agentic retrieval patterns.
/// </summary>
public partial class IterativeRetrievalService : IIterativeRetrievalService
{
    private readonly IHybridSearchService _searchService;
    private readonly ITextCompletionService? _llmService;
    private readonly IAdvancedEntityExtractionService? _entityService;
    private readonly ILogger<IterativeRetrievalService> _logger;

    public IterativeRetrievalService(
        IHybridSearchService searchService,
        ITextCompletionService? llmService = null,
        IAdvancedEntityExtractionService? entityService = null,
        ILogger<IterativeRetrievalService>? logger = null)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _llmService = llmService;
        _entityService = entityService;
        _logger = logger ?? NullLogger<IterativeRetrievalService>.Instance;
    }

    #region IRCOT (Interleaving Retrieval with Chain-of-Thought)

    /// <inheritdoc />
    public async Task<IterativeRetrievalResult> RetrieveWithReasoningAsync(
        string query,
        IterativeRetrievalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new IterativeRetrievalOptions();
        var stopwatch = Stopwatch.StartNew();

        if (_logger.IsEnabled(LogLevel.Information))
            LogIterativeRetrieval8(_logger, query);

        var iterations = new List<ReasoningIteration>();
        var allDocs = new List<IterativeSearchResult>();
        var seenDocIds = new HashSet<string>();
        var currentQuery = query;
        var confidence = 0.0f;
        var isComplete = false;
        string? stopReason = null;
        string? finalAnswer = null;

        for (int i = 0; i < options.MaxIterations && !isComplete; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_logger.IsEnabled(LogLevel.Information))
                LogIterativeRetrieval7(_logger, i + 1, currentQuery);

            // Step 1: Reason about what to do next
            var (thought, action, actionInput) = await ReasonNextStepAsync(
                query, currentQuery, iterations, allDocs, options, cancellationToken);

            // Step 2: Execute action
            var iterationDocs = new List<IterativeSearchResult>();
            string observation;

            if (action.Equals("retrieve", StringComparison.OrdinalIgnoreCase))
            {
                var searchResults = await _searchService.SearchAsync(
                    actionInput,
                    new HybridSearchOptions { MaxResults = options.MaxDocsPerIteration },
                    cancellationToken);

                foreach (var result in searchResults)
                {
                    var doc = IterativeSearchResult.FromHybridResult(result);
                    if (options.DeduplicateAcrossIterations && seenDocIds.Contains(doc.ChunkId))
                        continue;

                    seenDocIds.Add(doc.ChunkId);
                    iterationDocs.Add(doc);
                    allDocs.Add(doc);

                    if (allDocs.Count >= options.MaxTotalDocs)
                        break;
                }

                observation = iterationDocs.Count > 0
                    ? $"Retrieved {iterationDocs.Count} documents. Key content: {SummarizeDocuments(iterationDocs)}"
                    : "No new relevant documents found.";
            }
            else if (action.Equals("conclude", StringComparison.OrdinalIgnoreCase))
            {
                observation = "Concluding search with current results.";
                finalAnswer = actionInput;
                isComplete = true;
            }
            else
            {
                observation = $"Unknown action: {action}";
            }

            // Step 3: Assess confidence
            confidence = await AssessConfidenceAsync(query, allDocs, iterations, options, cancellationToken);

            var iteration = new ReasoningIteration
            {
                IterationNumber = i + 1,
                Thought = thought,
                Action = action,
                ActionInput = actionInput,
                RetrievedDocs = iterationDocs,
                Observation = observation,
                Confidence = confidence
            };
            iterations.Add(iteration);

            // Check stopping conditions
            if (confidence >= options.ConfidenceThreshold)
            {
                isComplete = true;
                stopReason = "Confidence threshold reached";
            }
            else if (allDocs.Count >= options.MaxTotalDocs)
            {
                stopReason = "Maximum documents reached";
            }
            else if (action.Equals("conclude", StringComparison.OrdinalIgnoreCase))
            {
                stopReason = "Model concluded";
            }

            // Update query for next iteration
            currentQuery = actionInput;
        }

        if (!isComplete && stopReason == null)
        {
            stopReason = "Maximum iterations reached";
        }

        // Generate final answer if not already done
        if (finalAnswer == null && _llmService != null && allDocs.Count != 0)
        {
            finalAnswer = await GenerateFinalAnswerAsync(query, allDocs, cancellationToken);
        }

        stopwatch.Stop();

        return new IterativeRetrievalResult
        {
            Documents = allDocs,
            Iterations = iterations,
            FinalAnswer = finalAnswer,
            Confidence = confidence,
            IsComplete = isComplete,
            StopReason = stopReason,
            ProcessingTime = stopwatch.Elapsed,
            Stats = new IterativeRetrievalStats
            {
                TotalIterations = iterations.Count,
                TotalDocuments = allDocs.Count,
                UniqueDocuments = seenDocIds.Count,
                LlmCalls = _llmService != null ? iterations.Count * 2 : 0,
                RetrievalCalls = iterations.Count(iter => iter.Action.Equals("retrieve", StringComparison.OrdinalIgnoreCase)),
                AvgDocsPerIteration = iterations.Count > 0 ? (float)allDocs.Count / iterations.Count : 0
            }
        };
    }

    private async Task<(string thought, string action, string actionInput)> ReasonNextStepAsync(
        string originalQuery,
        string currentQuery,
        List<ReasoningIteration> previousIterations,
        List<IterativeSearchResult> currentDocs,
        IterativeRetrievalOptions options,
        CancellationToken cancellationToken)
    {
        if (_llmService == null || !options.UseLlmReasoning)
        {
            // Fallback: simple retrieval
            return ("Performing search without LLM reasoning", "retrieve", currentQuery);
        }

        var context = BuildReasoningContext(originalQuery, previousIterations, currentDocs);

        var prompt = $"""
            You are a search assistant helping to answer the question: "{originalQuery}"

            {context}

            Based on the search history and current results, decide your next action.

            Think step by step:
            1. What information do we have?
            2. What information do we still need?
            3. Should we search for more information or conclude?

            Respond in this exact format:
            THOUGHT: [Your reasoning about what to do next]
            ACTION: [Either "retrieve" or "conclude"]
            ACTION_INPUT: [If retrieve: the search query to use. If conclude: your final answer]
            """;

        try
        {
            var response = await _llmService.GenerateCompletionAsync(
                prompt,
                maxTokens: 500,
                temperature: options.ReasoningTemperature,
                cancellationToken: cancellationToken);

            return ParseReasoningResponse(response);
        }
        catch (Exception ex)
        {
            LogIterativeRetrieval6(_logger, ex);
            return ("LLM reasoning failed, continuing with search", "retrieve", currentQuery);
        }
    }

    private static (string thought, string action, string actionInput) ParseReasoningResponse(string response)
    {
        var thought = "";
        var action = "retrieve";
        var actionInput = "";

        var lines = response.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("THOUGHT:", StringComparison.OrdinalIgnoreCase))
            {
                thought = trimmed.Substring("THOUGHT:".Length).Trim();
            }
            else if (trimmed.StartsWith("ACTION:", StringComparison.OrdinalIgnoreCase))
            {
                action = trimmed.Substring("ACTION:".Length).Trim().ToLowerInvariant();
            }
            else if (trimmed.StartsWith("ACTION_INPUT:", StringComparison.OrdinalIgnoreCase))
            {
                actionInput = trimmed.Substring("ACTION_INPUT:".Length).Trim();
            }
        }

        return (thought, action, actionInput);
    }

    private static string BuildReasoningContext(string query, List<ReasoningIteration> iterations, List<IterativeSearchResult> docs)
    {
        var sb = new StringBuilder();

        if (iterations.Count != 0)
        {
            sb.AppendLine("Previous iterations:");
            foreach (var iter in iterations.TakeLast(3))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- Iteration {iter.IterationNumber}:");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Thought: {iter.Thought}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Action: {iter.Action}({iter.ActionInput})");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Observation: {iter.Observation}");
            }
            sb.AppendLine();
        }

        if (docs.Count != 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Current documents ({docs.Count} total):");
            foreach (var doc in docs.Take(5))
            {
                var snippet = doc.Content.Length > 200 ? string.Concat(doc.Content.AsSpan(0, 200), "...") : doc.Content;
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {snippet}");
            }
        }
        else
        {
            sb.AppendLine("No documents retrieved yet.");
        }

        return sb.ToString();
    }

    private async Task<float> AssessConfidenceAsync(
        string query,
        List<IterativeSearchResult> docs,
        List<ReasoningIteration> iterations,
        IterativeRetrievalOptions options,
        CancellationToken cancellationToken)
    {
        if (_llmService == null || docs.Count == 0)
        {
            return docs.Count != 0 ? 0.5f : 0.1f;
        }

        // Simple heuristic-based confidence
        var docQuality = docs.Average(d => d.Score);
        var docCount = Math.Min(1.0f, docs.Count / 10.0f);
        var iterationProgress = Math.Min(1.0f, iterations.Count / (float)options.MaxIterations);

        return (float)((docQuality * 0.5) + (docCount * 0.3) + (iterationProgress * 0.2));
    }

    private async Task<string> GenerateFinalAnswerAsync(
        string query,
        List<IterativeSearchResult> docs,
        CancellationToken cancellationToken)
    {
        if (_llmService == null)
        {
            return SummarizeDocuments(docs.Take(5).ToList());
        }

        var context = string.Join("\n\n", docs.Take(5).Select(d => d.Content));

        var prompt = $"""
            Based on the following documents, answer the question: "{query}"

            Documents:
            {context}

            Provide a concise, accurate answer based only on the provided documents.
            """;

        try
        {
            var answer = await _llmService.GenerateCompletionAsync(
                prompt,
                maxTokens: 500,
                temperature: 0.3f,
                cancellationToken: cancellationToken);

            return answer;
        }
        catch (Exception ex)
        {
            LogIterativeRetrieval5(_logger, ex);
            return SummarizeDocuments(docs.Take(5).ToList());
        }
    }

    private static string SummarizeDocuments(List<IterativeSearchResult> docs)
    {
        if (docs.Count == 0) return "No documents";
        return string.Join("; ", docs.Take(3).Select(d =>
            d.Content.Length > 100 ? string.Concat(d.Content.AsSpan(0, 100), "...") : d.Content));
    }

    #endregion

    #region Query Decomposition (Self-Ask Pattern)

    /// <inheritdoc />
    public async Task<IterativeDecompositionResult> DecomposeAndRetrieveAsync(
        string query,
        QueryDecompositionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new QueryDecompositionOptions();
        var stopwatch = Stopwatch.StartNew();

        if (_logger.IsEnabled(LogLevel.Information))
            LogIterativeRetrieval4(_logger, query);

        // Step 1: Decompose query into sub-questions
        var subQuestions = await DecomposeQueryAsync(query, options, cancellationToken);

        // Step 2: Retrieve and answer each sub-question
        var allDocs = new List<IterativeSearchResult>();
        var answeredQuestions = new List<SubQuestion>();

        foreach (var subQ in subQuestions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var docs = new List<IterativeSearchResult>();
            string? answer = null;
            float confidence = 0;

            if (options.RetrievePerSubQuestion)
            {
                var searchResults = await _searchService.SearchAsync(
                    subQ.Question,
                    new HybridSearchOptions { MaxResults = options.MaxDocsPerSubQuestion },
                    cancellationToken);

                docs = searchResults.Select(IterativeSearchResult.FromHybridResult).ToList();
                allDocs.AddRange(docs);

                if (options.SynthesizeSubAnswers && _llmService != null && docs.Count != 0)
                {
                    (answer, confidence) = await AnswerSubQuestionAsync(
                        subQ.Question, docs, cancellationToken);
                }
            }

            answeredQuestions.Add(new SubQuestion
            {
                Question = subQ.Question,
                Dependencies = subQ.Dependencies,
                Documents = docs,
                Answer = answer,
                Confidence = confidence
            });
        }

        // Step 3: Compose final answer
        string? composedAnswer = null;
        float finalConfidence = 0;

        if (options.ComposeFinalAnswer && _llmService != null)
        {
            (composedAnswer, finalConfidence) = await ComposeFinalAnswerAsync(
                query, answeredQuestions, cancellationToken);
        }

        stopwatch.Stop();

        return new IterativeDecompositionResult
        {
            OriginalQuery = query,
            SubQuestions = answeredQuestions,
            ComposedAnswer = composedAnswer,
            AllDocuments = allDocs.GroupBy(d => d.ChunkId).Select(g => g.First()).ToList(),
            Confidence = finalConfidence,
            ProcessingTime = stopwatch.Elapsed
        };
    }

    private async Task<List<SubQuestion>> DecomposeQueryAsync(
        string query,
        QueryDecompositionOptions options,
        CancellationToken cancellationToken)
    {
        if (_llmService == null)
        {
            // Fallback: single question
            return new List<SubQuestion>
            {
                new SubQuestion { Question = query, Dependencies = Array.Empty<int>() }
            };
        }

        var prompt = $"""
            Decompose the following complex question into simpler sub-questions that can be answered independently.
            Each sub-question should be self-contained and answerable with a simple search.

            Question: "{query}"

            Generate {options.MaxSubQuestions} or fewer sub-questions.
            Format each as: SUBQ[n]: [question]

            If a sub-question depends on another, add: DEPENDS: [comma-separated indices]

            Example:
            SUBQ[1]: What is X?
            SUBQ[2]: How does Y relate to X?
            DEPENDS: 1
            """;

        try
        {
            var response = await _llmService.GenerateCompletionAsync(
                prompt,
                maxTokens: 500,
                temperature: 0.3f,
                cancellationToken: cancellationToken);

            return ParseSubQuestions(response);
        }
        catch (Exception ex)
        {
            LogIterativeRetrieval3(_logger, ex);
            return new List<SubQuestion>
            {
                new SubQuestion { Question = query, Dependencies = Array.Empty<int>() }
            };
        }
    }

    private static List<SubQuestion> ParseSubQuestions(string response)
    {
        var questions = new List<SubQuestion>();
        var lines = response.Split('\n');
        var currentDeps = new List<int>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("SUBQ[", StringComparison.OrdinalIgnoreCase))
            {
                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx > 0)
                {
                    var question = trimmed.Substring(colonIdx + 1).Trim();
                    questions.Add(new SubQuestion
                    {
                        Question = question,
                        Dependencies = currentDeps.ToArray()
                    });
                    currentDeps.Clear();
                }
            }
            else if (trimmed.StartsWith("DEPENDS:", StringComparison.OrdinalIgnoreCase))
            {
                var depsStr = trimmed.Substring("DEPENDS:".Length).Trim();
                currentDeps = depsStr.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => int.TryParse(s, out _))
                    .Select(int.Parse)
                    .ToList();
            }
        }

        return questions.Count != 0 ? questions : new List<SubQuestion>
        {
            new SubQuestion { Question = response.Trim(), Dependencies = Array.Empty<int>() }
        };
    }

    private async Task<(string answer, float confidence)> AnswerSubQuestionAsync(
        string question,
        List<IterativeSearchResult> docs,
        CancellationToken cancellationToken)
    {
        var context = string.Join("\n", docs.Take(3).Select(d => d.Content));

        var prompt = $"""
            Answer this question based on the provided context:

            Question: {question}

            Context:
            {context}

            Provide a brief, factual answer. If the context doesn't contain the answer, say "Not found in context."
            """;

        try
        {
            var answer = await _llmService!.GenerateCompletionAsync(
                prompt,
                maxTokens: 200,
                temperature: 0.2f,
                cancellationToken: cancellationToken);

            var confidence = answer.Contains("Not found", StringComparison.OrdinalIgnoreCase) ? 0.2f : 0.8f;
            return (answer, confidence);
        }
        catch
        {
            return ("Unable to answer", 0.0f);
        }
    }

    private async Task<(string answer, float confidence)> ComposeFinalAnswerAsync(
        string originalQuery,
        List<SubQuestion> subQuestions,
        CancellationToken cancellationToken)
    {
        var subAnswers = string.Join("\n", subQuestions
            .Where(q => !string.IsNullOrEmpty(q.Answer))
            .Select(q => $"Q: {q.Question}\nA: {q.Answer}"));

        var prompt = $"""
            Based on the following sub-questions and their answers, compose a comprehensive answer to the original question.

            Original Question: {originalQuery}

            Sub-questions and Answers:
            {subAnswers}

            Compose a final, coherent answer that synthesizes all the information.
            """;

        try
        {
            var answer = await _llmService!.GenerateCompletionAsync(
                prompt,
                maxTokens: 500,
                temperature: 0.3f,
                cancellationToken: cancellationToken);

            var answeredQuestions = subQuestions.Where(q => q.Confidence > 0).ToList();
            var avgConfidence = answeredQuestions.Count != 0 ? answeredQuestions.Average(q => q.Confidence) : 0f;
            return (answer, (float)avgConfidence);
        }
        catch
        {
            return ("Unable to compose answer", 0.0f);
        }
    }

    #endregion

    #region Multi-Hop Retrieval

    /// <inheritdoc />
    public async Task<MultiHopRetrievalResult> MultiHopRetrieveAsync(
        string query,
        MultiHopOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MultiHopOptions();
        var stopwatch = Stopwatch.StartNew();

        if (_logger.IsEnabled(LogLevel.Information))
            LogIterativeRetrieval2(_logger, query);

        var hops = new List<RetrievalHop>();
        var allDocs = new List<IterativeSearchResult>();
        var currentEntities = new List<ExtractedEntity>();
        var currentQuery = query;
        string? answer = null;
        var reasoningPath = new StringBuilder();

        // Initial hop
        var initialResults = await _searchService.SearchAsync(
            query,
            new HybridSearchOptions { MaxResults = options.MaxDocsPerHop },
            cancellationToken);

        var initialDocs = initialResults.Select(IterativeSearchResult.FromHybridResult).ToList();
        allDocs.AddRange(initialDocs);

        // Extract entities from initial results
        if (_entityService != null && initialDocs.Count != 0)
        {
            var combinedContent = string.Join(" ", initialDocs.Select(d => d.Content));
            var graph = await _entityService.ExtractEntityGraphAsync(combinedContent, cancellationToken: cancellationToken);
            if (graph?.Entities != null)
            {
                currentEntities = FilterEntities(graph.Entities.ToList(), options);
            }
        }

        hops.Add(new RetrievalHop
        {
            HopNumber = 0,
            Query = query,
            TriggerEntities = Array.Empty<ExtractedEntity>(),
            Documents = initialDocs,
            ExtractedEntities = currentEntities,
            Reasoning = "Initial retrieval based on user query"
        });

        reasoningPath.AppendLine(CultureInfo.InvariantCulture, $"Hop 0: Searched for '{query}', found {initialDocs.Count} documents");

        // Follow-up hops based on entities
        for (int hop = 1; hop <= options.MaxHops && currentEntities.Count != 0; hop++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entitiesToFollow = currentEntities
                .OrderByDescending(e => e.Confidence)
                .Take(options.MaxEntitiesPerHop)
                .ToList();

            if (entitiesToFollow.Count == 0)
                break;

            var hopDocs = new List<IterativeSearchResult>();
            var hopEntities = new List<ExtractedEntity>();

            foreach (var entity in entitiesToFollow)
            {
                var entityQuery = $"{entity.Text} {query}";
                var results = await _searchService.SearchAsync(
                    entityQuery,
                    new HybridSearchOptions { MaxResults = options.MaxDocsPerHop / entitiesToFollow.Count },
                    cancellationToken);

                var entityDocs = results.Select(IterativeSearchResult.FromHybridResult).ToList();
                hopDocs.AddRange(entityDocs);

                // Extract more entities
                if (_entityService != null && entityDocs.Count != 0)
                {
                    var content = string.Join(" ", entityDocs.Select(d => d.Content));
                    var graph = await _entityService.ExtractEntityGraphAsync(content, cancellationToken: cancellationToken);
                    hopEntities.AddRange(graph.Entities);
                }
            }

            // Deduplicate
            hopDocs = hopDocs.GroupBy(d => d.ChunkId).Select(g => g.First()).ToList();
            hopEntities = FilterEntities(hopEntities, options);

            hops.Add(new RetrievalHop
            {
                HopNumber = hop,
                Query = string.Join(", ", entitiesToFollow.Select(e => e.Text)),
                TriggerEntities = entitiesToFollow,
                Documents = hopDocs,
                ExtractedEntities = hopEntities,
                Reasoning = $"Following entities: {string.Join(", ", entitiesToFollow.Select(e => e.Text))}"
            });

            allDocs.AddRange(hopDocs);
            currentEntities = hopEntities.Except(entitiesToFollow).ToList();

            reasoningPath.AppendLine(CultureInfo.InvariantCulture, $"Hop {hop}: Followed {entitiesToFollow.Count} entities, found {hopDocs.Count} documents");

            // Check if we found an answer
            if (options.StopOnAnswerFound && _llmService != null)
            {
                var hasAnswer = await CheckForAnswerAsync(query, allDocs, cancellationToken);
                if (hasAnswer)
                {
                    answer = await GenerateFinalAnswerAsync(query, allDocs, cancellationToken);
                    reasoningPath.AppendLine(CultureInfo.InvariantCulture, $"Answer found at hop {hop}");
                    break;
                }
            }
        }

        // Build entity graph if tracking relationships
        EntityGraph? discoveredGraph = null;
        if (options.TrackRelationships && _entityService != null && allDocs.Count != 0)
        {
            var allContent = string.Join(" ", allDocs.Select(d => d.Content));
            discoveredGraph = await _entityService.ExtractEntityGraphAsync(allContent, cancellationToken: cancellationToken);
        }

        stopwatch.Stop();

        return new MultiHopRetrievalResult
        {
            Hops = hops,
            FinalDocuments = allDocs.GroupBy(d => d.ChunkId).Select(g => g.First()).ToList(),
            DiscoveredGraph = discoveredGraph,
            Answer = answer,
            ReasoningPath = reasoningPath.ToString(),
            ProcessingTime = stopwatch.Elapsed
        };
    }

    private static List<ExtractedEntity> FilterEntities(List<ExtractedEntity> entities, MultiHopOptions options)
    {
        var filtered = entities.Where(e => e.Confidence >= options.MinEntityConfidence);

        if (options.EntityTypesToFollow != null && options.EntityTypesToFollow.Count != 0)
        {
            filtered = filtered.Where(e => options.EntityTypesToFollow.Contains(e.Type));
        }

        return filtered.ToList();
    }

    private async Task<bool> CheckForAnswerAsync(
        string query,
        List<IterativeSearchResult> docs,
        CancellationToken cancellationToken)
    {
        if (_llmService == null || docs.Count == 0)
            return false;

        var context = string.Join("\n", docs.Take(5).Select(d => d.Content.Substring(0, Math.Min(200, d.Content.Length))));

        var prompt = $"""
            Can the following question be fully answered from this context?

            Question: {query}

            Context (excerpts):
            {context}

            Answer only "YES" or "NO".
            """;

        try
        {
            var response = await _llmService.GenerateCompletionAsync(
                prompt,
                maxTokens: 10,
                temperature: 0.0f,
                cancellationToken: cancellationToken);

            return response.Trim().Contains("YES", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Agentic Retrieval

    /// <inheritdoc />
    public async Task<AgenticRetrievalResult> AgenticRetrieveAsync(
        string query,
        AgenticRetrievalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AgenticRetrievalOptions();
        var stopwatch = Stopwatch.StartNew();

        if (_logger.IsEnabled(LogLevel.Information))
            LogIterativeRetrieval1(_logger, query);

        var availableTools = options.AvailableTools?.ToList() ?? new List<RetrievalTool>
        {
            RetrievalTool.HybridSearch,
            RetrievalTool.QueryReformulation,
            RetrievalTool.Reranking
        };

        var actions = new List<AgentAction>();
        var allDocs = new List<IterativeSearchResult>();
        var goalAchieved = false;
        string? finalAnswer = null;
        string? reflection = null;

        for (int i = 0; i < options.MaxIterations && !goalAchieved; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Step 1: Plan next action
            var (tool, thought, input) = await PlanNextActionAsync(
                query, actions, allDocs, availableTools, options, cancellationToken);

            // Step 2: Execute action
            var (docs, observation, success) = await ExecuteToolAsync(
                tool, input, options, cancellationToken);

            allDocs.AddRange(docs);

            var action = new AgentAction
            {
                ActionNumber = i + 1,
                Tool = tool,
                Thought = thought,
                Input = input,
                Observation = observation,
                Documents = docs,
                Success = success
            };
            actions.Add(action);

            // Step 3: Reflect and check goal
            if (options.EnableReflection && _llmService != null)
            {
                (goalAchieved, reflection) = await ReflectOnProgressAsync(
                    query, actions, allDocs, options, cancellationToken);
            }

            // Check document limit
            if (allDocs.Count >= options.MaxTotalDocs)
            {
                reflection = "Maximum document limit reached";
                break;
            }
        }

        // Generate final answer
        if (_llmService != null && allDocs.Count != 0)
        {
            finalAnswer = await GenerateFinalAnswerAsync(query, allDocs, cancellationToken);
        }

        stopwatch.Stop();

        return new AgenticRetrievalResult
        {
            ExecutionTrace = actions,
            Documents = allDocs.GroupBy(d => d.ChunkId).Select(g => g.First()).ToList(),
            FinalAnswer = finalAnswer,
            GoalAchieved = goalAchieved,
            Reflection = reflection,
            ProcessingTime = stopwatch.Elapsed
        };
    }

    private async Task<(RetrievalTool tool, string thought, Dictionary<string, object> input)> PlanNextActionAsync(
        string query,
        List<AgentAction> previousActions,
        List<IterativeSearchResult> currentDocs,
        List<RetrievalTool> availableTools,
        AgenticRetrievalOptions options,
        CancellationToken cancellationToken)
    {
        if (_llmService == null)
        {
            // Simple heuristic planning
            if (previousActions.Count == 0)
            {
                return (RetrievalTool.HybridSearch, "Starting with hybrid search", new Dictionary<string, object> { ["query"] = query });
            }
            return (RetrievalTool.Reranking, "Reranking results", new Dictionary<string, object> { ["query"] = query });
        }

        var toolList = string.Join(", ", availableTools);
        var history = string.Join("\n", previousActions.TakeLast(3).Select(a =>
            $"- {a.Tool}: {a.Observation}"));

        var prompt = $"""
            You are a retrieval agent trying to answer: "{query}"

            Available tools: {toolList}

            Previous actions:
            {history}

            Current documents: {currentDocs.Count}

            Plan your next action.

            THOUGHT: [Your reasoning]
            TOOL: [Tool name from the list]
            INPUT: [Parameters as JSON]
            """;

        try
        {
            var response = await _llmService.GenerateCompletionAsync(
                prompt,
                maxTokens: 300,
                temperature: 0.3f,
                cancellationToken: cancellationToken);

            return ParseAgentPlan(response, availableTools, query);
        }
        catch
        {
            return (RetrievalTool.HybridSearch, "Fallback to search", new Dictionary<string, object> { ["query"] = query });
        }
    }

    private static (RetrievalTool tool, string thought, Dictionary<string, object> input) ParseAgentPlan(
        string response,
        List<RetrievalTool> availableTools,
        string defaultQuery)
    {
        var thought = "";
        var toolName = "HybridSearch";
        var input = new Dictionary<string, object> { ["query"] = defaultQuery };

        foreach (var line in response.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("THOUGHT:", StringComparison.OrdinalIgnoreCase))
            {
                thought = trimmed.Substring("THOUGHT:".Length).Trim();
            }
            else if (trimmed.StartsWith("TOOL:", StringComparison.OrdinalIgnoreCase))
            {
                toolName = trimmed.Substring("TOOL:".Length).Trim();
            }
            else if (trimmed.StartsWith("INPUT:", StringComparison.OrdinalIgnoreCase))
            {
                var inputStr = trimmed.Substring("INPUT:".Length).Trim();
                try
                {
                    input = JsonSerializer.Deserialize<Dictionary<string, object>>(inputStr) ?? input;
                }
                catch (JsonException ex)
                {
                    Trace.TraceInformation($"[IterativeRetrieval] Failed to parse tool input JSON: {ex.Message}");
                }
            }
        }

        if (!Enum.TryParse<RetrievalTool>(toolName, true, out var tool))
        {
            tool = RetrievalTool.HybridSearch;
        }

        if (!availableTools.Contains(tool))
        {
            tool = availableTools.First();
        }

        return (tool, thought, input);
    }

    private async Task<(List<IterativeSearchResult> docs, string observation, bool success)> ExecuteToolAsync(
        RetrievalTool tool,
        Dictionary<string, object> input,
        AgenticRetrievalOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = input.GetValueOrDefault("query")?.ToString() ?? "";

            switch (tool)
            {
                case RetrievalTool.VectorSearch:
                case RetrievalTool.KeywordSearch:
                case RetrievalTool.HybridSearch:
                    var results = await _searchService.SearchAsync(
                        query,
                        new HybridSearchOptions { MaxResults = 5 },
                        cancellationToken);
                    var docs = results.Select(IterativeSearchResult.FromHybridResult).ToList();
                    return (docs, $"Retrieved {docs.Count} documents", docs.Count != 0);

                case RetrievalTool.QueryReformulation:
                    var reformulated = await ReformulateQueryAsync(query, cancellationToken);
                    return (new List<IterativeSearchResult>(), $"Query reformulated to: {reformulated}", true);

                default:
                    return (new List<IterativeSearchResult>(), $"Tool {tool} not fully implemented", false);
            }
        }
        catch (Exception ex)
        {
            return (new List<IterativeSearchResult>(), $"Error: {ex.Message}", false);
        }
    }

    private async Task<string> ReformulateQueryAsync(string query, CancellationToken cancellationToken)
    {
        if (_llmService == null) return query;

        var prompt = $"Reformulate this search query to be more specific and effective: \"{query}\"\nReformulated:";

        try
        {
            var response = await _llmService.GenerateCompletionAsync(
                prompt,
                maxTokens: 100,
                temperature: 0.3f,
                cancellationToken: cancellationToken);
            return response.Trim();
        }
        catch
        {
            return query;
        }
    }

    private async Task<(bool goalAchieved, string reflection)> ReflectOnProgressAsync(
        string query,
        List<AgentAction> actions,
        List<IterativeSearchResult> docs,
        AgenticRetrievalOptions options,
        CancellationToken cancellationToken)
    {
        if (_llmService == null)
        {
            return (docs.Count >= 5, $"Collected {docs.Count} documents");
        }

        var actionSummary = string.Join("\n", actions.Select(a => $"- {a.Tool}: {a.Success}"));

        var prompt = $"""
            Reflect on progress toward answering: "{query}"

            Actions taken:
            {actionSummary}

            Documents collected: {docs.Count}

            {(options.SuccessCriteria != null ? $"Success criteria: {options.SuccessCriteria}" : "")}

            Is the goal achieved? Reply in format:
            ACHIEVED: YES/NO
            REFLECTION: [Brief reflection]
            """;

        try
        {
            var response = await _llmService.GenerateCompletionAsync(
                prompt,
                maxTokens: 150,
                temperature: 0.2f,
                cancellationToken: cancellationToken);

            var achieved = response.Contains("ACHIEVED: YES", StringComparison.OrdinalIgnoreCase);
            var reflectionMatch = response.IndexOf("REFLECTION:", StringComparison.OrdinalIgnoreCase);
            var reflectionText = reflectionMatch >= 0 ? response.Substring(reflectionMatch + 11).Trim() : response;

            return (achieved, reflectionText);
        }
        catch
        {
            return (false, "Reflection failed");
        }
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting iterative retrieval for query: {Query}")]
    private static partial void LogIterativeRetrieval8(ILogger logger, string query);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Iteration {Iteration}: query = {Query}")]
    private static partial void LogIterativeRetrieval7(ILogger logger, int iteration, string query);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to get LLM reasoning, using fallback")]
    private static partial void LogIterativeRetrieval6(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to generate final answer")]
    private static partial void LogIterativeRetrieval5(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Information, Message = "Decomposing query: {Query}")]
    private static partial void LogIterativeRetrieval4(ILogger logger, string query);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to decompose query")]
    private static partial void LogIterativeRetrieval3(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Information, Message = "Starting multi-hop retrieval for: {Query}")]
    private static partial void LogIterativeRetrieval2(ILogger logger, string query);
    [LoggerMessage(Level = LogLevel.Information, Message = "Starting agentic retrieval for: {Query}")]
    private static partial void LogIterativeRetrieval1(ILogger logger, string query);

    #endregion
}
