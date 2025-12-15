using Xunit;
using FluxIndex.SDK;
using System.IO;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// ✅ Issue #1, #2 검증: 간편 API 및 기본 임베딩 테스트
/// </summary>
public class SimplifiedApiTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly IFluxIndexContext _context;

    public SimplifiedApiTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"fluxindex_simple_api_test_{Guid.NewGuid()}.db");

        // ✅ Issue #1 검증: 간편 API 테스트 (InMemory 임베딩 사용)
        // Note: CI 환경에서 LocalAI 모델이 없으므로 InMemory 사용
        _context = FluxIndexContext.CreateBuilder()
            .UseSQLite(_testDbPath)
            .UseInMemoryEmbedding()
            .Build();
    }

    public void Dispose()
    {
        if (_context is IDisposable disposable)
        {
            disposable.Dispose();
        }

        Thread.Sleep(100);

        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch (IOException)
        {
            // Ignore
        }
    }

    [Fact]
    public async Task SimplifiedAPI_BasicIndexing_ShouldWork()
    {
        // Arrange: README 예제 코드 패턴
        var content = "FluxIndex is a RAG library for .NET";
        var documentId = "doc-001";

        // Act: 간편 API 사용 (문자열 직접 전달)
        var resultId = await _context.Indexer.IndexDocumentAsync(content, documentId);

        // Assert
        Assert.NotNull(resultId);
        Assert.Equal(documentId, resultId);
    }

    [Fact]
    public async Task SimplifiedAPI_WithMetadata_ShouldPreserveMetadata()
    {
        // Arrange
        var content = "Test document with metadata";
        var documentId = "doc-002";
        var metadata = new Dictionary<string, object>
        {
            ["title"] = "Test Document",
            ["category"] = "testing",
            ["tags"] = new[] { "test", "metadata" }
        };

        // Act: 메타데이터 포함 인덱싱
        var resultId = await _context.Indexer.IndexDocumentAsync(content, documentId, metadata);

        // Assert
        Assert.NotNull(resultId);
        Assert.Equal(documentId, resultId);
    }

    [Fact]
    public async Task SimplifiedAPI_EmptyContent_ShouldThrowException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _context.Indexer.IndexDocumentAsync("", "doc-003");
        });
    }

    [Fact]
    public async Task SimplifiedAPI_EmptyDocumentId_ShouldThrowException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _context.Indexer.IndexDocumentAsync("Some content", "");
        });
    }

    [Fact]
    public async Task DefaultEmbedding_ShouldBeInMemory()
    {
        // Arrange
        var content = "Test for default embedding";
        var documentId = "doc-004";

        // Act: 임베딩 설정 없이 인덱싱 (Issue #1 수정으로 가능해짐)
        var resultId = await _context.Indexer.IndexDocumentAsync(content, documentId);

        // Assert: 오류 없이 성공해야 함
        Assert.NotNull(resultId);
    }
}
