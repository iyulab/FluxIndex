using ModelContextProtocol.Server;
using System.ComponentModel;

namespace FluxIndex.Stack.Api.Mcp;

/// <summary>
/// MCP Server Prompts for FluxIndex RAG operations.
/// Provides reusable prompt templates for common RAG tasks.
/// </summary>
[McpServerPromptType]
public static class FluxIndexPrompts
{
    /// <summary>
    /// Generate a RAG prompt for question answering using retrieved context.
    /// </summary>
    [McpServerPrompt]
    [Description("Generate a RAG prompt for answering questions using retrieved context from the knowledge base.")]
    public static string RagAnswer(
        [Description("The user's question to answer")] string question,
        [Description("The retrieved context from knowledge base search")] string context,
        [Description("Optional system instructions for the LLM")] string? systemInstructions = null)
    {
        var instructions = systemInstructions ??
            "You are a helpful assistant. Answer the question based ONLY on the provided context. " +
            "If the context doesn't contain enough information to answer the question, say so clearly.";

        return $"""
            {instructions}

            ## Context
            {context}

            ## Question
            {question}

            ## Instructions
            - Base your answer ONLY on the provided context
            - Cite specific parts of the context when relevant
            - If the context is insufficient, explain what information is missing
            - Be concise but thorough

            ## Answer
            """;
    }

    /// <summary>
    /// Generate a summarization prompt for document content.
    /// </summary>
    [McpServerPrompt]
    [Description("Generate a prompt for summarizing document content with customizable detail level.")]
    public static string Summarize(
        [Description("The document content to summarize")] string content,
        [Description("Summary style: 'brief', 'detailed', or 'bullet'")] string style = "detailed",
        [Description("Maximum length in words (approximate)")] int maxLength = 200)
    {
        var styleInstruction = style.ToLowerInvariant() switch
        {
            "brief" => "Create a very concise summary in 1-2 sentences.",
            "bullet" => "Create a bullet-point summary highlighting key points.",
            _ => "Create a comprehensive summary covering all main points."
        };

        return $"""
            ## Task
            Summarize the following content.

            ## Instructions
            - {styleInstruction}
            - Target length: approximately {maxLength} words
            - Preserve key facts and important details
            - Maintain the original tone and intent

            ## Content
            {content}

            ## Summary
            """;
    }

    /// <summary>
    /// Generate a query expansion prompt for improved search.
    /// </summary>
    [McpServerPrompt]
    [Description("Generate alternative search queries to improve retrieval coverage.")]
    public static string ExpandQuery(
        [Description("The original search query")] string query,
        [Description("Number of alternative queries to generate")] int count = 3)
    {
        return $"""
            ## Task
            Generate {count} alternative search queries that could help find relevant information for the original query.

            ## Original Query
            {query}

            ## Instructions
            - Create semantically similar but differently worded queries
            - Include both more specific and more general variations
            - Consider different terminology that might be used for the same concepts
            - Output each query on a separate line
            - Do not number the queries

            ## Alternative Queries
            """;
    }

    /// <summary>
    /// Generate a hypothetical document prompt for HyDE search.
    /// </summary>
    [McpServerPrompt]
    [Description("Generate a hypothetical document that would answer the query (for HyDE search technique).")]
    public static string HyDE(
        [Description("The search query to generate a hypothetical answer for")] string query,
        [Description("The domain or topic area")] string? domain = null)
    {
        var domainContext = string.IsNullOrWhiteSpace(domain)
            ? ""
            : $"This is in the context of {domain}. ";

        return $"""
            ## Task
            Write a hypothetical paragraph that would perfectly answer the following question.
            {domainContext}

            ## Question
            {query}

            ## Instructions
            - Write as if you are creating an excerpt from a document that contains the answer
            - Be factual and informative in tone
            - Include specific details that would be relevant to the question
            - Length: 100-200 words
            - Do not preface with "Here is..." or similar phrases

            ## Hypothetical Document
            """;
    }

    /// <summary>
    /// Generate a content extraction prompt for structured data.
    /// </summary>
    [McpServerPrompt]
    [Description("Extract structured information from unstructured text content.")]
    public static string ExtractEntities(
        [Description("The content to extract entities from")] string content,
        [Description("Comma-separated list of entity types to extract")] string entityTypes = "person,organization,date,location")
    {
        var types = entityTypes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var typeList = string.Join(", ", types);

        return $"""
            ## Task
            Extract the following entity types from the provided content: {typeList}

            ## Content
            {content}

            ## Instructions
            - Extract all instances of the specified entity types
            - For each entity, provide the exact text as it appears in the content
            - Group entities by type
            - If no entities of a type are found, indicate "None found"
            - Format the output as a structured list

            ## Extracted Entities
            """;
    }

    /// <summary>
    /// Generate a fact-checking prompt.
    /// </summary>
    [McpServerPrompt]
    [Description("Verify claims in a statement against provided context.")]
    public static string FactCheck(
        [Description("The statement containing claims to verify")] string statement,
        [Description("The context/evidence to check against")] string context)
    {
        return $"""
            ## Task
            Verify the claims in the following statement against the provided context.

            ## Statement to Verify
            {statement}

            ## Available Context
            {context}

            ## Instructions
            - Identify each distinct claim in the statement
            - For each claim, determine if it is:
              - SUPPORTED: The context confirms this claim
              - CONTRADICTED: The context directly contradicts this claim
              - UNVERIFIABLE: The context doesn't contain relevant information
            - Provide specific quotes from the context as evidence
            - Give an overall assessment

            ## Verification Results
            """;
    }

    /// <summary>
    /// Generate a comparative analysis prompt.
    /// </summary>
    [McpServerPrompt]
    [Description("Compare and contrast information from multiple sources or documents.")]
    public static string Compare(
        [Description("First content to compare")] string content1,
        [Description("Second content to compare")] string content2,
        [Description("Aspects to focus the comparison on (comma-separated)")] string? aspects = null)
    {
        var aspectGuide = string.IsNullOrWhiteSpace(aspects)
            ? "Compare across all relevant dimensions."
            : $"Focus specifically on these aspects: {aspects}";

        return $"""
            ## Task
            Provide a comparative analysis of the two pieces of content below.

            ## Content A
            {content1}

            ## Content B
            {content2}

            ## Instructions
            - {aspectGuide}
            - Identify key similarities and differences
            - Note any contradictions or conflicts
            - Highlight unique information in each source
            - Provide a balanced assessment

            ## Comparative Analysis
            """;
    }
}
