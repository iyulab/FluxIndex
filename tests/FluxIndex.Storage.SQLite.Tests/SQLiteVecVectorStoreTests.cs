using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.Entities;
using FluxIndex.Storage.SQLite;
using FluxIndex.Storage.SQLite.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// SQLite-vec 벡터 저장소 테스트
/// </summary>
public class SQLiteVecVectorStoreTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly SQLiteVecOptions _options;
    private readonly string _testDatabasePath;

    public SQLiteVecVectorStoreTests(ITestOutputHelper output)
    {
        _output = output;
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"fluxindex_test_{Guid.NewGuid()}.db");

        _options = SQLiteVecOptions.CreateForTesting(useSqliteVec: false); // 테스트에서는 폴백 모드 사용
        _options.DatabasePath = _testDatabasePath;

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSQLiteVecInMemoryVectorStore(); // 인메모리 테스트

        _serviceProvider = services.BuildServiceProvider();
    }

    [SkippableFact]
    public async Task StoreAsync_WithValidChunk_ShouldReturnId()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        var chunk = CreateTestChunk();

        // Act
        var id = await vectorStore.StoreAsync(chunk);

        // Assert
        id.Should().NotBeEmpty();
        chunk.Id = id; // ID 설정

        var retrieved = await vectorStore.GetAsync(id);
        retrieved.Should().NotBeNull();
        retrieved!.DocumentId.Should().Be(chunk.DocumentId);
        retrieved.Content.Should().Be(chunk.Content);
    }

    [SkippableFact]
    public async Task StoreBatchAsync_WithMultipleChunks_ShouldReturnAllIds()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        var chunks = new List<DocumentChunk>
        {
            CreateTestChunk("doc1", 0),
            CreateTestChunk("doc1", 1),
            CreateTestChunk("doc2", 0)
        };

        // Act
        var ids = await vectorStore.StoreBatchAsync(chunks);

        // Assert
        ids.Should().HaveCount(3);
        ids.Should().AllSatisfy(id => id.Should().NotBeEmpty());

        // 각 항목이 제대로 저장되었는지 확인
        foreach (var id in ids)
        {
            var retrieved = await vectorStore.GetAsync(id);
            retrieved.Should().NotBeNull();
        }
    }

    [SkippableFact]
    public async Task SearchAsync_WithSimilarVectors_ShouldReturnOrderedResults()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();

        // 유사한 벡터들 생성
        var baseVector = CreateTestEmbedding();
        var chunks = new List<DocumentChunk>
        {
            CreateTestChunk("doc1", 0, baseVector), // 동일한 벡터 (최고 유사도)
            CreateTestChunk("doc1", 1, AddNoise(baseVector, 0.1f)), // 약간의 노이즈
            CreateTestChunk("doc2", 0, AddNoise(baseVector, 0.3f)), // 더 많은 노이즈
            CreateTestChunk("doc3", 0, CreateRandomEmbedding()) // 완전히 다른 벡터
        };

        await vectorStore.StoreBatchAsync(chunks);

        // Act
        var results = await vectorStore.SearchAsync(baseVector, topK: 3, minScore: 0.5f);

        // Assert
        results.Should().NotBeEmpty();
        results.Should().HaveCountLessThanOrEqualTo(3);

        // 첫 번째 결과가 가장 유사해야 함 (동일한 벡터)
        results.First().DocumentId.Should().Be("doc1");
        results.First().ChunkIndex.Should().Be(0);
    }

    [SkippableFact]
    public async Task SearchAsync_WithEmptyStore_ShouldReturnEmpty()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        var queryVector = CreateTestEmbedding();

        // Act
        var results = await vectorStore.SearchAsync(queryVector, topK: 5);

        // Assert
        results.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task GetByDocumentIdAsync_WithValidDocumentId_ShouldReturnAllChunks()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        var documentId = "test-document";
        var chunks = new List<DocumentChunk>
        {
            CreateTestChunk(documentId, 0),
            CreateTestChunk(documentId, 1),
            CreateTestChunk(documentId, 2),
            CreateTestChunk("other-document", 0) // 다른 문서
        };

        await vectorStore.StoreBatchAsync(chunks);

        // Act
        var results = await vectorStore.GetByDocumentIdAsync(documentId);

        // Assert
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(chunk => chunk.DocumentId.Should().Be(documentId));

        // 청크 인덱스 순서대로 정렬되어야 함
        var sortedResults = results.OrderBy(c => c.ChunkIndex).ToList();
        for (int i = 0; i < 3; i++)
        {
            sortedResults[i].ChunkIndex.Should().Be(i);
        }
    }

    [SkippableFact]
    public async Task UpdateAsync_WithValidChunk_ShouldUpdateSuccessfully()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        var chunk = CreateTestChunk();
        var id = await vectorStore.StoreAsync(chunk);

        // Act
        chunk.Id = id;
        chunk.Content = "Updated content";
        chunk.TokenCount = 999;
        chunk.Metadata!["updated"] = true;

        var updated = await vectorStore.UpdateAsync(chunk);

        // Assert
        updated.Should().BeTrue();

        var retrieved = await vectorStore.GetAsync(id);
        retrieved.Should().NotBeNull();
        retrieved!.Content.Should().Be("Updated content");
        retrieved.TokenCount.Should().Be(999);
        retrieved.Metadata.Should().ContainKey("updated");
    }

    [SkippableFact]
    public async Task DeleteAsync_WithValidId_ShouldRemoveChunk()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        var chunk = CreateTestChunk();
        var id = await vectorStore.StoreAsync(chunk);

        // 저장 확인
        var existsBefore = await vectorStore.ExistsAsync(id);
        existsBefore.Should().BeTrue();

        // Act
        var deleted = await vectorStore.DeleteAsync(id);

        // Assert
        deleted.Should().BeTrue();

        var existsAfter = await vectorStore.ExistsAsync(id);
        existsAfter.Should().BeFalse();

        var retrieved = await vectorStore.GetAsync(id);
        retrieved.Should().BeNull();
    }

    [SkippableFact]
    public async Task DeleteByDocumentIdAsync_WithValidDocumentId_ShouldRemoveAllChunks()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        var documentId = "test-document";
        var chunks = new List<DocumentChunk>
        {
            CreateTestChunk(documentId, 0),
            CreateTestChunk(documentId, 1),
            CreateTestChunk("other-document", 0) // 보존되어야 할 청크
        };

        var ids = await vectorStore.StoreBatchAsync(chunks);

        // Act
        var deleted = await vectorStore.DeleteByDocumentIdAsync(documentId);

        // Assert
        deleted.Should().BeTrue();

        // 해당 문서의 청크들이 삭제되었는지 확인
        var remainingChunks = await vectorStore.GetByDocumentIdAsync(documentId);
        remainingChunks.Should().BeEmpty();

        // 다른 문서의 청크는 보존되어야 함
        var otherChunks = await vectorStore.GetByDocumentIdAsync("other-document");
        otherChunks.Should().HaveCount(1);
    }

    [SkippableFact]
    public async Task CountAsync_WithStoredChunks_ShouldReturnCorrectCount()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        var chunks = new List<DocumentChunk>
        {
            CreateTestChunk("doc1", 0),
            CreateTestChunk("doc1", 1),
            CreateTestChunk("doc2", 0)
        };

        var initialCount = await vectorStore.CountAsync();
        await vectorStore.StoreBatchAsync(chunks);

        // Act
        var finalCount = await vectorStore.CountAsync();

        // Assert
        finalCount.Should().Be(initialCount + 3);
    }

    [SkippableFact]
    public async Task ClearAsync_WithStoredChunks_ShouldRemoveAllChunks()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        var chunks = new List<DocumentChunk>
        {
            CreateTestChunk("doc1", 0),
            CreateTestChunk("doc2", 0)
        };

        await vectorStore.StoreBatchAsync(chunks);
        var countBefore = await vectorStore.CountAsync();
        countBefore.Should().BeGreaterThan(0);

        // Act
        await vectorStore.ClearAsync();

        // Assert
        var countAfter = await vectorStore.CountAsync();
        countAfter.Should().Be(0);
    }

    [SkippableTheory]
    [InlineData(10, 5)]
    [InlineData(100, 20)]
    [InlineData(1000, 50)]
    public async Task Performance_BatchOperations_ShouldCompleteInReasonableTime(int chunkCount, int topK)
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        var chunks = Enumerable.Range(0, chunkCount)
            .Select(i => CreateTestChunk($"doc_{i / 10}", i % 10))
            .ToList();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act & Assert
        // 배치 저장 성능
        await vectorStore.StoreBatchAsync(chunks);
        var storeTime = stopwatch.ElapsedMilliseconds;
        _output.WriteLine($"배치 저장 시간 ({chunkCount}개): {storeTime}ms");

        // 검색 성능
        stopwatch.Restart();
        var searchResults = await vectorStore.SearchAsync(CreateTestEmbedding(), topK: topK);
        var searchTime = stopwatch.ElapsedMilliseconds;
        _output.WriteLine($"검색 시간 (topK={topK}): {searchTime}ms");

        // 성능 기준 (매우 관대한 기준)
        storeTime.Should().BeLessThan(chunkCount * 10); // 청크당 10ms 이하
        searchTime.Should().BeLessThan(1000); // 검색은 1초 이하
        searchResults.Should().HaveCountLessThanOrEqualTo(topK);
    }

    private DocumentChunk CreateTestChunk(string? documentId = null, int chunkIndex = 0, float[]? embedding = null)
    {
        return new DocumentChunk
        {
            DocumentId = documentId ?? "test-document",
            ChunkIndex = chunkIndex,
            Content = $"Test content for chunk {chunkIndex}",
            Embedding = embedding ?? CreateTestEmbedding(),
            TokenCount = 50,
            Metadata = new Dictionary<string, object>
            {
                ["test"] = true,
                ["chunkIndex"] = chunkIndex
            }
        };
    }

    private float[] CreateTestEmbedding(int dimension = 384)
    {
        var random = new Random(42); // 고정 시드로 재현 가능한 테스트
        var embedding = new float[dimension];

        for (int i = 0; i < dimension; i++)
        {
            embedding[i] = (float)(random.NextDouble() - 0.5) * 2; // -1 ~ 1 범위
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
        var random = new Random(); // 다른 시드
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

    public void Dispose()
    {
        _serviceProvider?.Dispose();

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
}

/// <summary>
/// SQLite-vec 확장 로더 테스트
/// </summary>
public class SQLiteVecExtensionLoaderTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;

    public SQLiteVecExtensionLoaderTests(ITestOutputHelper output)
    {
        _output = output;

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // 테스트용 옵션 설정 (확장 없이)
        services.Configure<SQLiteVecOptions>(options =>
        {
            options.UseSQLiteVec = false;
            options.FallbackToInMemoryOnError = true;
        });

        services.AddScoped<ISQLiteVecExtensionLoader, NoOpSQLiteVecExtensionLoader>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [SkippableFact]
    public void GetExtensionPath_ShouldReturnPlatformSpecificPath()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var loader = _serviceProvider.GetRequiredService<ISQLiteVecExtensionLoader>();

        // Act
        var path = loader.GetExtensionPath();

        // Assert
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            path.Should().EndWith(".dll");
        }
        else if (Environment.OSVersion.Platform == PlatformID.Unix)
        {
            path.Should().EndWith(".so");
        }
        else if (Environment.OSVersion.Platform == PlatformID.MacOSX)
        {
            path.Should().EndWith(".dylib");
        }
    }

    [SkippableFact]
    public void ExtensionFileExists_WithoutExtension_ShouldReturnFalse()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var loader = _serviceProvider.GetRequiredService<ISQLiteVecExtensionLoader>();

        // Act
        var exists = loader.ExtensionFileExists();

        // Assert - 테스트 환경에서는 확장 파일이 없어야 함
        exists.Should().BeFalse();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}

/// <summary>
/// SQLite-vec 옵션 테스트
/// </summary>
public class SQLiteVecOptionsTests
{
    [SkippableFact]
    public void Validate_WithValidOptions_ShouldNotThrow()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var options = new SQLiteVecOptions
        {
            VectorDimension = 1536,
            MaxBatchSize = 1000,
            DefaultMinScore = 0.0f
        };

        // Act & Assert
        options.Invoking(o => o.Validate()).Should().NotThrow();
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithInvalidVectorDimension_ShouldThrow(int dimension)
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var options = new SQLiteVecOptions { VectorDimension = dimension };

        // Act & Assert
        options.Invoking(o => o.Validate()).Should().Throw<ArgumentException>();
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithInvalidMaxBatchSize_ShouldThrow(int batchSize)
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var options = new SQLiteVecOptions { MaxBatchSize = batchSize };

        // Act & Assert
        options.Invoking(o => o.Validate()).Should().Throw<ArgumentException>();
    }

    [SkippableTheory]
    [InlineData(-1.1f)]
    [InlineData(1.1f)]
    public void Validate_WithInvalidMinScore_ShouldThrow(float minScore)
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var options = new SQLiteVecOptions { DefaultMinScore = minScore };

        // Act & Assert
        options.Invoking(o => o.Validate()).Should().Throw<ArgumentException>();
    }

    [SkippableFact]
    public void GetVecTableSchema_ShouldReturnValidSQL()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Arrange
        var options = new SQLiteVecOptions { VectorDimension = 1536 };

        // Act
        var schema = options.GetVecTableSchema("test_embeddings");

        // Assert
        schema.Should().Contain("CREATE VIRTUAL TABLE test_embeddings");
        schema.Should().Contain("vec0");
        schema.Should().Contain("float[1536]");
        schema.Should().Contain("metric=cosine");
    }

    [SkippableFact]
    public void CreateForTesting_ShouldReturnValidTestOptions()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Act
        var options = SQLiteVecOptions.CreateForTesting();

        // Assert
        options.UseInMemory.Should().BeTrue();
        options.AutoMigrate.Should().BeTrue();
        options.FallbackToInMemoryOnError.Should().BeTrue();
        options.VectorDimension.Should().Be(384);
        options.MaxBatchSize.Should().Be(100);
    }

    [SkippableFact]
    public void CreateForProduction_ShouldReturnValidProductionOptions()
    {
        CITestHelper.SkipIfSqliteVecNotAvailable();

        // Act
        var options = SQLiteVecOptions.CreateForProduction("test.db", 1536);

        // Assert
        options.DatabasePath.Should().Be("test.db");
        options.VectorDimension.Should().Be(1536);
        options.UseSQLiteVec.Should().BeTrue();
        options.FallbackToInMemoryOnError.Should().BeFalse();
        options.CommandTimeout.Should().Be(60);
    }
}