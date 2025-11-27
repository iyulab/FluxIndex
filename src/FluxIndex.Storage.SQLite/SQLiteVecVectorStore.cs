using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
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

            var ids = new List<string>(chunkList.Count);
            var batchSize = Math.Min(_options.MaxBatchSize, chunkList.Count);
            var totalBatches = (chunkList.Count + batchSize - 1) / batchSize;
            var processedCount = 0;
            var startTime = DateTime.UtcNow;

            // 대용량 작업 시 진행 로깅
            var isLargeBatch = chunkList.Count > 1000;
            if (isLargeBatch)
            {
                _logger.LogInformation(
                    "대용량 배치 저장 시작: 총 {TotalCount}개 항목, {BatchCount}개 배치 (배치당 {BatchSize}개)",
                    chunkList.Count, totalBatches, batchSize);
            }

            for (int i = 0; i < chunkList.Count; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = chunkList.Skip(i).Take(batchSize);
                var batchIds = await StoreBatchInternalAsync(batch, cancellationToken);
                ids.AddRange(batchIds);
                processedCount += batchIds.Count();

                // 진행 상황 로깅
                if (_options.BatchProgressLogInterval > 0 &&
                    processedCount % _options.BatchProgressLogInterval == 0 &&
                    processedCount < chunkList.Count)
                {
                    var elapsed = DateTime.UtcNow - startTime;
                    var remaining = TimeSpan.FromTicks(
                        (long)(elapsed.Ticks * (chunkList.Count - processedCount) / (double)processedCount));

                    _logger.LogDebug(
                        "배치 저장 진행: {Processed}/{Total} ({Percent:P0}), 경과: {Elapsed:mm\\:ss}, 예상 남은 시간: {Remaining:mm\\:ss}",
                        processedCount, chunkList.Count, (double)processedCount / chunkList.Count,
                        elapsed, remaining);
                }
            }

            var totalElapsed = DateTime.UtcNow - startTime;
            var rate = totalElapsed.TotalSeconds > 0 ? ids.Count / totalElapsed.TotalSeconds : 0;

            _logger.LogInformation(
                "배치 벡터 저장 완료: {Count}개 항목, 소요 시간: {Elapsed:mm\\:ss\\.fff}, 처리율: {Rate:F0}개/초",
                ids.Count, totalElapsed, rate);

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
            var vectorBatch = new List<(string Id, float[] Embedding)>();

            // 1단계: 메타데이터 엔티티 준비 (메모리 작업)
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

                // 벡터 배치에 추가 (아직 DB 삽입 안 함)
                if (chunk.Embedding != null && _sqliteVecAvailable)
                {
                    vectorBatch.Add((id, chunk.Embedding));
                }
            }

            // 2단계: 메타데이터 일괄 저장 (EF Core 최적화된 배치 INSERT)
            await _context.SaveChangesAsync(cancellationToken);

            // 3단계: 벡터 배치 삽입 (단일 SQL 문으로 최적화)
            if (vectorBatch.Any())
            {
                await StoreBatchVectorsAsync(vectorBatch, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            return ids;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// 벡터 배치 삽입 최적화 (단일 SQL 문으로 처리)
    /// </summary>
    private async Task StoreBatchVectorsAsync(List<(string Id, float[] Embedding)> vectorBatch, CancellationToken cancellationToken)
    {
        if (!vectorBatch.Any()) return;

        try
        {
            // VALUES clause 구성 (최대 999개 제한 - SQLite 제약)
            const int maxBatchSize = 999;
            for (int i = 0; i < vectorBatch.Count; i += maxBatchSize)
            {
                var batch = vectorBatch.Skip(i).Take(maxBatchSize).ToList();
                var valuesClauses = new List<string>();
                var parameters = new List<object>();

                int paramIndex = 0;
                foreach (var (id, embedding) in batch)
                {
                    var vectorString = "[" + string.Join(",", embedding.Select(f => f.ToString("F6"))) + "]";
                    valuesClauses.Add($"({{{paramIndex}}}, {{{paramIndex + 1}}})");
                    parameters.Add(id);
                    parameters.Add(vectorString);
                    paramIndex += 2;
                }

                var sql = $"INSERT OR REPLACE INTO chunk_embeddings (chunk_id, embedding) VALUES {string.Join(", ", valuesClauses)}";
                await _context.Database.ExecuteSqlRawAsync(sql, parameters.ToArray(), cancellationToken);

                _logger.LogDebug("배치 벡터 삽입 완료: {Count}개", batch.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "배치 벡터 삽입 실패");
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

    /// <summary>
    /// 하이브리드 검색 (벡터 + FTS5 텍스트 검색 + RRF 결합)
    /// </summary>
    /// <param name="queryEmbedding">쿼리 임베딩 벡터</param>
    /// <param name="textQuery">텍스트 검색 쿼리 (FTS5)</param>
    /// <param name="topK">반환할 최대 결과 수</param>
    /// <param name="minScore">최소 점수 임계값</param>
    /// <param name="vectorWeight">벡터 점수 가중치 (0.0 ~ 1.0), null이면 옵션 기본값 사용</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>RRF로 결합된 검색 결과</returns>
    public async Task<IEnumerable<HybridSearchResult>> HybridSearchAsync(
        float[] queryEmbedding,
        string textQuery,
        int topK = 10,
        float minScore = 0.0f,
        float? vectorWeight = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            var weight = vectorWeight ?? _options.HybridVectorWeight;
            var textWeight = 1.0f - weight;
            var k = _options.RrfK;

            // 1. 벡터 검색 수행
            var vectorResults = new Dictionary<string, (int Rank, DocumentChunk Chunk, float VectorScore)>();
            if (_sqliteVecAvailable && queryEmbedding != null && queryEmbedding.Length > 0)
            {
                var vectorChunks = await SearchWithSQLiteVecAsync(queryEmbedding, topK * 2, minScore, cancellationToken);
                int rank = 1;
                foreach (var chunk in vectorChunks)
                {
                    vectorResults[chunk.Id] = (rank++, chunk, 1.0f / rank); // 순위 기반 점수
                }
            }

            // 2. FTS5 텍스트 검색 수행
            var ftsResults = new Dictionary<string, (int Rank, DocumentChunk Chunk, float BM25Score)>();
            if (_options.UseFts5 && !string.IsNullOrWhiteSpace(textQuery))
            {
                var ftsChunks = await SearchWithFts5Async(textQuery, topK * 2, cancellationToken);
                int rank = 1;
                foreach (var (chunk, bm25Score) in ftsChunks)
                {
                    ftsResults[chunk.Id] = (rank++, chunk, bm25Score);
                }
            }

            // 3. RRF (Reciprocal Rank Fusion) 결합
            var allIds = vectorResults.Keys.Union(ftsResults.Keys).ToHashSet();
            var combinedResults = new List<HybridSearchResult>();

            foreach (var id in allIds)
            {
                var vectorRank = vectorResults.TryGetValue(id, out var vr) ? vr.Rank : int.MaxValue;
                var ftsRank = ftsResults.TryGetValue(id, out var fr) ? fr.Rank : int.MaxValue;

                // RRF 점수 계산: score = w1 / (k + rank1) + w2 / (k + rank2)
                var rrfScore = (weight / (k + vectorRank)) + (textWeight / (k + ftsRank));

                var chunk = vectorResults.TryGetValue(id, out var vc) ? vc.Chunk :
                           ftsResults.TryGetValue(id, out var fc) ? fc.Chunk : null;

                if (chunk != null)
                {
                    combinedResults.Add(new HybridSearchResult
                    {
                        Chunk = chunk,
                        RrfScore = (float)rrfScore,
                        VectorRank = vectorRank == int.MaxValue ? null : vectorRank,
                        FtsRank = ftsRank == int.MaxValue ? null : ftsRank,
                        VectorScore = vectorResults.TryGetValue(id, out var vs) ? vs.VectorScore : null,
                        Bm25Score = ftsResults.TryGetValue(id, out var fs) ? fs.BM25Score : null
                    });
                }
            }

            // 4. RRF 점수로 정렬하여 반환
            var finalResults = combinedResults
                .OrderByDescending(r => r.RrfScore)
                .Take(topK)
                .ToList();

            _logger.LogDebug(
                "하이브리드 검색 완료: 벡터={VectorCount}개, FTS={FtsCount}개, 결합={CombinedCount}개",
                vectorResults.Count, ftsResults.Count, finalResults.Count);

            return finalResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "하이브리드 검색 실패");
            throw;
        }
    }

    /// <summary>
    /// FTS5 전문 검색 수행
    /// </summary>
    private async Task<IEnumerable<(DocumentChunk Chunk, float BM25Score)>> SearchWithFts5Async(
        string textQuery,
        int topK,
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = _context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            // FTS5 쿼리 이스케이프 (특수문자 처리)
            var escapedQuery = EscapeFts5Query(textQuery);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT
                    vc.Id,
                    vc.DocumentId,
                    vc.ChunkIndex,
                    vc.Content,
                    vc.TokenCount,
                    vc.Metadata,
                    bm25(chunk_fts) as bm25_score
                FROM chunk_fts fts
                JOIN vector_chunks vc ON vc.rowid = fts.rowid
                WHERE chunk_fts MATCH @query
                ORDER BY bm25_score
                LIMIT @topK";

            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@query", escapedQuery));
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@topK", topK));

            var results = new List<(DocumentChunk, float)>();

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var metadataJson = reader.GetString(5);
                var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson)
                    ?? new Dictionary<string, object>();

                var chunk = new DocumentChunk
                {
                    Id = reader.GetString(0),
                    DocumentId = reader.GetString(1),
                    ChunkIndex = reader.GetInt32(2),
                    Content = reader.GetString(3),
                    TokenCount = reader.GetInt32(4),
                    Metadata = metadata,
                    Embedding = null
                };

                // BM25 점수 (음수, 낮을수록 좋음) -> 양수로 변환
                var bm25Score = reader.GetFloat(6);
                results.Add((chunk, -bm25Score)); // 음수를 양수로 변환
            }

            _logger.LogDebug("FTS5 검색 완료: {Count}개 결과, 쿼리: {Query}", results.Count, escapedQuery);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FTS5 검색 실패, 빈 결과 반환");
            return Enumerable.Empty<(DocumentChunk, float)>();
        }
    }

    /// <summary>
    /// FTS5 쿼리 문자열 이스케이프
    /// </summary>
    private static string EscapeFts5Query(string query)
    {
        // FTS5 특수 문자 이스케이프
        var escaped = query
            .Replace("\"", "\"\"") // 큰따옴표 이스케이프
            .Replace("*", "") // 와일드카드 제거 (안전성)
            .Replace(":", " ") // 필드 구분자 제거
            .Trim();

        // 각 단어를 따옴표로 감싸서 특수문자 문제 방지
        var words = escaped.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "*"; // 빈 쿼리 처리

        return string.Join(" OR ", words.Select(w => $"\"{w}\""));
    }

    /// <summary>
    /// 텍스트만으로 검색 (벡터 없이 FTS5만 사용)
    /// </summary>
    public async Task<IEnumerable<DocumentChunk>> TextSearchAsync(
        string textQuery,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        if (!_options.UseFts5)
        {
            _logger.LogWarning("FTS5가 비활성화되어 있어 텍스트 검색을 수행할 수 없습니다.");
            return Enumerable.Empty<DocumentChunk>();
        }

        var results = await SearchWithFts5Async(textQuery, topK, cancellationToken);
        return results.Select(r => r.Chunk);
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

/// <summary>
/// 하이브리드 검색 결과 (벡터 + FTS5 결합)
/// </summary>
public class HybridSearchResult
{
    /// <summary>
    /// 검색된 문서 청크
    /// </summary>
    public DocumentChunk Chunk { get; set; } = null!;

    /// <summary>
    /// RRF (Reciprocal Rank Fusion) 결합 점수
    /// </summary>
    public float RrfScore { get; set; }

    /// <summary>
    /// 벡터 검색에서의 순위 (null이면 벡터 검색 결과에 포함되지 않음)
    /// </summary>
    public int? VectorRank { get; set; }

    /// <summary>
    /// FTS5 검색에서의 순위 (null이면 텍스트 검색 결과에 포함되지 않음)
    /// </summary>
    public int? FtsRank { get; set; }

    /// <summary>
    /// 벡터 유사도 점수 (null이면 벡터 검색 결과에 포함되지 않음)
    /// </summary>
    public float? VectorScore { get; set; }

    /// <summary>
    /// BM25 점수 (null이면 텍스트 검색 결과에 포함되지 않음)
    /// </summary>
    public float? Bm25Score { get; set; }

    /// <summary>
    /// 검색 결과가 벡터 검색에서 찾아졌는지 여부
    /// </summary>
    public bool FoundInVectorSearch => VectorRank.HasValue;

    /// <summary>
    /// 검색 결과가 텍스트 검색에서 찾아졌는지 여부
    /// </summary>
    public bool FoundInTextSearch => FtsRank.HasValue;

    /// <summary>
    /// 양쪽 검색에서 모두 찾아졌는지 여부
    /// </summary>
    public bool FoundInBoth => FoundInVectorSearch && FoundInTextSearch;
}