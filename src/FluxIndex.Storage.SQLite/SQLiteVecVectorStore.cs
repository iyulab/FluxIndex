using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// sqlite-vec 확장을 사용하는 고성능 SQLite 벡터 저장소
/// </summary>
public class SQLiteVecVectorStore : IVectorStore
{
    private readonly SQLiteVecDbContext _context;
    private readonly ILogger<SQLiteVecVectorStore> _logger;
    private readonly SQLiteVecOptions _options;
    private readonly ISQLiteVecExtensionLoader _extensionLoader;

    // 폴백용 in-memory 벡터 저장소 (sqlite-vec 실패 시 사용)
    private readonly Lazy<SQLiteVectorStore> _fallbackStore;
    private bool _sqliteVecAvailable = true;

    public SQLiteVecVectorStore(
        SQLiteVecDbContext context,
        ILogger<SQLiteVecVectorStore> logger,
        IOptions<SQLiteVecOptions> options,
        ISQLiteVecExtensionLoader extensionLoader,
        Lazy<SQLiteVectorStore> fallbackStore)
    {
        _context = context;
        _logger = logger;
        _options = options.Value;
        _extensionLoader = extensionLoader;
        _fallbackStore = fallbackStore;
    }

    public async Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
            {
                return await _fallbackStore.Value.StoreAsync(chunk, cancellationToken);
            }

            var id = Guid.NewGuid().ToString();

            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // 1. 메타데이터 저장
                var chunkEntity = new VectorChunkEntity
                {
                    Id = id,
                    DocumentId = chunk.DocumentId,
                    ChunkIndex = chunk.ChunkIndex,
                    Content = chunk.Content,
                    TokenCount = chunk.TokenCount,
                    Metadata = chunk.Metadata ?? new Dictionary<string, object>(),
                    CreatedAt = DateTime.UtcNow
                };

                _context.VectorChunks.Add(chunkEntity);

                // 2. 벡터 저장 (sqlite-vec 사용)
                if (chunk.Embedding != null && _sqliteVecAvailable)
                {
                    await _context.StoreVectorInVecTableAsync(id, chunk.Embedding, cancellationToken);
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                _logger.LogDebug("벡터 저장 완료: {Id}, Document: {DocumentId}", id, chunk.DocumentId);
                return id;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "벡터 저장 실패: Document {DocumentId}", chunk.DocumentId);
            throw;
        }
    }

    public async Task<IEnumerable<string>> StoreBatchAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        if (!chunkList.Any())
            return Enumerable.Empty<string>();

        try
        {
            await EnsureInitializedAsync(cancellationToken);

            if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
            {
                return await _fallbackStore.Value.StoreBatchAsync(chunkList, cancellationToken);
            }

            var ids = new List<string>();
            var batchSize = Math.Min(_options.MaxBatchSize, chunkList.Count);

            for (int i = 0; i < chunkList.Count; i += batchSize)
            {
                var batch = chunkList.Skip(i).Take(batchSize);
                var batchIds = await StoreBatchInternalAsync(batch, cancellationToken);
                ids.AddRange(batchIds);
            }

            _logger.LogInformation("배치 벡터 저장 완료: {Count}개 항목", ids.Count);
            return ids;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "배치 벡터 저장 실패: {Count}개 항목", chunkList.Count);
            throw;
        }
    }

    private async Task<IEnumerable<string>> StoreBatchInternalAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var ids = new List<string>();

            foreach (var chunk in chunks)
            {
                var id = Guid.NewGuid().ToString();
                ids.Add(id);

                // 메타데이터 저장
                var chunkEntity = new VectorChunkEntity
                {
                    Id = id,
                    DocumentId = chunk.DocumentId,
                    ChunkIndex = chunk.ChunkIndex,
                    Content = chunk.Content,
                    TokenCount = chunk.TokenCount,
                    Metadata = chunk.Metadata ?? new Dictionary<string, object>(),
                    CreatedAt = DateTime.UtcNow
                };

                _context.VectorChunks.Add(chunkEntity);

                // 벡터 저장 (나중에 배치로 처리)
                if (chunk.Embedding != null && _sqliteVecAvailable)
                {
                    await _context.StoreVectorInVecTableAsync(id, chunk.Embedding, cancellationToken);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ids;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<DocumentChunk?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
            {
                return await _fallbackStore.Value.GetAsync(id, cancellationToken);
            }

            var chunkEntity = await _context.VectorChunks
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (chunkEntity == null)
                return null;

            return new DocumentChunk
            {
                Id = chunkEntity.Id,
                DocumentId = chunkEntity.DocumentId,
                ChunkIndex = chunkEntity.ChunkIndex,
                Content = chunkEntity.Content,
                Embedding = null, // 필요시 별도 쿼리로 로드
                TokenCount = chunkEntity.TokenCount,
                Metadata = chunkEntity.Metadata
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "벡터 조회 실패: {Id}", id);
            throw;
        }
    }

    public async Task<IEnumerable<DocumentChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
            {
                return await _fallbackStore.Value.SearchAsync(queryEmbedding, topK, minScore, cancellationToken);
            }

            return await SearchWithSQLiteVecAsync(queryEmbedding, topK, minScore, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "벡터 검색 실패");

            // 폴백 모드 활성화
            if (_options.FallbackToInMemoryOnError)
            {
                _logger.LogInformation("폴백 모드로 검색 재시도");
                _sqliteVecAvailable = false;
                return await _fallbackStore.Value.SearchAsync(queryEmbedding, topK, minScore, cancellationToken);
            }

            throw;
        }
    }

    private async Task<IEnumerable<DocumentChunk>> SearchWithSQLiteVecAsync(
        float[] queryEmbedding,
        int topK,
        float minScore,
        CancellationToken cancellationToken)
    {
        // sqlite-vec 네이티브 검색 사용
        var vectorString = "[" + string.Join(",", queryEmbedding.Select(f => f.ToString("F6"))) + "]";

        var sql = @"
            SELECT
                vc.Id,
                vc.DocumentId,
                vc.ChunkIndex,
                vc.Content,
                vc.TokenCount,
                vc.Metadata,
                ce.distance
            FROM chunk_embeddings ce
            JOIN vector_chunks vc ON vc.Id = ce.chunk_id
            WHERE ce.embedding MATCH @vector AND k = @k
            AND ce.distance >= @minScore
            ORDER BY ce.distance";

        try
        {
            var connection = _context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@vector", vectorString));
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@k", topK));
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@minScore", minScore));

            var results = new List<DocumentChunk>();

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var metadataJson = reader.GetString(5); // metadata column index
                var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson) ?? new Dictionary<string, object>();

                var chunk = new DocumentChunk
                {
                    Id = reader.GetString(0), // id column index
                    DocumentId = reader.GetString(1), // document_id column index
                    ChunkIndex = reader.GetInt32(2), // chunk_index column index
                    Content = reader.GetString(3), // content column index
                    TokenCount = reader.GetInt32(4), // token_count column index
                    Metadata = metadata,
                    Embedding = null // 검색 결과에서는 임베딩을 제외하여 성능 향상
                };

                results.Add(chunk);
            }

            _logger.LogDebug("sqlite-vec 검색 완료: {Count}개 결과", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "sqlite-vec 네이티브 검색 실패");
            throw;
        }
    }

    public async Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
            {
                return await _fallbackStore.Value.GetByDocumentIdAsync(documentId, cancellationToken);
            }

            var entities = await _context.VectorChunks
                .Where(c => c.DocumentId == documentId)
                .OrderBy(c => c.ChunkIndex)
                .ToListAsync(cancellationToken);

            return entities.Select(e => new DocumentChunk
            {
                Id = e.Id,
                DocumentId = e.DocumentId,
                ChunkIndex = e.ChunkIndex,
                Content = e.Content,
                TokenCount = e.TokenCount,
                Metadata = e.Metadata,
                Embedding = null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "문서 청크 조회 실패: {DocumentId}", documentId);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
            {
                return await _fallbackStore.Value.DeleteAsync(id, cancellationToken);
            }

            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var entity = await _context.VectorChunks
                    .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

                if (entity == null)
                    return false;

                _context.VectorChunks.Remove(entity);

                // vec0 테이블에서도 삭제
                if (_sqliteVecAvailable)
                {
                    await _context.DeleteVectorFromVecTableAsync(id, cancellationToken);
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return true;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "벡터 삭제 실패: {Id}", id);
            throw;
        }
    }

    public async Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
            {
                return await _fallbackStore.Value.DeleteByDocumentIdAsync(documentId, cancellationToken);
            }

            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var entities = await _context.VectorChunks
                    .Where(c => c.DocumentId == documentId)
                    .ToListAsync(cancellationToken);

                if (!entities.Any())
                    return false;

                // vec0 테이블에서 벡터들 삭제
                if (_sqliteVecAvailable)
                {
                    foreach (var entity in entities)
                    {
                        await _context.DeleteVectorFromVecTableAsync(entity.Id, cancellationToken);
                    }
                }

                _context.VectorChunks.RemoveRange(entities);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return true;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "문서 벡터 삭제 실패: {DocumentId}", documentId);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
        {
            return await _fallbackStore.Value.ExistsAsync(id, cancellationToken);
        }

        return await _context.VectorChunks.AnyAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await GetAsync(id, cancellationToken);
    }

    public async Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
        {
            return await _fallbackStore.Value.GetChunksByIdsAsync(ids, cancellationToken);
        }

        var entities = await _context.VectorChunks
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(cancellationToken);

        return entities.Select(e => new DocumentChunk
        {
            Id = e.Id,
            DocumentId = e.DocumentId,
            ChunkIndex = e.ChunkIndex,
            Content = e.Content,
            TokenCount = e.TokenCount,
            Metadata = e.Metadata,
            Embedding = null
        });
    }

    public async Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
            {
                return await _fallbackStore.Value.UpdateAsync(chunk, cancellationToken);
            }

            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var entity = await _context.VectorChunks
                    .FirstOrDefaultAsync(c => c.Id == chunk.Id, cancellationToken);

                if (entity == null)
                    return false;

                entity.Content = chunk.Content;
                entity.TokenCount = chunk.TokenCount;
                entity.Metadata = chunk.Metadata ?? new Dictionary<string, object>();

                // 벡터 업데이트
                if (chunk.Embedding != null && _sqliteVecAvailable)
                {
                    await _context.StoreVectorInVecTableAsync(chunk.Id, chunk.Embedding, cancellationToken);
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return true;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "벡터 업데이트 실패: {Id}", chunk.Id);
            throw;
        }
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
        {
            return await _fallbackStore.Value.CountAsync(cancellationToken);
        }

        return await _context.VectorChunks.CountAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return await CountAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
            {
                await _fallbackStore.Value.ClearAsync(cancellationToken);
                return;
            }

            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // vec0 테이블 클리어
                if (_sqliteVecAvailable)
                {
                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM chunk_embeddings", cancellationToken);
                }

                // 메타데이터 테이블 클리어
                _context.VectorChunks.RemoveRange(_context.VectorChunks);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("벡터 저장소 클리어 완료");
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "벡터 저장소 클리어 실패");
            throw;
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_options.UseSQLiteVec)
        {
            _sqliteVecAvailable = await _context.IsSQLiteVecAvailableAsync(cancellationToken);
        }
        else
        {
            _sqliteVecAvailable = false;
        }
    }
}