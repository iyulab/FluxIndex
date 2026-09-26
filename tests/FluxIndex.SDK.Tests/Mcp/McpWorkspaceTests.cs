using System.Text.Json;
using AwesomeAssertions;
using FluxIndex.MCP.Tools;
using FluxIndex.MCP.Workspace;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.SDK.Tests.Mcp;

/// <summary>
/// Until 0.55.0 the MCP workspace config declared an OpenAI embedding model, search defaults and a completion section
/// that nothing read. The workspace always ran the local LMSupply <c>default</c> model, and the <c>search</c> tool ran
/// the vector leg for every <c>strategy</c>. None of these facts load a model: the embedder is registered lazily, and
/// every path here stops before a search reaches it.
/// </summary>
public sealed class McpWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fi-mcp-" + Guid.NewGuid().ToString("N")[..8]);

    public McpWorkspaceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Initialize_WritesTheModelThatRuns()
    {
        using var ws = FluxIndexWorkspace.Initialize(_root);

        var saved = JsonDocument.Parse(File.ReadAllText(ws.ConfigPath)).RootElement;
        saved.GetProperty("embedding").GetProperty("provider").GetString().Should().Be("lmsupply");
        saved.GetProperty("embedding").GetProperty("model").GetString().Should().Be("default");
        saved.TryGetProperty("search", out _).Should().BeFalse("search defaults were never read");
        ws.EmbeddingModel.Should().Be("default");
    }

    [Theory]
    [InlineData("lmsupply", "bge-small-en-v1.5", "bge-small-en-v1.5")]
    [InlineData("LOCAL", "multilingual-e5-small", "multilingual-e5-small")]
    [InlineData("lmsupply", "", "default")]
    [InlineData("openai", "text-embedding-3-small", "default")]
    public void EmbeddingModel_IsTheConfiguredModelOnlyForALocalProvider(string provider, string model, string expected)
    {
        using var ws = FluxIndexWorkspace.Initialize(_root, new WorkspaceConfig { Embedding = new EmbeddingConfig { Provider = provider, Model = model } });

        ws.EmbeddingModel.Should().Be(expected);
    }

    [Fact]
    public void AConfigWrittenBefore055_LoadsAndRunsTheLocalDefault()
    {
        using (FluxIndexWorkspace.Initialize(_root)) { }
        var configPath = WorkspaceLocator.GetConfigPath(_root);
        File.WriteAllText(configPath, """
            {
              "version": "1.0",
              "embedding": { "provider": "openai", "model": "text-embedding-3-small", "dimensions": 1536 },
              "completion": { "provider": "openai", "model": "gpt-4o-mini" },
              "search": { "strategy": "Hybrid", "top_k": 10, "min_score": 0.5 }
            }
            """);

        using var ws = FluxIndexWorkspace.Open(_root);

        ws.Config.Embedding.Provider.Should().Be("openai");
        ws.EmbeddingModel.Should().Be("default", "a non-local provider has always run the local default model");
    }

    [Fact]
    public async Task Search_WithAnUnknownStrategy_ReturnsAnError_InsteadOfAVectorSearch()
    {
        await using var ws = FluxIndexWorkspace.Initialize(_root);
        var search = new SearchTool(ws, NullLogger<SearchTool>.Instance);

        var json = await search.SearchAsync("anything", 5, "semantic");

        JsonDocument.Parse(json).RootElement.GetProperty("error").GetString().Should().Contain("'semantic'");
    }

    [Fact]
    public async Task Workspace_DisposeAsync_AfterTheContextIsBuilt_Completes()
    {
        var ws = FluxIndexWorkspace.Initialize(_root);
        _ = ws.GetContext();

        await ws.DisposeAsync();
    }
}
