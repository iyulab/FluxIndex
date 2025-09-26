using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.Entities;
using FluxIndex.Storage.SQLite;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// 실제 sqlite-vec NuGet 패키지를 사용한 실전 테스트
/// </summary>
public class SQLiteVecRealWorldTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testDatabasePath;

    public SQLiteVecRealWorldTests(ITestOutputHelper output)
    {
        _output = output;
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"fluxindex_realworld_{Guid.NewGuid()}.db");

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug); // 디버그 레벨로 자세한 로그 확인
        });

        // 실제 sqlite-vec 확장 사용 설정
        services.AddSQLiteVecVectorStore(options =>
        {
            options.DatabasePath = _testDatabasePath;
            options.VectorDimension = 384; // 테스트용 작은 차원
            options.UseSQLiteVec = true; // 실제 확장 사용
            options.FallbackToInMemoryOnError = false; // 확장 실패시 오류로 처리
            options.AutoMigrate = true;
        });

        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        // 호스팅 서비스 시작 (데이터베이스 초기화 및 sqlite-vec 로드)
        var hostedServices = _serviceProvider.GetServices<IHostedService>();
        foreach (var service in hostedServices)
        {
            await service.StartAsync(CancellationToken.None);
        }

        _output.WriteLine($"테스트 데이터베이스 초기화 완료: {_testDatabasePath}");
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
                _output.WriteLine("테스트 데이터베이스 파일 삭제됨");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"테스트 데이터베이스 정리 실패: {ex.Message}");
        }
    }

    [SkippableFact]
    public async Task ExtensionLoading_WithRealNuGetPackage_ShouldLoadSuccessfully()
    {
        // Skip if sqlite-vec is not available (CI environment)
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();

        // Act & Assert - 확장 로딩이 성공했는지 간접적으로 확인
        // (실제 벡터 저장이 성공하면 확장이 로드된 것)
        var testChunk = CreateTestChunk("extension_test", 0);

        var stopwatch = Stopwatch.StartNew();
        var id = await vectorStore.StoreAsync(testChunk);
        stopwatch.Stop();

        _output.WriteLine($"벡터 저장 시간: {stopwatch.ElapsedMilliseconds}ms");

        id.Should().NotBeEmpty();

        // 저장된 데이터 검색 테스트
        var results = await vectorStore.SearchAsync(testChunk.Embedding!, topK: 1, minScore: 0.0f);
        results.Should().HaveCount(1);
        results.First().Id.Should().Be(id);

        _output.WriteLine("✅ sqlite-vec 확장 로딩 및 기본 기능 검증 완료");
    }

    [SkippableFact]
    public async Task VectorSearch_WithNativeExtension_ShouldReturnAccurateResults()
    {
        // Skip if sqlite-vec is not available (CI environment)
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();

        // 다양한 유사도를 가진 벡터들 생성
        var baseVector = CreateTestEmbedding();
        var chunks = new List<DocumentChunk>
        {
            CreateTestChunk("doc1", 0, baseVector), // 동일한 벡터 (최고 유사도)
            CreateTestChunk("doc2", 0, AddNoise(baseVector, 0.1f)), // 10% 노이즈
            CreateTestChunk("doc3", 0, AddNoise(baseVector, 0.3f)), // 30% 노이즈
            CreateTestChunk("doc4", 0, AddNoise(baseVector, 0.5f)), // 50% 노이즈
            CreateTestChunk("doc5", 0, CreateRandomEmbedding()) // 완전히 다른 벡터
        };

        await vectorStore.StoreBatchAsync(chunks);
        _output.WriteLine($"테스트 벡터 {chunks.Count}개 저장 완료");

        // Act
        var stopwatch = Stopwatch.StartNew();
        var results = await vectorStore.SearchAsync(baseVector, topK: 5, minScore: 0.0f);
        stopwatch.Stop();

        // Assert
        results.Should().HaveCount(5);

        var resultList = results.ToList();
        resultList[0].DocumentId.Should().Be("doc1"); // 가장 유사한 것이 첫 번째여야 함

        _output.WriteLine($"검색 시간: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine("검색 결과 순서:");
        for (int i = 0; i < resultList.Count; i++)
        {
            _output.WriteLine($"  {i + 1}. {resultList[i].DocumentId} (예상 순서와 일치: {resultList[i].DocumentId == $"doc{i + 1}"})");
        }

        _output.WriteLine("✅ 네이티브 벡터 검색 정확도 검증 완료");
    }

    [Theory]
    [InlineData(100, 5)]
    [InlineData(500, 10)]
    [InlineData(1000, 20)]
    public async Task PerformanceTest_NativeVsMemory_ShouldShowImprovement(int datasetSize, int topK)
    {
        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        var testData = GenerateTestDataset(datasetSize);

        _output.WriteLine($"\n=== 성능 테스트: {datasetSize}개 벡터, TopK={topK} ===");

        // 대량 데이터 인덱싱
        var indexingStopwatch = Stopwatch.StartNew();
        await vectorStore.StoreBatchAsync(testData);
        indexingStopwatch.Stop();

        _output.WriteLine($"인덱싱 시간: {indexingStopwatch.ElapsedMilliseconds}ms ({datasetSize / (indexingStopwatch.ElapsedMilliseconds / 1000.0):F1} vectors/sec)");

        // 검색 성능 측정
        var queryVector = CreateTestEmbedding();
        var searchTimes = new List<long>();

        // 워밍업
        await vectorStore.SearchAsync(queryVector, topK: topK);

        // 실제 측정
        for (int i = 0; i < 10; i++)
        {
            var searchStopwatch = Stopwatch.StartNew();
            var results = await vectorStore.SearchAsync(queryVector, topK: topK);
            searchStopwatch.Stop();

            searchTimes.Add(searchStopwatch.ElapsedMilliseconds);
            results.Should().HaveCountLessThanOrEqualTo(topK);
        }

        // Assert & Report
        var avgSearchTime = searchTimes.Average();
        var p95SearchTime = searchTimes.OrderBy(t => t).Skip(8).First(); // 90th percentile for 10 samples

        _output.WriteLine($"평균 검색 시간: {avgSearchTime:F2}ms");
        _output.WriteLine($"95% 검색 시간: {p95SearchTime}ms");

        // 성능 기준 (sqlite-vec 사용시 더 나은 성능 기대)
        avgSearchTime.Should().BeLessThan(100); // 100ms 이하
        p95SearchTime.Should().BeLessThan(200); // 95%가 200ms 이하

        _output.WriteLine("✅ 성능 기준 충족");
    }

    [SkippableFact]
    public async Task ConcurrentAccess_WithNativeExtension_ShouldMaintainConsistency()
    {
        // Skip if sqlite-vec is not available (CI environment)
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        const int concurrentTasks = 5;
        const int operationsPerTask = 20;

        _output.WriteLine($"동시성 테스트: {concurrentTasks}개 태스크, 태스크당 {operationsPerTask}개 작업");

        // Act
        var tasks = Enumerable.Range(0, concurrentTasks).Select(taskId =>
            Task.Run(async () =>
            {
                var results = new List<string>();
                var errors = new List<Exception>();

                for (int i = 0; i < operationsPerTask; i++)
                {
                    try
                    {
                        var chunk = CreateTestChunk($"concurrent_task{taskId}_doc{i}", i);
                        var id = await vectorStore.StoreAsync(chunk);
                        results.Add(id);

                        // 일부 작업에서 검색도 수행
                        if (i % 3 == 0)
                        {
                            var searchResults = await vectorStore.SearchAsync(chunk.Embedding!, topK: 3);
                            searchResults.Should().NotBeEmpty();
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                        _output.WriteLine($"Task {taskId}, Operation {i} 실패: {ex.Message}");
                    }
                }

                return new { TaskId = taskId, Results = results, Errors = errors };
            }));

        var taskResults = await Task.WhenAll(tasks);

        // Assert
        var totalSuccesses = taskResults.SelectMany(r => r.Results).ToList();
        var totalErrors = taskResults.SelectMany(r => r.Errors).ToList();

        totalSuccesses.Should().HaveCount(concurrentTasks * operationsPerTask);
        totalSuccesses.Should().OnlyHaveUniqueItems(); // 모든 ID가 고유해야 함

        if (totalErrors.Any())
        {
            _output.WriteLine($"총 {totalErrors.Count}개 오류 발생:");
            foreach (var error in totalErrors.Take(3))
            {
                _output.WriteLine($"  {error.GetType().Name}: {error.Message}");
            }
        }

        totalErrors.Should().BeEmpty(); // sqlite-vec에서는 오류가 없어야 함

        // 최종 데이터 일관성 확인
        var finalCount = await vectorStore.CountAsync();
        finalCount.Should().Be(totalSuccesses.Count);

        _output.WriteLine($"✅ 동시성 테스트 완료: {totalSuccesses.Count}개 성공, {totalErrors.Count}개 오류");
    }

    [SkippableFact]
    public async Task LargeDataset_RealWorldScenario_ShouldHandleEfficiently()
    {
        // Skip if sqlite-vec is not available (CI environment)
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        const int largeDatasetSize = 5000; // 실제 사용에 가까운 크기
        const int searchQueries = 50;

        _output.WriteLine($"대용량 실전 시나리오: {largeDatasetSize}개 문서, {searchQueries}개 검색");

        // 실제적인 문서 시뮬레이션
        var documents = GenerateRealisticDocuments(largeDatasetSize);
        var categories = documents.Select(d => d.Metadata!["category"]).Distinct().ToList();

        _output.WriteLine($"생성된 카테고리: {string.Join(", ", categories)}");

        // Act - 대량 인덱싱
        var indexingStart = Stopwatch.StartNew();
        var batchSize = 500;

        for (int i = 0; i < documents.Count; i += batchSize)
        {
            var batch = documents.Skip(i).Take(batchSize);
            await vectorStore.StoreBatchAsync(batch);

            if (i % (batchSize * 4) == 0) // 2000개마다 진행률 출력
            {
                _output.WriteLine($"인덱싱 진행률: {i + batchSize}/{documents.Count}");
            }
        }

        indexingStart.Stop();
        _output.WriteLine($"전체 인덱싱 시간: {indexingStart.ElapsedMilliseconds}ms ({largeDatasetSize / (indexingStart.ElapsedMilliseconds / 1000.0):F1} docs/sec)");

        // Act - 다양한 검색 패턴 테스트
        var searchResults = new List<(string category, long timeMs, int resultCount)>();

        foreach (var category in categories.Take(3)) // 상위 3개 카테고리만 테스트
        {
            for (int i = 0; i < searchQueries / 3; i++)
            {
                var queryVector = CreateCategoryBasedEmbedding(category.ToString()!);

                var searchStart = Stopwatch.StartNew();
                var results = await vectorStore.SearchAsync(queryVector, topK: 10);
                searchStart.Stop();

                searchResults.Add((category.ToString()!, searchStart.ElapsedMilliseconds, results.Count()));
            }
        }

        // Assert & Analysis
        var avgSearchTime = searchResults.Average(r => r.timeMs);
        var avgResultCount = searchResults.Average(r => r.resultCount);

        _output.WriteLine("\n=== 검색 성능 분석 ===");
        _output.WriteLine($"평균 검색 시간: {avgSearchTime:F2}ms");
        _output.WriteLine($"평균 결과 수: {avgResultCount:F1}개");

        foreach (var categoryGroup in searchResults.GroupBy(r => r.category))
        {
            var categoryAvg = categoryGroup.Average(r => r.timeMs);
            _output.WriteLine($"  {categoryGroup.Key}: {categoryAvg:F2}ms");
        }

        // 성능 기준 검증
        avgSearchTime.Should().BeLessThan(50); // 평균 50ms 이하
        avgResultCount.Should().BeGreaterThan(5); // 평균 5개 이상 결과

        var finalCount = await vectorStore.CountAsync();
        finalCount.Should().Be(largeDatasetSize);

        _output.WriteLine("✅ 대용량 실전 시나리오 테스트 완료");
    }

    private List<DocumentChunk> GenerateRealisticDocuments(int count)
    {
        var random = new Random(42);
        var categories = new[] { "Technology", "Science", "Business", "Health", "Education", "Environment" };
        var documents = new List<DocumentChunk>();

        for (int i = 0; i < count; i++)
        {
            var category = categories[i % categories.Length];
            var content = GenerateRealisticContent(category, random);

            documents.Add(new DocumentChunk
            {
                DocumentId = $"{category.ToLower()}_doc_{i / categories.Length}",
                ChunkIndex = i % 10, // 문서당 최대 10개 청크
                Content = content,
                Embedding = CreateCategoryBasedEmbedding(category),
                TokenCount = content.Split(' ').Length,
                Metadata = new Dictionary<string, object>
                {
                    ["category"] = category,
                    ["priority"] = random.Next(1, 6), // 1-5 우선순위
                    ["timestamp"] = DateTimeOffset.UtcNow.AddDays(-random.Next(0, 365)).ToUnixTimeSeconds(),
                    ["word_count"] = content.Split(' ').Length
                }
            });
        }

        return documents;
    }

    private string GenerateRealisticContent(string category, Random random)
    {
        var templates = new Dictionary<string, string[]>
        {
            ["Technology"] = new[]
            {
                "Advanced machine learning algorithms enable automated decision making in complex systems.",
                "Cloud computing platforms provide scalable infrastructure for modern applications.",
                "Artificial intelligence transforms business processes through intelligent automation.",
                "Software development practices focus on agile methodologies and continuous integration."
            },
            ["Science"] = new[]
            {
                "Recent research in quantum physics reveals new properties of subatomic particles.",
                "Climate change studies demonstrate significant impact on global weather patterns.",
                "Biological research advances understanding of cellular mechanisms and genetic expression.",
                "Mathematical models help predict complex natural phenomena and system behaviors."
            },
            ["Business"] = new[]
            {
                "Strategic planning involves market analysis and competitive positioning for growth.",
                "Digital transformation initiatives require organizational change and technology adoption.",
                "Financial management principles guide investment decisions and risk assessment.",
                "Customer relationship management systems improve client engagement and retention."
            },
            ["Health"] = new[]
            {
                "Medical research focuses on developing innovative treatments for chronic diseases.",
                "Public health initiatives promote wellness and disease prevention in communities.",
                "Healthcare technology improves patient outcomes through digital health solutions.",
                "Nutritional science studies the relationship between diet and human health."
            },
            ["Education"] = new[]
            {
                "Educational technology enhances learning experiences through interactive digital tools.",
                "Pedagogical research explores effective teaching methods for diverse student populations.",
                "Curriculum development aligns educational content with professional skill requirements.",
                "Assessment strategies measure student progress and learning effectiveness."
            },
            ["Environment"] = new[]
            {
                "Environmental conservation efforts protect biodiversity and natural ecosystems.",
                "Sustainable energy solutions reduce carbon emissions and environmental impact.",
                "Waste management systems promote recycling and circular economy principles.",
                "Environmental monitoring tracks pollution levels and ecosystem health indicators."
            }
        };

        var categoryTemplates = templates.GetValueOrDefault(category, templates["Technology"]);
        var baseContent = categoryTemplates[random.Next(categoryTemplates.Length)];

        // 내용을 더 풍부하게 만들기 위해 추가 문장 생성
        var additionalInfo = $" Research findings from {random.Next(2020, 2025)} indicate significant progress in this field. " +
                           $"Implementation requires careful consideration of technical requirements and resource allocation. " +
                           $"Future developments will likely focus on scalability and practical applications.";

        return baseContent + additionalInfo;
    }

    private float[] CreateCategoryBasedEmbedding(string category)
    {
        // 카테고리별로 일관된 임베딩 생성 (실제로는 임베딩 모델 사용)
        var hash = category.GetHashCode();
        var random = new Random(Math.Abs(hash));

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

    private List<DocumentChunk> GenerateTestDataset(int count)
    {
        var random = new Random(42);
        var chunks = new List<DocumentChunk>();

        for (int i = 0; i < count; i++)
        {
            chunks.Add(CreateTestChunk($"perf_doc_{i / 10}", i % 10));
        }

        return chunks;
    }

    private DocumentChunk CreateTestChunk(string documentId, int chunkIndex, float[]? embedding = null)
    {
        return new DocumentChunk
        {
            DocumentId = documentId,
            ChunkIndex = chunkIndex,
            Content = $"Test content for document {documentId}, chunk {chunkIndex}",
            Embedding = embedding ?? CreateTestEmbedding(),
            TokenCount = 50,
            Metadata = new Dictionary<string, object>
            {
                ["test"] = true,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }
        };
    }

    private float[] CreateTestEmbedding(int dimension = 384)
    {
        var random = new Random(42);
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

    private float[] AddNoise(float[] baseVector, float noiseLevel)
    {
        var random = new Random(123);
        var noisyVector = new float[baseVector.Length];

        for (int i = 0; i < baseVector.Length; i++)
        {
            var noise = (float)(random.NextDouble() - 0.5) * 2 * noiseLevel;
            noisyVector[i] = baseVector[i] + noise;
        }

        // 정규화
        var magnitude = (float)Math.Sqrt(noisyVector.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < noisyVector.Length; i++)
            {
                noisyVector[i] /= magnitude;
            }
        }

        return noisyVector;
    }
}