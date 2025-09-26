using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.Entities;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// SQLite-vec 통합 테스트
/// 실제 벡터 검색 시나리오와 성능 테스트 포함
/// </summary>
public class SQLiteVecIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testDatabasePath;

    public SQLiteVecIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"fluxindex_integration_{Guid.NewGuid()}.db");

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 통합 테스트용 설정 (실제 파일 기반, sqlite-vec 폴백 모드)
        services.AddSQLiteVecVectorStore(options =>
        {
            options.DatabasePath = _testDatabasePath;
            options.VectorDimension = 384; // 테스트용 작은 차원
            options.UseSQLiteVec = true; // 실제 환경에서 테스트
            options.FallbackToInMemoryOnError = true; // 확장 없으면 폴백
            options.AutoMigrate = true;
            options.MaxBatchSize = 500;
        });

        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        // 호스팅 서비스 시작 (데이터베이스 초기화)
        var hostedServices = _serviceProvider.GetServices<IHostedService>();
        foreach (var service in hostedServices)
        {
            await service.StartAsync(CancellationToken.None);
        }
    }

    public async Task DisposeAsync()
    {
        // 호스팅 서비스 정지
        var hostedServices = _serviceProvider.GetServices<IHostedService>();
        foreach (var service in hostedServices.Reverse())
        {
            await service.StopAsync(CancellationToken.None);
        }

        _serviceProvider?.Dispose();

        // 테스트 파일 정리
        try
        {
            if (File.Exists(_testDatabasePath))
            {
                File.Delete(_testDatabasePath);
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"테스트 데이터베이스 정리 실패: {ex.Message}");
        }
    }

    [Fact]
    public async Task EndToEndWorkflow_DocumentIndexingAndSearch_ShouldWorkCorrectly()
    {
        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();

        // 가상의 문서들을 시뮬레이션
        var documents = new[]
        {
            new { Id = "doc1", Title = "Machine Learning Basics", Content = "Introduction to supervised learning algorithms" },
            new { Id = "doc2", Title = "Deep Learning", Content = "Neural networks and backpropagation explained" },
            new { Id = "doc3", Title = "Natural Language Processing", Content = "Text processing and language understanding" },
            new { Id = "doc4", Title = "Computer Vision", Content = "Image recognition and convolutional networks" },
            new { Id = "doc5", Title = "Reinforcement Learning", Content = "Agents, rewards, and policy optimization" }
        };

        // 문서를 청크로 분할하여 저장 (실제로는 FileFlux가 담당)
        var allChunks = new List<DocumentChunk>();
        foreach (var doc in documents)
        {
            var chunks = SplitDocumentIntoChunks(doc.Id, doc.Title, doc.Content);
            allChunks.AddRange(chunks);
        }

        var stopwatch = Stopwatch.StartNew();

        // Act 1: 대량 인덱싱
        var ids = await vectorStore.StoreBatchAsync(allChunks);
        var indexingTime = stopwatch.ElapsedMilliseconds;

        _output.WriteLine($"인덱싱 완료: {allChunks.Count}개 청크, {indexingTime}ms");

        // Act 2: 의미적 검색
        stopwatch.Restart();
        var neuralNetworkQuery = CreateQueryEmbedding("neural networks deep learning");
        var neuralResults = await vectorStore.SearchAsync(neuralNetworkQuery, topK: 3, minScore: 0.1f);
        var searchTime1 = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        var textProcessingQuery = CreateQueryEmbedding("text processing language understanding");
        var nlpResults = await vectorStore.SearchAsync(textProcessingQuery, topK: 3, minScore: 0.1f);
        var searchTime2 = stopwatch.ElapsedMilliseconds;

        // Assert
        ids.Should().HaveCount(allChunks.Count);
        ids.Should().AllSatisfy(id => id.Should().NotBeEmpty());

        // 신경망 쿼리는 Deep Learning 관련 문서를 찾아야 함
        neuralResults.Should().NotBeEmpty();
        neuralResults.Should().Contain(r => r.DocumentId == "doc2"); // Deep Learning document

        // NLP 쿼리는 Natural Language Processing 관련 문서를 찾아야 함
        nlpResults.Should().NotBeEmpty();
        nlpResults.Should().Contain(r => r.DocumentId == "doc3"); // NLP document

        // 성능 검증 (관대한 기준)
        indexingTime.Should().BeLessThan(5000); // 5초 이하
        searchTime1.Should().BeLessThan(1000); // 1초 이하
        searchTime2.Should().BeLessThan(1000); // 1초 이하

        _output.WriteLine($"검색 성능: Neural={searchTime1}ms, NLP={searchTime2}ms");
    }

    [Fact]
    public async Task PerformanceTest_LargeScaleOperations_ShouldMeetPerformanceTargets()
    {
        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        const int chunkCount = 5000;
        const int searchIterations = 100;

        // 대량 테스트 데이터 생성
        var chunks = GenerateLargeTestDataset(chunkCount);
        var queryVectors = Enumerable.Range(0, searchIterations)
            .Select(_ => CreateRandomEmbedding())
            .ToList();

        _output.WriteLine($"테스트 데이터 생성 완료: {chunkCount}개 청크, {searchIterations}개 쿼리");

        var stopwatch = Stopwatch.StartNew();

        // Act 1: 대량 배치 인덱싱
        var batchSize = 1000;
        var allIds = new List<string>();

        for (int i = 0; i < chunks.Count; i += batchSize)
        {
            var batch = chunks.Skip(i).Take(batchSize);
            var batchIds = await vectorStore.StoreBatchAsync(batch);
            allIds.AddRange(batchIds);
        }

        var totalIndexingTime = stopwatch.ElapsedMilliseconds;
        var avgIndexingTimePerChunk = (double)totalIndexingTime / chunkCount;

        _output.WriteLine($"배치 인덱싱: {totalIndexingTime}ms, 평균 {avgIndexingTimePerChunk:F2}ms/청크");

        // Act 2: 연속 검색 성능 테스트
        var searchTimes = new List<long>();
        var resultCounts = new List<int>();

        for (int i = 0; i < searchIterations; i++)
        {
            stopwatch.Restart();
            var results = await vectorStore.SearchAsync(queryVectors[i], topK: 10, minScore: 0.0f);
            var searchTime = stopwatch.ElapsedMilliseconds;

            searchTimes.Add(searchTime);
            resultCounts.Add(results.Count());

            if (i % 20 == 0) // 중간 진행 상황 출력
            {
                _output.WriteLine($"검색 진행률: {i + 1}/{searchIterations}");
            }
        }

        // Act 3: 동시 검색 성능 테스트
        var concurrentTasks = queryVectors.Take(20).Select(async queryVector =>
        {
            var sw = Stopwatch.StartNew();
            var results = await vectorStore.SearchAsync(queryVector, topK: 5);
            sw.Stop();
            return new { Time = sw.ElapsedMilliseconds, Count = results.Count() };
        });

        var concurrentResults = await Task.WhenAll(concurrentTasks);

        // Assert & Performance Analysis
        allIds.Should().HaveCount(chunkCount);

        // 인덱싱 성능 검증
        avgIndexingTimePerChunk.Should().BeLessThan(10.0); // 청크당 10ms 이하

        // 검색 성능 통계
        var avgSearchTime = searchTimes.Average();
        var p95SearchTime = searchTimes.OrderBy(t => t).Skip((int)(searchIterations * 0.95)).First();
        var avgResultCount = resultCounts.Average();

        _output.WriteLine($"검색 성능 통계:");
        _output.WriteLine($"  평균 검색 시간: {avgSearchTime:F2}ms");
        _output.WriteLine($"  95th 백분위수: {p95SearchTime}ms");
        _output.WriteLine($"  평균 결과 수: {avgResultCount:F2}개");

        // 검색 성능 기준
        avgSearchTime.Should().BeLessThan(100.0); // 평균 100ms 이하
        p95SearchTime.Should().BeLessThan(500); // 95% 요청이 500ms 이하
        avgResultCount.Should().BeGreaterThan(0); // 결과가 있어야 함

        // 동시 검색 성능
        var avgConcurrentTime = concurrentResults.Average(r => r.Time);
        _output.WriteLine($"동시 검색 평균 시간: {avgConcurrentTime:F2}ms");

        avgConcurrentTime.Should().BeLessThan(200.0); // 동시성 환경에서도 200ms 이하
    }

    [Fact]
    public async Task AccuracyTest_VectorSimilarity_ShouldReturnRelevantResults()
    {
        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();

        // 의미적으로 관련된 문서 그룹 생성
        var techDocs = new[]
        {
            CreateDocumentChunk("tech1", "Python programming tutorial", "learn python coding"),
            CreateDocumentChunk("tech2", "Java development guide", "java programming language"),
            CreateDocumentChunk("tech3", "JavaScript web development", "javascript frontend backend")
        };

        var foodDocs = new[]
        {
            CreateDocumentChunk("food1", "Italian cooking recipes", "pasta pizza italian cuisine"),
            CreateDocumentChunk("food2", "Asian food guide", "sushi ramen chinese japanese"),
            CreateDocumentChunk("food3", "French pastry techniques", "croissant baking french desserts")
        };

        var sportsDocs = new[]
        {
            CreateDocumentChunk("sports1", "Football training", "soccer football training exercises"),
            CreateDocumentChunk("sports2", "Basketball techniques", "basketball shooting dribbling"),
            CreateDocumentChunk("sports3", "Tennis strategies", "tennis serve volley strategies")
        };

        var allChunks = techDocs.Concat(foodDocs).Concat(sportsDocs).ToList();
        await vectorStore.StoreBatchAsync(allChunks);

        // Act & Assert
        // 기술 관련 쿼리
        var programmingQuery = CreateQueryEmbedding("software development programming");
        var programmingResults = await vectorStore.SearchAsync(programmingQuery, topK: 5, minScore: 0.1f);

        programmingResults.Should().NotBeEmpty();
        var programmingDocs = programmingResults.Select(r => r.DocumentId).ToList();
        programmingDocs.Should().Contain(id => id.ToString().StartsWith("tech"));

        _output.WriteLine($"프로그래밍 쿼리 결과: {string.Join(", ", programmingDocs.Cast<object>())}");

        // 음식 관련 쿼리
        var cookingQuery = CreateQueryEmbedding("cooking recipes food preparation");
        var cookingResults = await vectorStore.SearchAsync(cookingQuery, topK: 5, minScore: 0.1f);

        cookingResults.Should().NotBeEmpty();
        var cookingDocs = cookingResults.Select(r => r.DocumentId).ToList();
        cookingDocs.Should().Contain(id => id.ToString().StartsWith("food"));

        _output.WriteLine($"요리 쿼리 결과: {string.Join(", ", cookingDocs.Cast<object>())}");

        // 스포츠 관련 쿼리
        var sportsQuery = CreateQueryEmbedding("sports training athletic performance");
        var sportsResults = await vectorStore.SearchAsync(sportsQuery, topK: 5, minScore: 0.1f);

        sportsResults.Should().NotBeEmpty();
        var sportsDocsFound = sportsResults.Select(r => r.DocumentId).ToList();
        sportsDocsFound.Should().Contain(id => id.ToString().StartsWith("sports"));

        _output.WriteLine($"스포츠 쿼리 결과: {string.Join(", ", sportsDocsFound.Cast<object>())}");
    }

    [Fact]
    public async Task ConcurrencyTest_MultipleOperations_ShouldNotCauseDataCorruption()
    {
        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        const int concurrentUsers = 10;
        const int operationsPerUser = 20;

        // Act
        var tasks = Enumerable.Range(0, concurrentUsers).Select(userId =>
            Task.Run(async () =>
            {
                var results = new List<string>();
                var errors = new List<Exception>();

                for (int i = 0; i < operationsPerUser; i++)
                {
                    try
                    {
                        // 각 사용자가 다양한 작업 수행
                        var chunk = CreateTestChunk($"user{userId}_doc{i}", i);
                        var id = await vectorStore.StoreAsync(chunk);
                        results.Add(id);

                        // 50% 확률로 검색 수행
                        if (i % 2 == 0)
                        {
                            var queryResults = await vectorStore.SearchAsync(
                                chunk.Embedding!, topK: 5, minScore: 0.0f);
                        }

                        // 30% 확률로 업데이트 수행
                        if (i % 3 == 0 && results.Count > 1)
                        {
                            chunk.Id = results[^2]; // 이전 항목 업데이트
                            chunk.Content = $"Updated content {i}";
                            await vectorStore.UpdateAsync(chunk);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }

                return new { UserId = userId, Results = results, Errors = errors };
            }));

        var userResults = await Task.WhenAll(tasks);

        // Assert
        var totalResults = userResults.SelectMany(r => r.Results).ToList();
        var totalErrors = userResults.SelectMany(r => r.Errors).ToList();

        totalResults.Should().HaveCount(concurrentUsers * operationsPerUser);
        totalResults.Should().AllSatisfy(id => id.Should().NotBeEmpty());
        totalResults.Should().OnlyHaveUniqueItems(); // ID 중복 없어야 함

        if (totalErrors.Any())
        {
            _output.WriteLine($"동시성 테스트 중 {totalErrors.Count}개 오류 발생:");
            foreach (var error in totalErrors.Take(5)) // 처음 5개만 출력
            {
                _output.WriteLine($"  {error.GetType().Name}: {error.Message}");
            }
        }

        // 일부 오류는 허용하지만, 대부분은 성공해야 함
        var errorRate = (double)totalErrors.Count / (concurrentUsers * operationsPerUser);
        errorRate.Should().BeLessThan(0.05); // 5% 이하 오류율

        // 최종 데이터 일관성 확인
        var finalCount = await vectorStore.CountAsync();
        finalCount.Should().BeGreaterThanOrEqualTo((int)(totalResults.Count * 0.9)); // 90% 이상 저장됨

        _output.WriteLine($"동시성 테스트 결과: {totalResults.Count}개 성공, {totalErrors.Count}개 오류, 최종 개수: {finalCount}");
    }

    private List<DocumentChunk> SplitDocumentIntoChunks(string documentId, string title, string content)
    {
        // 간단한 청크 분할 시뮬레이션 (실제로는 FileFlux 사용)
        var chunks = new List<DocumentChunk>();

        // 제목 청크
        chunks.Add(CreateDocumentChunk(documentId, title, $"title:{title}", chunkIndex: 0));

        // 내용을 단어 단위로 분할 (간단한 방식)
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var chunkSize = Math.Max(5, words.Length / 3); // 3개 청크로 분할

        for (int i = 0; i < words.Length; i += chunkSize)
        {
            var chunkWords = words.Skip(i).Take(chunkSize);
            var chunkContent = string.Join(" ", chunkWords);

            chunks.Add(CreateDocumentChunk(
                documentId,
                chunkContent,
                $"content:{chunkContent}",
                chunkIndex: i / chunkSize + 1));
        }

        return chunks;
    }

    private List<DocumentChunk> GenerateLargeTestDataset(int count)
    {
        var random = new Random(12345); // 고정 시드로 재현 가능
        var categories = new[] { "technology", "science", "health", "education", "business" };
        var chunks = new List<DocumentChunk>();

        for (int i = 0; i < count; i++)
        {
            var category = categories[i % categories.Length];
            var docId = $"{category}_doc_{i / 10}";
            var chunkIndex = i % 10;

            var chunk = CreateTestChunk(docId, chunkIndex);
            chunk.Content = $"Generated content for {category} document {i}. " +
                           $"This is chunk {chunkIndex} with random data: {random.Next(1000, 9999)}";

            chunks.Add(chunk);
        }

        return chunks;
    }

    private DocumentChunk CreateDocumentChunk(string documentId, string content, string embeddingContext, int chunkIndex = 0)
    {
        return new DocumentChunk
        {
            DocumentId = documentId,
            ChunkIndex = chunkIndex,
            Content = content,
            Embedding = CreateQueryEmbedding(embeddingContext),
            TokenCount = content.Split(' ').Length,
            Metadata = new Dictionary<string, object>
            {
                ["category"] = documentId.Split('_')[0],
                ["generated"] = DateTime.UtcNow
            }
        };
    }

    private DocumentChunk CreateTestChunk(string documentId, int chunkIndex)
    {
        return new DocumentChunk
        {
            DocumentId = documentId,
            ChunkIndex = chunkIndex,
            Content = $"Test content for document {documentId}, chunk {chunkIndex}",
            Embedding = CreateRandomEmbedding(),
            TokenCount = 50,
            Metadata = new Dictionary<string, object>
            {
                ["test"] = true,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }
        };
    }

    private float[] CreateQueryEmbedding(string text)
    {
        // 실제로는 임베딩 모델을 사용하지만, 테스트에서는 텍스트 기반 의사 임베딩 생성
        var hash = text.GetHashCode();
        var random = new Random(Math.Abs(hash)); // 동일 텍스트는 동일 임베딩

        var embedding = new float[384];
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(random.NextDouble() - 0.5) * 2;
        }

        // 정규화
        var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= magnitude;
            }
        }

        return embedding;
    }

    private float[] CreateRandomEmbedding(int dimension = 384)
    {
        var random = new Random();
        var embedding = new float[dimension];

        for (int i = 0; i < dimension; i++)
        {
            embedding[i] = (float)(random.NextDouble() - 0.5) * 2;
        }

        // 정규화
        var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < dimension; i++)
            {
                embedding[i] /= magnitude;
            }
        }

        return embedding;
    }
}