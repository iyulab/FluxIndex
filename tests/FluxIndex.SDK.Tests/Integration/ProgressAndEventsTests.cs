using FluxIndex.SDK;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using DocumentChunkEntity = FluxIndex.Core.Domain.Entities.DocumentChunk;
using NSubstitute;
using Xunit;

namespace FluxIndex.SDK.Tests.Integration;

/// <summary>
/// Integration tests for Phase 3: Developer Experience (DX) improvements
/// Tests progress reporting and event-based monitoring for Indexer and Retriever
/// </summary>
public class ProgressAndEventsTests
{
    #region Indexer Tests

    [Fact]
    public void Indexer_ShouldHaveIndexingStartedEvent()
    {
        // Arrange
        var (indexer, _) = CreateIndexer();

        // Act & Assert
        var eventInfo = indexer.GetType().GetEvent("IndexingStarted");
        Assert.NotNull(eventInfo);
        Assert.Equal(typeof(EventHandler<IndexingStartedEventArgs>), eventInfo.EventHandlerType);
    }

    [Fact]
    public void Indexer_ShouldHaveIndexingCompletedEvent()
    {
        // Arrange
        var (indexer, _) = CreateIndexer();

        // Act & Assert
        var eventInfo = indexer.GetType().GetEvent("IndexingCompleted");
        Assert.NotNull(eventInfo);
        Assert.Equal(typeof(EventHandler<IndexingCompletedEventArgs>), eventInfo.EventHandlerType);
    }

    [Fact]
    public void Indexer_ShouldHaveIndexingFailedEvent()
    {
        // Arrange
        var (indexer, _) = CreateIndexer();

        // Act & Assert
        var eventInfo = indexer.GetType().GetEvent("IndexingFailed");
        Assert.NotNull(eventInfo);
        Assert.Equal(typeof(EventHandler<IndexingFailedEventArgs>), eventInfo.EventHandlerType);
    }

    [Fact]
    public void Indexer_ShouldHaveBatchStartedEvent()
    {
        // Arrange
        var (indexer, _) = CreateIndexer();

        // Act & Assert
        var eventInfo = indexer.GetType().GetEvent("BatchStarted");
        Assert.NotNull(eventInfo);
        Assert.Equal(typeof(EventHandler<BatchStartedEventArgs>), eventInfo.EventHandlerType);
    }

    [Fact]
    public void Indexer_ShouldHaveBatchCompletedEvent()
    {
        // Arrange
        var (indexer, _) = CreateIndexer();

        // Act & Assert
        var eventInfo = indexer.GetType().GetEvent("BatchCompleted");
        Assert.NotNull(eventInfo);
        Assert.Equal(typeof(EventHandler<BatchCompletedEventArgs>), eventInfo.EventHandlerType);
    }

    [Fact]
    public void Indexer_IndexDocumentAsync_ShouldHaveProgressOverload()
    {
        // Arrange
        var (indexer, _) = CreateIndexer();

        // Act & Assert
        var methods = indexer.GetType().GetMethods()
            .Where(m => m.Name == "IndexDocumentAsync" &&
                        m.GetParameters().Any(p => p.ParameterType.Name.Contains("IProgress")))
            .ToList();

        Assert.NotEmpty(methods);
        var method = methods.First();
        var progressParam = method.GetParameters().First(p => p.ParameterType.Name.Contains("IProgress"));
        Assert.Contains("IndexingProgress", progressParam.ParameterType.GetGenericArguments().First().Name);
    }

    [Fact]
    public void Indexer_IndexBatchAsync_ShouldHaveProgressOverload()
    {
        // Arrange
        var (indexer, _) = CreateIndexer();

        // Act & Assert
        var methods = indexer.GetType().GetMethods()
            .Where(m => m.Name == "IndexBatchAsync" &&
                        m.GetParameters().Any(p => p.ParameterType.Name.Contains("IProgress")))
            .ToList();

        Assert.NotEmpty(methods);
        var method = methods.First();
        var progressParam = method.GetParameters().First(p => p.ParameterType.Name.Contains("IProgress"));
        Assert.Contains("BatchProgress", progressParam.ParameterType.GetGenericArguments().First().Name);
    }

    [Fact]
    public async Task Indexer_IndexDocumentAsync_WithProgress_ShouldReportProgress()
    {
        // Arrange
        var (indexer, mocks) = CreateIndexer();
        var document = CreateTestDocument();
        var progressReports = new List<IndexingProgress>();
        var progress = new Progress<IndexingProgress>(p => progressReports.Add(p));

        // Mock successful indexing
        mocks.VectorStore.StoreBatchAsync(Arg.Any<IEnumerable<DocumentChunkEntity>>(), Arg.Any<CancellationToken>()).Returns(new List<string> { "chunk1", "chunk2" });

        // Act
        await indexer.IndexDocumentAsync(document, progress);

        // Assert
        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, p => p.ProgressPercentage == 0); // Starting
        Assert.Contains(progressReports, p => p.ProgressPercentage == 100); // Completed
        Assert.All(progressReports, p => Assert.Equal(document.Id, p.DocumentId));
    }

    [Fact]
    public async Task Indexer_IndexDocumentAsync_ShouldFireIndexingStartedEvent()
    {
        // Arrange
        var (indexer, mocks) = CreateIndexer();
        var document = CreateTestDocument();
        IndexingStartedEventArgs? capturedEvent = null;
        indexer.IndexingStarted += (sender, e) => capturedEvent = e;

        // Mock successful indexing
        mocks.VectorStore.StoreBatchAsync(Arg.Any<IEnumerable<DocumentChunkEntity>>(), Arg.Any<CancellationToken>()).Returns(new List<string> { "chunk1" });

        // Act
        await indexer.IndexDocumentAsync(document);

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(document.Id, capturedEvent.DocumentId);
        Assert.True(capturedEvent.TotalChunks > 0);
    }

    [Fact]
    public async Task Indexer_IndexDocumentAsync_ShouldFireIndexingCompletedEvent()
    {
        // Arrange
        var (indexer, mocks) = CreateIndexer();
        var document = CreateTestDocument();
        IndexingCompletedEventArgs? capturedEvent = null;
        indexer.IndexingCompleted += (sender, e) => capturedEvent = e;

        // Mock successful indexing
        mocks.VectorStore.StoreBatchAsync(Arg.Any<IEnumerable<DocumentChunkEntity>>(), Arg.Any<CancellationToken>()).Returns(new List<string> { "chunk1" });

        // Act
        await indexer.IndexDocumentAsync(document);

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(document.Id, capturedEvent.DocumentId);
        Assert.True(capturedEvent.Success);
        Assert.True(capturedEvent.ChunksIndexed > 0);
    }

    #endregion

    #region Retriever Tests

    [Fact]
    public void Retriever_ShouldHaveSearchStartedEvent()
    {
        // Arrange
        var (retriever, _) = CreateRetriever();

        // Act & Assert
        var eventInfo = retriever.GetType().GetEvent("SearchStarted");
        Assert.NotNull(eventInfo);
        Assert.Equal(typeof(EventHandler<SearchStartedEventArgs>), eventInfo.EventHandlerType);
    }

    [Fact]
    public void Retriever_ShouldHaveSearchCompletedEvent()
    {
        // Arrange
        var (retriever, _) = CreateRetriever();

        // Act & Assert
        var eventInfo = retriever.GetType().GetEvent("SearchCompleted");
        Assert.NotNull(eventInfo);
        Assert.Equal(typeof(EventHandler<SearchCompletedEventArgs>), eventInfo.EventHandlerType);
    }

    [Fact]
    public void Retriever_ShouldHaveSearchFailedEvent()
    {
        // Arrange
        var (retriever, _) = CreateRetriever();

        // Act & Assert
        var eventInfo = retriever.GetType().GetEvent("SearchFailed");
        Assert.NotNull(eventInfo);
        Assert.Equal(typeof(EventHandler<SearchFailedEventArgs>), eventInfo.EventHandlerType);
    }

    [Fact]
    public void Retriever_SearchAsync_ShouldHaveProgressOverload()
    {
        // Arrange
        var (retriever, _) = CreateRetriever();

        // Act & Assert
        var methods = retriever.GetType().GetMethods()
            .Where(m => m.Name == "SearchAsync" &&
                        m.GetParameters().Any(p => p.ParameterType.Name.Contains("IProgress")))
            .ToList();

        Assert.NotEmpty(methods);
        var method = methods.First();
        var progressParam = method.GetParameters().First(p => p.ParameterType.Name.Contains("IProgress"));
        Assert.Contains("SearchProgress", progressParam.ParameterType.GetGenericArguments().First().Name);
    }

    [Fact]
    public void Retriever_HybridSearchAsync_ShouldHaveProgressOverload()
    {
        // Arrange
        var (retriever, _) = CreateRetriever();

        // Act & Assert
        var methods = retriever.GetType().GetMethods()
            .Where(m => m.Name == "HybridSearchAsync" &&
                        m.GetParameters().Any(p => p.ParameterType.Name.Contains("IProgress")))
            .ToList();

        Assert.NotEmpty(methods);
        var method = methods.First();
        var progressParam = method.GetParameters().First(p => p.ParameterType.Name.Contains("IProgress"));
        Assert.Contains("SearchProgress", progressParam.ParameterType.GetGenericArguments().First().Name);
    }

    [Fact]
    public void Retriever_KeywordSearchAsync_ShouldHaveProgressOverload()
    {
        // Arrange
        var (retriever, _) = CreateRetriever();

        // Act & Assert
        var methods = retriever.GetType().GetMethods()
            .Where(m => m.Name == "KeywordSearchAsync" &&
                        m.GetParameters().Any(p => p.ParameterType.Name.Contains("IProgress")))
            .ToList();

        Assert.NotEmpty(methods);
        var method = methods.First();
        var progressParam = method.GetParameters().First(p => p.ParameterType.Name.Contains("IProgress"));
        Assert.Contains("SearchProgress", progressParam.ParameterType.GetGenericArguments().First().Name);
    }

    [Fact]
    public async Task Retriever_SearchAsync_WithProgress_ShouldReportProgress()
    {
        // Arrange
        var (retriever, mocks) = CreateRetriever();
        var query = "test query";
        var progressReports = new List<SearchProgress>();
        var progress = new Progress<SearchProgress>(p => progressReports.Add(p));

        // Mock successful search
        mocks.EmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[] { 0.1f, 0.2f, 0.3f });
        mocks.VectorStore.SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>()).Returns(new List<DocumentChunkEntity>());

        // Act
        await retriever.SearchAsync(query, progress);

        // Assert
        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, p => p.ProgressPercentage == 0); // Starting
        Assert.Contains(progressReports, p => p.ProgressPercentage == 100); // Completed
        Assert.All(progressReports, p => Assert.Equal(query, p.Query));
    }

    [Fact]
    public async Task Retriever_SearchAsync_ShouldFireSearchStartedEvent()
    {
        // Arrange
        var (retriever, mocks) = CreateRetriever();
        var query = "test query";
        SearchStartedEventArgs? capturedEvent = null;
        retriever.SearchStarted += (sender, e) => capturedEvent = e;

        // Mock successful search
        mocks.EmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[] { 0.1f, 0.2f, 0.3f });
        mocks.VectorStore.SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>()).Returns(new List<DocumentChunkEntity>());

        // Act
        await retriever.SearchAsync(query);

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(query, capturedEvent.Query);
        Assert.Equal("Vector", capturedEvent.SearchType);
    }

    [Fact]
    public async Task Retriever_SearchAsync_ShouldFireSearchCompletedEvent()
    {
        // Arrange
        var (retriever, mocks) = CreateRetriever();
        var query = "test query";
        SearchCompletedEventArgs? capturedEvent = null;
        retriever.SearchCompleted += (sender, e) => capturedEvent = e;

        // Mock successful search
        mocks.EmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[] { 0.1f, 0.2f, 0.3f });
        mocks.VectorStore.SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>()).Returns(new List<DocumentChunkEntity>());

        // Act
        await retriever.SearchAsync(query);

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(query, capturedEvent.Query);
        Assert.Equal("Vector", capturedEvent.SearchType);
        Assert.True(capturedEvent.ProcessingTime.TotalMilliseconds >= 0);
    }

    #endregion

    #region Progress Model Tests

    [Fact]
    public void IndexingProgress_Properties_ShouldExist()
    {
        // Act
        var progress = new IndexingProgress
        {
            JobId = "job1",
            DocumentId = "doc1",
            CurrentChunk = 5,
            TotalChunks = 10,
            ProgressPercentage = 50,
            Status = "Processing",
            Message = "Indexing chunks"
        };

        // Assert
        Assert.Equal("job1", progress.JobId);
        Assert.Equal("doc1", progress.DocumentId);
        Assert.Equal(5, progress.CurrentChunk);
        Assert.Equal(10, progress.TotalChunks);
        Assert.Equal(50, progress.ProgressPercentage);
        Assert.Equal("Processing", progress.Status);
        Assert.Equal("Indexing chunks", progress.Message);
    }

    [Fact]
    public void SearchProgress_Properties_ShouldExist()
    {
        // Act
        var progress = new SearchProgress
        {
            QueryId = "query1",
            Query = "test",
            CurrentStep = 3,
            TotalSteps = 5,
            ProgressPercentage = 60,
            Status = "Searching",
            Message = "Vector search",
            ResultsFound = 10
        };

        // Assert
        Assert.Equal("query1", progress.QueryId);
        Assert.Equal("test", progress.Query);
        Assert.Equal(3, progress.CurrentStep);
        Assert.Equal(5, progress.TotalSteps);
        Assert.Equal(60, progress.ProgressPercentage);
        Assert.Equal("Searching", progress.Status);
        Assert.Equal("Vector search", progress.Message);
        Assert.Equal(10, progress.ResultsFound);
    }

    [Fact]
    public void BatchProgress_Properties_ShouldExist()
    {
        // Act
        var progress = new BatchProgress
        {
            BatchId = "batch1",
            CurrentItem = 25,
            TotalItems = 100,
            ProgressPercentage = 25,
            Status = "Processing",
            Message = "Batch indexing",
            SuccessfulItems = 20,
            FailedItems = 5
        };

        // Assert
        Assert.Equal("batch1", progress.BatchId);
        Assert.Equal(25, progress.CurrentItem);
        Assert.Equal(100, progress.TotalItems);
        Assert.Equal(25, progress.ProgressPercentage);
        Assert.Equal("Processing", progress.Status);
        Assert.Equal("Batch indexing", progress.Message);
        Assert.Equal(20, progress.SuccessfulItems);
        Assert.Equal(5, progress.FailedItems);
    }

    #endregion

    #region Helper Methods

    private (Indexer indexer, (IVectorStore VectorStore, IDocumentRepository DocumentRepository, IEmbeddingService EmbeddingService, IChunkingService ChunkingService) mocks) CreateIndexer()
    {
        var vectorStore = Substitute.For<IVectorStore>();
        var documentRepository = Substitute.For<IDocumentRepository>();
        var embeddingService = Substitute.For<IEmbeddingService>();
        var chunkingService = Substitute.For<IChunkingService>();

        documentRepository.AddAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(callInfo => callInfo.ArgAt<Document>(0).Id);

        var indexer = new Indexer(
            vectorStore,
            documentRepository,
            embeddingService,
            chunkingService,
            new IndexerOptions());

        return (indexer, (vectorStore, documentRepository, embeddingService, chunkingService));
    }

    private (Retriever retriever, (IVectorStore VectorStore, IDocumentRepository DocumentRepository, IEmbeddingService EmbeddingService, IRankFusionService RankFusion) mocks) CreateRetriever()
    {
        var vectorStore = Substitute.For<IVectorStore>();
        var documentRepository = Substitute.For<IDocumentRepository>();
        var embeddingService = Substitute.For<IEmbeddingService>();
        var rankFusion = Substitute.For<IRankFusionService>();

        var retriever = new Retriever(
            vectorStore,
            documentRepository,
            embeddingService,
            new RetrieverOptions(),
            null,
            rankFusion);

        return (retriever, (vectorStore, documentRepository, embeddingService, rankFusion));
    }

    private Document CreateTestDocument()
    {
        var document = Document.Create("test-doc-id");
        document.FileName = "test.txt";
        document.FilePath = "/test/test.txt";
        document.Content = "Test document content";
        document.SetMetadata("source", "test");

        // Add some chunks
        document.AddChunk(new DocumentChunkEntity
        {
            Id = "chunk1",
            DocumentId = document.Id,
            Content = "Test chunk 1",
            ChunkIndex = 0,
            TotalChunks = 2,
            Embedding = new float[] { 0.1f, 0.2f, 0.3f },
            Score = 0.9f,
            TokenCount = 10,
            Metadata = new Dictionary<string, object>()
        });

        document.AddChunk(new DocumentChunkEntity
        {
            Id = "chunk2",
            DocumentId = document.Id,
            Content = "Test chunk 2",
            ChunkIndex = 1,
            TotalChunks = 2,
            Embedding = new float[] { 0.4f, 0.5f, 0.6f },
            Score = 0.8f,
            TokenCount = 10,
            Metadata = new Dictionary<string, object>()
        });

        return document;
    }

    #endregion
}
