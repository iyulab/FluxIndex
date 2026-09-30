using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Application.Services.Base;
using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Exceptions;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Globalization;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// sqlite-vec 확장을 사용하는 고성능 SQLite 벡터 저장소
/// </summary>
public partial class SQLiteVecVectorStore : IVectorStore, IVectorStoreManager, INativeHybridSearch, IDisposable
{
    private static readonly char[] FtsQuerySeparators = [' ', '\t', '\n'];

    private readonly IDbContextFactory<SQLiteVecDbContext> _contextFactory;
    private readonly ILogger<SQLiteVecVectorStore> _logger;
    private readonly SQLiteVecOptions _options;
    private readonly ISQLiteVecExtensionLoader _extensionLoader;

    // 폴백용 in-memory 벡터 저장소 (sqlite-vec 실패 시 사용)
    private readonly Lazy<SQLiteVectorStore> _fallbackStore;
    private bool _sqliteVecAvailable;

    /// <summary>
    /// Whether vec0 rows are read and written: the extension is loaded AND an embedding identity is bound. A keyword-only
    /// context (no embedding service) binds none, so the store keeps chunks in <c>vector_chunks</c> and creates no vec0
    /// table; binding an identity later (an embedding backfill) creates that identity's table on the next operation.
    /// </summary>
    private bool VecTableActive => _sqliteVecAvailable && _options.EmbeddingFingerprint is not null;

    /// <summary>The vec0 table the current binding targets, or null when vec0 is off or no identity is bound.</summary>
    private string? CurrentVecTableName() =>
        _options.UseSQLiteVec && _options.EmbeddingFingerprint is not null ? _options.GetVecTableName() : null;
    private bool _initialized;
    // The effective vec0 table name captured at the last successful init. When the bound
    // fingerprint drifts on the shared options, the current name diverges from this and
    // EnsureInitializedAsync re-runs so the new table is created before any write.
    private string? _initializedTableName;
    private EmbeddingIdentity? _boundIdentity;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    // sqlite-vec's vec0 KNN ceiling (SQLITE_VEC_VEC0_K_MAX, a compile-time constant of the bundled
    // extension). A larger k is rejected as an error, not truncated.
    internal const int SqliteVecMaxK = 4096;
    // SQLite는 동시 쓰기를 지원하지 않으므로 쓰기 작업을 직렬화
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <inheritdoc />
    public EmbeddingIdentity? BoundIdentity => _boundIdentity;

    /// <inheritdoc />
    /// <remarks>The vec0 table the last initialization created (fingerprinted once an identity is bound);
    /// null before initialization and when the store fell back because sqlite-vec is unavailable.</remarks>
    public string? ResolvedStoreName => _initialized && _sqliteVecAvailable ? _initializedTableName : null;

    /// <inheritdoc />
    /// <remarks>The bound identity's dimension; after initialization without a bound identity, the configured
    /// dimension the vec0 table was created with.</remarks>
    public int? DetectedDimension =>
        _boundIdentity?.Dimension ?? (_initialized && _sqliteVecAvailable ? _options.VectorDimension : null);

    /// <summary>
    /// Binds an embedding identity to this store and configures fingerprint-based table naming.
    /// </summary>
    public void BindIdentity(EmbeddingIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (_boundIdentity is not null && _boundIdentity != identity)
        {
            throw new EmbeddingModelMismatchException(_boundIdentity, identity);
        }

        if (_boundIdentity is null)
        {
            _boundIdentity = identity;
            _options.EmbeddingFingerprint = identity.Fingerprint;
            _options.VectorDimension = identity.Dimension;
        }
    }

    /// <summary>
    /// Creates the store. Every operation opens its own <see cref="SQLiteVecDbContext"/> from
    /// <paramref name="contextFactory"/> (and loads sqlite-vec on that context's connection), so one store
    /// instance is safe for concurrent callers.
    /// </summary>
    public SQLiteVecVectorStore(
        IDbContextFactory<SQLiteVecDbContext> contextFactory,
        ILogger<SQLiteVecVectorStore> logger,
        IOptions<SQLiteVecOptions> options,
        ISQLiteVecExtensionLoader extensionLoader,
        Lazy<SQLiteVectorStore> fallbackStore)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _options = options.Value;
        _extensionLoader = extensionLoader;
        _fallbackStore = fallbackStore;
        
        // UseSQLiteVec 옵션이 false면 처음부터 폴백 사용
        _sqliteVecAvailable = _options.UseSQLiteVec;
    }

    public async Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            if (!_sqliteVecAvailable)
            {
                if (_options.FallbackToInMemoryOnError)
                {
                    return await _fallbackStore.Value.StoreAsync(chunk, cancellationToken);
                }
                else
                {
                    throw new InvalidOperationException(
                        "The sqlite-vec extension is not loaded. " +
                        "Check that the extension is installed, or enable FallbackToInMemoryOnError.");
                }
            }

            // SQLite는 동시 쓰기를 지원하지 않으므로 직렬화
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                // Honour the caller's chunk id. Minting one here and returning it instead (what this
                // did before) silently discarded the id every other IVectorStore implementation keeps,
                // so a consumer that recorded the ids it wrote — to roll back a partial write, or to
                // tie graph provenance to chunks — could never find those rows again.
                var id = chunk.EnsureId();

                await using var context = await OpenContextAsync(cancellationToken);

                using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    // 1. 메타데이터 저장 — re-storing an id is an update, not a second row. AsTracking is
                    // load-bearing: the context is NoTracking, so a plain query returns a detached instance
                    // whose edits SaveChanges would never see.
                    var existing = await context.VectorChunks
                        .AsTracking()
                        .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

                    if (existing == null)
                    {
                        context.VectorChunks.Add(new VectorChunkEntity
                        {
                            Id = id,
                            DocumentId = chunk.DocumentId,
                            ChunkIndex = chunk.ChunkIndex,
                            TotalChunks = chunk.TotalChunks,
                            Content = chunk.Content,
                            TokenCount = chunk.TokenCount,
                            Metadata = MetadataHelper.ForStorage(chunk),
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        existing.DocumentId = chunk.DocumentId;
                        existing.ChunkIndex = chunk.ChunkIndex;
                        existing.TotalChunks = chunk.TotalChunks;
                        existing.Content = chunk.Content;
                        existing.TokenCount = chunk.TokenCount;
                        existing.Metadata = MetadataHelper.ForStorage(chunk);
                    }

                    // 2. 벡터 저장 (sqlite-vec 사용) — delete + insert, so an update replaces the vector.
                    // A re-store without an embedding drops the stale vector rather than leaving one that
                    // no longer describes the row's content.
                    if (chunk.Embedding != null && VecTableActive)
                    {
                        await context.StoreVectorInVecTableAsync(id, chunk.Embedding, cancellationToken);
                    }
                    else if (existing != null && VecTableActive)
                    {
                        await context.DeleteVectorFromVecTableAsync(id, cancellationToken);
                    }

                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    LogVectorStored(_logger, id, chunk.DocumentId);
                    return id;
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (Exception ex)
        {
            LogVectorStoreFailed(_logger, ex, chunk.DocumentId);
            throw;
        }
    }

    public async Task<IEnumerable<string>> StoreBatchAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        if (chunkList.Count == 0)
            return Enumerable.Empty<string>();

        try
        {
            await EnsureInitializedAsync(cancellationToken);

            if (!_sqliteVecAvailable)
            {
                if (_options.FallbackToInMemoryOnError)
                {
                    return await _fallbackStore.Value.StoreBatchAsync(chunkList, cancellationToken);
                }
                else
                {
                    throw new InvalidOperationException(
                        "The sqlite-vec extension is not loaded. " +
                        "Check that the extension is installed, or enable FallbackToInMemoryOnError.");
                }
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
                LogLargeBatchStart(_logger, chunkList.Count, totalBatches, batchSize);
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

                    LogBatchProgress(_logger, processedCount, chunkList.Count, (double)processedCount / chunkList.Count,
                        elapsed, remaining);
                }
            }

            var totalElapsed = DateTime.UtcNow - startTime;
            var rate = totalElapsed.TotalSeconds > 0 ? ids.Count / totalElapsed.TotalSeconds : 0;

            LogBatchStoreCompleted(_logger, ids.Count, totalElapsed, rate);

            return ids;
        }
        catch (Exception ex)
        {
            LogBatchStoreFailed(_logger, ex, chunkList.Count);

            // If sqlite-vec is now disabled and fallback is available, retry via fallback
            if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
            {
                LogFallbackRetry(_logger);
                return await _fallbackStore.Value.StoreBatchAsync(chunkList, cancellationToken);
            }

            throw;
        }
    }

    private async Task<IEnumerable<string>> StoreBatchInternalAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        // SQLite는 동시 쓰기를 지원하지 않으므로 직렬화
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var context = await OpenContextAsync(cancellationToken);
            using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var ids = await WriteChunksAsync(context, chunks as IReadOnlyList<DocumentChunk> ?? chunks.ToList(), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ids;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Replaces the chunks of <paramref name="documentIds"/> in one transaction: the old rows (and their vectors) are
    /// deleted and the new ones written together, so a reader never sees the document half-replaced and an interrupted
    /// call changes nothing.
    /// </summary>
    public async Task<IReadOnlyList<string>> ReplaceDocumentsAsync(
        IReadOnlyCollection<string> documentIds,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        ArgumentNullException.ThrowIfNull(chunks);

        await EnsureInitializedAsync(cancellationToken);

        if (!_sqliteVecAvailable)
        {
            if (_options.FallbackToInMemoryOnError)
                return await ((IVectorStore)_fallbackStore.Value).ReplaceDocumentsAsync(documentIds, chunks, cancellationToken);

            throw new InvalidOperationException(
                "The sqlite-vec extension is not loaded. " +
                "Check that the extension is installed, or enable FallbackToInMemoryOnError.");
        }

        var replaced = documentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var context = await OpenContextAsync(cancellationToken);
            using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                const int idsPerQuery = 500;
                for (var offset = 0; offset < replaced.Count; offset += idsPerQuery)
                {
                    var slice = replaced.Skip(offset).Take(idsPerQuery).ToList();
                    if (VecTableActive)
                    {
                        var oldIds = await context.VectorChunks
                            .Where(c => slice.Contains(c.DocumentId))
                            .Select(c => c.Id)
                            .ToListAsync(cancellationToken);
                        foreach (var oldId in oldIds)
                            await context.DeleteVectorFromVecTableAsync(oldId, cancellationToken);
                    }

                    await context.VectorChunks
                        .Where(c => slice.Contains(c.DocumentId))
                        .ExecuteDeleteAsync(cancellationToken);
                }

                var ids = chunks.Count == 0
                    ? new List<string>()
                    : await WriteChunksAsync(context, chunks, cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                return ids;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Writes <paramref name="chunkList"/> on <paramref name="context"/>'s open transaction: rows upserted by id, vectors
    /// replaced. The caller holds the write lock and commits.
    /// </summary>
    private async Task<List<string>> WriteChunksAsync(SQLiteVecDbContext context, IReadOnlyList<DocumentChunk> chunkList, CancellationToken cancellationToken)
    {
        var ids = new List<string>(chunkList.Count);
        var entities = new List<VectorChunkEntity>(chunkList.Count);
        var vectorBatch = new List<(string Id, float[] Embedding)>();

        // 1단계: 메타데이터 엔티티 준비 (메모리 작업) + ID 할당 — the caller's id is kept (see
        // StoreCoreAsync). Within one batch the last chunk carrying an id wins, the same way
        // two sequential stores of that id would resolve.
        var vectorById = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var chunk in chunkList)
        {
            var id = chunk.EnsureId();
            ids.Add(id);

            var entity = new VectorChunkEntity
            {
                Id = id,
                DocumentId = chunk.DocumentId,
                ChunkIndex = chunk.ChunkIndex,
                TotalChunks = chunk.TotalChunks,
                Content = chunk.Content,
                TokenCount = chunk.TokenCount,
                Metadata = MetadataHelper.ForStorage(chunk),
                CreatedAt = DateTime.UtcNow
            };
            entities.Add(entity);

            if (chunk.Embedding != null && VecTableActive)
            {
                vectorById[id] = chunk.Embedding;
            }
            else
            {
                vectorById.Remove(id);
            }
        }

        // Ids already stored: their vector rows must go before the batch insert below, because
        // the vec0 table has no upsert and a second row for one chunk_id would be either a
        // constraint failure or a duplicate hit in every search.
        if (VecTableActive && entities.Count > 0)
        {
            var alreadyStored = await FindStoredIdsAsync(context, ids, cancellationToken);
            foreach (var storedId in alreadyStored)
            {
                await context.DeleteVectorFromVecTableAsync(storedId, cancellationToken);
            }
        }

        foreach (var (id, embedding) in vectorById)
        {
            vectorBatch.Add((id, embedding));
        }

        // 2단계: vector_chunks 일괄 삽입 (단일 raw SQL multi-row INSERT, upsert on Id)
        // EF Core의 Add() × N + SaveChangesAsync()는 N개의 개별 INSERT를 발생시킨다.
        // sqlite-vec의 chunk_embeddings 테이블이 이미 사용하는 multi-row VALUES 패턴을 적용.
        if (entities.Count > 0)
        {
            // 7 columns × N rows: SQLite parameter limit is 32766. 7×4000 = 28000 < limit.
            // 보수적으로 500 rows/batch로 제한하여 SQL 길이/파서 부담을 줄인다.
            const int rowsPerStatement = 500;
            for (int offset = 0; offset < entities.Count; offset += rowsPerStatement)
            {
                var batchSize = Math.Min(rowsPerStatement, entities.Count - offset);
                var valueClauses = new List<string>(batchSize);
                var parameters = new List<object>(batchSize * 8);
                int p = 0;

                for (int j = 0; j < batchSize; j++)
                {
                    var e = entities[offset + j];
                    // Metadata JSON: match EF Core's value converter (default JsonSerializerOptions)
                    var metaJson = System.Text.Json.JsonSerializer.Serialize(
                        e.Metadata,
                        (System.Text.Json.JsonSerializerOptions?)null);

                    valueClauses.Add($"({{{p}}},{{{p + 1}}},{{{p + 2}}},{{{p + 3}}},{{{p + 4}}},{{{p + 5}}},{{{p + 6}}},{{{p + 7}}})");
                    parameters.Add(e.Id);
                    parameters.Add(e.DocumentId);
                    parameters.Add(e.ChunkIndex);
                    parameters.Add((object?)e.TotalChunks ?? DBNull.Value);
                    parameters.Add(e.Content);
                    parameters.Add(e.TokenCount);
                    parameters.Add(metaJson);
                    // Pass DateTime as parameter so Microsoft.Data.Sqlite serializes it
                    // in the same TEXT format EF Core uses (ISO 8601 with fractional seconds).
                    parameters.Add(e.CreatedAt);
                    p += 8;
                }

                // Table name set via entity.ToTable("vector_chunks").
                // Column names: PascalCase per EF Core convention (no explicit HasColumnName).
                // ON CONFLICT DO UPDATE keeps re-storing an id an update: the row's content follows
                // the new write while CreatedAt stays. The FTS5 UPDATE trigger fires for the DO
                // UPDATE branch, so the keyword index follows too.
                var sql = "INSERT INTO \"vector_chunks\" " +
                          "(\"Id\", \"DocumentId\", \"ChunkIndex\", \"TotalChunks\", \"Content\", \"TokenCount\", \"Metadata\", \"CreatedAt\") " +
                          $"VALUES {string.Join(",", valueClauses)} " +
                          "ON CONFLICT(\"Id\") DO UPDATE SET " +
                          "\"DocumentId\" = excluded.\"DocumentId\", \"ChunkIndex\" = excluded.\"ChunkIndex\", " +
                          "\"TotalChunks\" = excluded.\"TotalChunks\", " +
                          "\"Content\" = excluded.\"Content\", \"TokenCount\" = excluded.\"TokenCount\", " +
                          "\"Metadata\" = excluded.\"Metadata\"";

                await context.Database.ExecuteSqlRawAsync(sql, parameters.ToArray(), cancellationToken);
                LogVectorChunksBatchInserted(_logger, batchSize);
            }
        }

        // 3단계: 벡터 배치 삽입 (단일 SQL 문으로 최적화)
        if (vectorBatch.Count != 0)
        {
            await StoreBatchVectorsAsync(context, vectorBatch, cancellationToken);
        }

        return ids;
    }

    /// <summary>
    /// The subset of <paramref name="ids"/> that already has a row in <c>vector_chunks</c>.
    /// Queried in slices so a large batch stays under SQLite's bound-parameter limit.
    /// </summary>
    private static async Task<HashSet<string>> FindStoredIdsAsync(SQLiteVecDbContext context, List<string> ids, CancellationToken cancellationToken)
    {
        var stored = new HashSet<string>(StringComparer.Ordinal);
        const int idsPerQuery = 500;
        for (var offset = 0; offset < ids.Count; offset += idsPerQuery)
        {
            var slice = ids.Skip(offset).Take(idsPerQuery).ToList();
            var found = await context.VectorChunks
                .Where(c => slice.Contains(c.Id))
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);
            stored.UnionWith(found);
        }
        return stored;
    }

    /// <summary>
    /// 벡터 배치 삽입 최적화 (단일 SQL 문으로 처리)
    /// </summary>
    private async Task StoreBatchVectorsAsync(SQLiteVecDbContext context, List<(string Id, float[] Embedding)> vectorBatch, CancellationToken cancellationToken)
    {
        if (vectorBatch.Count == 0) return;

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
                    var vectorString = "[" + string.Join(",", embedding.Select(f => f.ToString("F6", CultureInfo.InvariantCulture))) + "]";
                    valuesClauses.Add($"({{{paramIndex}}}, {{{paramIndex + 1}}})");
                    parameters.Add(id);
                    parameters.Add(vectorString);
                    paramIndex += 2;
                }

                // 배치 삽입은 새 청크에 대해서만 호출되므로 순수 INSERT 사용
                var sql = $"INSERT INTO {_options.GetVecTableName()} (chunk_id, embedding) VALUES {string.Join(", ", valuesClauses)}";
                await context.Database.ExecuteSqlRawAsync(sql, parameters.ToArray(), cancellationToken);

                LogBatchVectorInserted(_logger, batch.Count);
            }
        }
        catch (Exception ex) when (IsSqliteVecError(ex))
        {
            LogBatchVectorInsertFailed(_logger, ex);

            // Attempt self-healing: drop + recreate vec0 table — on this operation's connection, which
            // holds the open transaction.
            if (await TryRecreateVecTableAsync(context, cancellationToken))
            {
                // Retry once with fresh table
                try
                {
                    const int retryMaxBatch = 999;
                    for (int ri = 0; ri < vectorBatch.Count; ri += retryMaxBatch)
                    {
                        var retryBatch = vectorBatch.Skip(ri).Take(retryMaxBatch).ToList();
                        var retryValues = new List<string>();
                        var retryParams = new List<object>();
                        int rpi = 0;
                        foreach (var (rid, rembedding) in retryBatch)
                        {
                            var rvs = "[" + string.Join(",", rembedding.Select(f => f.ToString("F6", CultureInfo.InvariantCulture))) + "]";
                            retryValues.Add($"({{{rpi}}}, {{{rpi + 1}}})");
                            retryParams.Add(rid);
                            retryParams.Add(rvs);
                            rpi += 2;
                        }
                        var retrySql = $"INSERT INTO {_options.GetVecTableName()} (chunk_id, embedding) VALUES {string.Join(", ", retryValues)}";
                        await context.Database.ExecuteSqlRawAsync(retrySql, retryParams.ToArray(), cancellationToken);
                    }
                    LogVecTableRecovered(_logger);
                    return; // Recovery succeeded
                }
                catch (Exception retryEx)
                {
                    LogVecTableRecoveryFailed(_logger, retryEx);
                }
            }

            // Recovery failed — disable sqlite-vec for this session
            _sqliteVecAvailable = false;
            throw;
        }
        catch (Exception ex)
        {
            LogBatchVectorInsertFailed(_logger, ex);
            throw;
        }
    }

    public async Task<DocumentChunk?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
            {
                return await _fallbackStore.Value.GetAsync(id, cancellationToken);
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var chunkEntity = await context.VectorChunks
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (chunkEntity == null)
                return null;

            var chunk = new DocumentChunk
            {
                Id = chunkEntity.Id,
                DocumentId = chunkEntity.DocumentId,
                ChunkIndex = chunkEntity.ChunkIndex,
                TotalChunks = chunkEntity.TotalChunks ?? 0,
                Content = chunkEntity.Content,
                Embedding = null, // 필요시 별도 쿼리로 로드
                TokenCount = chunkEntity.TokenCount,
                Metadata = chunkEntity.Metadata
            };

            MetadataHelper.RestoreRichMetadata(chunk);
            return chunk;
        }
        catch (Exception ex)
        {
            LogVectorGetFailed(_logger, ex, id);
            throw;
        }
    }

    public async Task<IEnumerable<DocumentChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        Dictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default)
    {
        // Fail-loud contract validation BEFORE the try — an unsupported filter value must throw,
        // not trigger the in-memory fallback path.
        VectorStoreBase.ValidateFilters(filters);

        // No identity bound = no vector space (a keyword-only context): there is no vec0 table to search, and an
        // empty result would read as "nothing matched". Say what is missing instead.
        if (_options.UseSQLiteVec && _options.EmbeddingFingerprint is null)
        {
            throw new InvalidOperationException(
                "No embedding identity is bound to this sqlite-vec store, so it holds no vectors to search (a " +
                "keyword-only context). Register an embedding service, or search the keyword index.");
        }

        try
        {
            await EnsureInitializedAsync(cancellationToken);

            if (!_sqliteVecAvailable)
            {
                if (_options.FallbackToInMemoryOnError)
                {
                    return await _fallbackStore.Value.SearchAsync(queryEmbedding, topK, minScore, filters, cancellationToken);
                }
                else
                {
                    throw new InvalidOperationException(
                        "The sqlite-vec extension is not loaded. " +
                        "Check that the extension is installed, or enable FallbackToInMemoryOnError.");
                }
            }

            await using var context = await OpenContextAsync(cancellationToken);
            return await SearchWithSQLiteVecAsync(context, queryEmbedding, topK, minScore, filters, cancellationToken);
        }
        catch (Exception ex)
        {
            LogVectorSearchFailed(_logger, ex);

            // 폴백 모드 활성화
            if (_options.FallbackToInMemoryOnError)
            {
                LogFallbackRetry(_logger);
                _sqliteVecAvailable = false;
                return await _fallbackStore.Value.SearchAsync(queryEmbedding, topK, minScore, filters, cancellationToken);
            }

            throw;
        }
    }

    private async Task<IEnumerable<DocumentChunk>> SearchWithSQLiteVecAsync(
        SQLiteVecDbContext context,
        float[] queryEmbedding,
        int topK,
        float minScore,
        Dictionary<string, object>? filters,
        CancellationToken cancellationToken)
    {
        // sqlite-vec 네이티브 검색 사용
        var vectorString = "[" + string.Join(",", queryEmbedding.Select(f => f.ToString("F6", CultureInfo.InvariantCulture))) + "]";

        // Metadata lives in vector_chunks, not in the vec0 table, so the filter cannot be part of the
        // KNN. The KNN runs first, over a window over-fetched when a filter is present, and the filter
        // is applied to it in distance order. When the window comes back full and the filter still
        // cannot fill topK, matching chunks may rank past it — so the store scans every vector exactly
        // and keeps walking in distance order (see ScanAllDistancesAsync); the on-disk schema is
        // unchanged, so there is nothing to migrate. (vec0 metadata columns cannot express a scope of
        // many documents — no IN operator — and partition keys assume hundreds of vectors per value.)
        //
        // Cost: vec0 is brute-force, so the scan's distances cost about what the KNN's do, but the
        // walk that follows does not — it reads every candidate's metadata in pages and matches it in
        // memory. Measured over 6,000 chunks at topK 10 (SQLiteVecWideFilterCostTests): unfiltered
        // 6 ms, a filter allowing every document 6 ms (the window fills, no walk), a filter allowing
        // 100 of them 277 ms. So a wide scope is free here and a narrow one is not — the opposite of
        // the keyword leg — and the narrow cost grows with the store, not with the scope.
        var requestedK = filters is { Count: > 0 } ? (long)topK * 3 : topK;

        // vec0 rejects a KNN k above its compile-time ceiling ("k value in knn query too large"), and
        // that rejection fails the whole search. The window is this store's own widening, so the store
        // bounds it; what lies past the bound is reached by the exact scan below.
        var knnK = (int)Math.Min(requestedK, SqliteVecMaxK);

        // sqlite-vec vec0: CTEs and JOINs with vec0 virtual tables are unreliable
        // (silently return 0 rows). Use two-step approach:
        // Step 1: KNN search on vec0 (direct query, no CTE)
        // Step 2: Fetch metadata from vector_chunks for matched IDs
        var knnSql = $@"
            SELECT chunk_id, distance
            FROM {_options.GetVecTableName()}
            WHERE embedding MATCH @vector AND k = {knnK}";

        try
        {
            // Opened, with sqlite-vec loaded, by OpenContextAsync — the extension is per connection.
            var connection = context.Database.GetDbConnection();

            // Step 1: KNN search — retrieve all k results, filter by score later.
            // Distance filtering is deferred because the vec0 table may use L2 or cosine
            // distance depending on how it was created, and the threshold semantics differ.
            var knnResults = new List<(string ChunkId, float Distance)>();
            using (var knnCmd = connection.CreateCommand())
            {
                knnCmd.CommandText = knnSql;
                knnCmd.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@vector", vectorString));

                using var knnReader = await knnCmd.ExecuteReaderAsync(cancellationToken);
                while (await knnReader.ReadAsync(cancellationToken))
                {
                    knnResults.Add((knnReader.GetString(0), knnReader.GetFloat(1)));
                }
            }

            var windowFull = knnResults.Count >= knnK;
            if (requestedK > SqliteVecMaxK)
            {
                if (windowFull)
                {
                    LogVecKnnWindowClamped(_logger, requestedK, SqliteVecMaxK);
                }
                else
                {
                    LogVecKnnWindowClampedWithoutLoss(_logger, requestedK, SqliteVecMaxK, knnResults.Count);
                }
            }

            if (knnResults.Count == 0)
            {
                LogVecSearchCompleted(_logger, 0);
                return [];
            }

            knnResults.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            var (results, reachedScoreFloor) = await CollectInDistanceOrderAsync(
                connection, knnResults, topK, minScore, filters, cancellationToken);

            // A full window that still could not fill topK leaves candidates unexamined past it —
            // unless the walk stopped at minScore, in which case everything further is farther still.
            if (windowFull && results.Count < topK && !reachedScoreFloor)
            {
                var all = await ScanAllDistancesAsync(connection, queryEmbedding, cancellationToken);
                if (all.Count > knnResults.Count)
                {
                    LogVecExactScopedScan(_logger, knnK, results.Count, topK, all.Count);
                    (results, _) = await CollectInDistanceOrderAsync(
                        connection, all, topK, minScore, filters, cancellationToken);
                }
            }

            LogVecSearchCompleted(_logger, results.Count);
            return results;
        }
        catch (Exception ex)
        {
            LogVecNativeSearchFailed(_logger, ex);
            throw;
        }
    }

    // Metadata rows fetched per round trip while walking candidates (well under SQLite's variable limit).
    private const int CandidatePageSize = 500;

    /// <summary>
    /// Walks <paramref name="candidates"/> (ascending distance) a page at a time, fetching each page's
    /// metadata, and keeps the chunks that pass the filter and <paramref name="minScore"/> until
    /// <paramref name="topK"/> are found. Reports whether the walk stopped at the score floor — since
    /// candidates are in distance order, nothing after that point can pass it either.
    /// </summary>
    private static async Task<(List<DocumentChunk> Results, bool ReachedScoreFloor)> CollectInDistanceOrderAsync(
        System.Data.Common.DbConnection connection,
        List<(string ChunkId, float Distance)> candidates,
        int topK,
        float minScore,
        Dictionary<string, object>? filters,
        CancellationToken cancellationToken)
    {
        var results = new List<DocumentChunk>();
        // Compiled once for the whole walk: this loop is the one that can touch every vector in the
        // store, so expanding the filter per row made a narrow scope quadratic in allowed values.
        var matcher = MetadataFilterMatcher.Compile(filters);

        for (var offset = 0; offset < candidates.Count && results.Count < topK; offset += CandidatePageSize)
        {
            var page = candidates.GetRange(offset, Math.Min(CandidatePageSize, candidates.Count - offset));

            // Step 2: Fetch metadata for this page of chunk IDs
            var placeholders = string.Join(",", page.Select((_, i) => $"@id{i}"));
            var metaSql = $@"
                SELECT Id, DocumentId, ChunkIndex, Content, TokenCount, Metadata, TotalChunks
                FROM vector_chunks
                WHERE Id IN ({placeholders})";

            var metaMap = new Dictionary<string, (string DocId, int ChunkIdx, string Content, int Tokens, string Meta, int Total)>();
            using (var metaCmd = connection.CreateCommand())
            {
                metaCmd.CommandText = metaSql;
                for (int i = 0; i < page.Count; i++)
                {
                    metaCmd.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter($"@id{i}", page[i].ChunkId));
                }

                using var metaReader = await metaCmd.ExecuteReaderAsync(cancellationToken);
                while (await metaReader.ReadAsync(cancellationToken))
                {
                    metaMap[metaReader.GetString(0)] = (
                        metaReader.GetString(1),
                        metaReader.GetInt32(2),
                        metaReader.GetString(3),
                        metaReader.GetInt32(4),
                        metaReader.GetString(5),
                        metaReader.IsDBNull(6) ? 0 : metaReader.GetInt32(6));
                }
            }

            foreach (var (chunkId, distance) in page)
            {
                // Convert distance → similarity score (0–1 range, 1 = best).
                // Uses general-purpose formula that works for both L2 and cosine distance:
                //   L2:     d ∈ [0, ∞)  → score ∈ (0, 1]
                //   cosine: d ∈ [0, 2]  → score ∈ [0.33, 1]
                var score = 1.0f / (1.0f + distance);
                if (score < minScore)
                    return (results, true);

                if (!metaMap.TryGetValue(chunkId, out var meta))
                    continue;

                var metadata = MetadataValues.Deserialize(meta.Meta);

                // Metadata filter — without this the native path leaks chunks across filter
                // scope (e.g. other tenants). Same match semantics as VectorStoreBase.
                if (!matcher.Matches(meta.DocId, metadata))
                    continue;

                results.Add(new DocumentChunk
                {
                    Id = chunkId,
                    DocumentId = meta.DocId,
                    ChunkIndex = meta.ChunkIdx,
                    TotalChunks = meta.Total,
                    Content = meta.Content,
                    TokenCount = meta.Tokens,
                    Metadata = metadata,
                    Embedding = null,
                    Score = score
                });

                if (results.Count >= topK)
                    break;
            }
        }

        return (results, false);
    }

    /// <summary>
    /// Every stored vector's distance to the query, ascending — a full scan of the vec0 table with the
    /// distance function matching the table's own metric, so the values are the ones the KNN reports.
    /// </summary>
    private async Task<List<(string ChunkId, float Distance)>> ScanAllDistancesAsync(
        System.Data.Common.DbConnection connection,
        float[] queryEmbedding,
        CancellationToken cancellationToken)
    {
        var all = new List<(string ChunkId, float Distance)>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $@"
                SELECT chunk_id, {VecDistanceFunction(_options.VecTableOptions)}(embedding, @vector)
                FROM {_options.GetVecTableName()}";
            // Bound as a float32 BLOB, not as the "[1,2,...]" text the KNN uses. The KNN parses its
            // argument once; a scalar distance function is called per row, so text made every row
            // re-parse the whole query vector -- the scan's real cost, far above the match itself.
            cmd.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@vector", ToVectorBlob(queryEmbedding)));

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                all.Add((reader.GetString(0), reader.GetFloat(1)));
            }
        }

        all.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return all;
    }

    /// <summary>
    /// Packs a vector as sqlite-vec's float32 BLOB representation (little-endian, one float per
    /// element), which the distance functions accept without parsing.
    /// </summary>
    internal static byte[] ToVectorBlob(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        for (var i = 0; i < vector.Length; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(i * sizeof(float)), vector[i]);
        }

        return bytes;
    }

    /// <summary>
    /// The sqlite-vec scalar distance function for a vec0 table's <c>distance_metric</c> option —
    /// vec0's default (no option) is L2.
    /// </summary>
    internal static string VecDistanceFunction(string? vecTableOptions)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            vecTableOptions ?? string.Empty, @"distance_metric\s*=\s*(\w+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var metric = match.Success ? match.Groups[1].Value.ToLowerInvariant() : "l2";

        return metric switch
        {
            "cosine" => "vec_distance_cosine",
            "l2" => "vec_distance_l2",
            "l1" => "vec_distance_l1",
            _ => throw new NotSupportedException(
                $"vec0 distance_metric '{metric}' has no matching sqlite-vec distance function for an exact scan."),
        };
    }

    /// <summary>
    /// 하이브리드 검색 (벡터 + FTS5 텍스트 검색 + RRF 결합)
    /// </summary>
    /// <param name="queryEmbedding">쿼리 임베딩 벡터</param>
    /// <param name="textQuery">텍스트 검색 쿼리 (FTS5)</param>
    /// <param name="topK">반환할 최대 결과 수</param>
    /// <param name="minScore">최소 점수 임계값</param>
    /// <param name="vectorWeight">벡터 점수 가중치 (0.0 ~ 1.0), null이면 옵션 기본값 사용</param>
    /// <param name="filters">
    /// 두 leg 모두에 융합 <em>전에</em> 적용되는 메타데이터 필터(<see cref="SearchAsync"/>와 같은 어휘).
    /// 벡터 leg는 KNN 뒤 over-fetch 창에서, FTS5 leg는 매치 행에서 걸러진다 — 둘 다 이 저장소의 스키마 한계로
    /// 사전 필터가 아니므로 좁은 스코프는 창 포화를 겪을 수 있다(<see cref="SearchWithSQLiteVecAsync"/> 주석).
    /// </param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>RRF로 결합된 검색 결과</returns>
    public async Task<IEnumerable<HybridSearchResult>> HybridSearchAsync(
        float[] queryEmbedding,
        string textQuery,
        int topK = 10,
        float minScore = 0.0f,
        float? vectorWeight = null,
        Dictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            // One context for both legs; opened with sqlite-vec loaded when the vector leg can run.
            await using var context = await OpenContextAsync(cancellationToken);

            var weight = vectorWeight ?? _options.HybridVectorWeight;
            var textWeight = 1.0f - weight;
            var k = _options.RrfK;

            // 1. 벡터 검색 수행
            var vectorResults = new Dictionary<string, (int Rank, DocumentChunk Chunk, float VectorScore)>();
            if (VecTableActive && queryEmbedding != null && queryEmbedding.Length > 0)
            {
                var vectorChunks = await SearchWithSQLiteVecAsync(context, queryEmbedding, topK * 2, minScore, filters, cancellationToken);
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
                var ftsChunks = await SearchWithFts5Async(context, textQuery, topK * 2, filters, cancellationToken);
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

            LogHybridSearchCompleted(_logger, vectorResults.Count, ftsResults.Count, finalResults.Count);

            return finalResults;
        }
        catch (Exception ex)
        {
            LogHybridSearchFailed(_logger, ex);
            throw;
        }
    }

    /// <summary>
    /// <see cref="INativeHybridSearch"/> — exposes this store's native vec + <c>chunk_fts</c> fusion under
    /// the Core <see cref="FluxIndex.Core.Domain.Models.HybridSearchResult"/> type, so a hybrid
    /// route can prefer it over a separately-registered <c>IHybridSearchService</c> whose sparse index is
    /// not populated by ingestion-only pipelines. Maps the store-local result (RrfScore/Bm25Score) onto the
    /// Core model (FusedScore/SparseScore).
    /// </summary>
    async Task<IEnumerable<FluxIndex.Core.Domain.Models.HybridSearchResult>> INativeHybridSearch.HybridSearchAsync(
        float[] queryEmbedding,
        string textQuery,
        int topK,
        float minScore,
        float? vectorWeight,
        Dictionary<string, object>? filters,
        CancellationToken cancellationToken)
    {
        var local = await HybridSearchAsync(queryEmbedding, textQuery, topK, minScore, vectorWeight, filters, cancellationToken);
        return local.Select(r => new FluxIndex.Core.Domain.Models.HybridSearchResult
        {
            Chunk = r.Chunk,
            FusedScore = r.RrfScore,
            VectorScore = r.VectorScore ?? 0,
            SparseScore = r.Bm25Score ?? 0,
            VectorRank = r.VectorRank ?? 0,
            SparseRank = r.FtsRank ?? 0,
        }).ToList();
    }

    /// <summary>
    /// FTS5 전문 검색 수행
    /// </summary>
    private async Task<IEnumerable<(DocumentChunk Chunk, float BM25Score)>> SearchWithFts5Async(
        SQLiteVecDbContext context,
        string textQuery,
        int topK,
        Dictionary<string, object>? filters,
        CancellationToken cancellationToken)
    {
        // Metadata is a JSON column on vector_chunks, so the filter cannot be part of the MATCH. With a
        // filter the matches are read in rank order until topK pass it: a LIMIT of any fixed multiple of
        // topK would drop in-scope matches ranked past it, silently, the way the vec leg's window did.
        var hasFilter = filters is { Count: > 0 };
        var ftsMatcher = MetadataFilterMatcher.Compile(filters);

        try
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await context.Database.OpenConnectionAsync(cancellationToken);
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
                    bm25(chunk_fts) as bm25_score,
                    vc.TotalChunks
                FROM chunk_fts fts
                JOIN vector_chunks vc ON vc.rowid = fts.rowid
                WHERE chunk_fts MATCH @query
                ORDER BY bm25_score
                LIMIT @limit";

            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@query", escapedQuery));
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@limit", hasFilter ? -1 : topK));

            var results = new List<(DocumentChunk, float)>();

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken) && results.Count < topK)
            {
                var metadataJson = reader.GetString(5);
                var metadata = MetadataValues.Deserialize(metadataJson);

                if (hasFilter && !ftsMatcher.Matches(reader.GetString(1), metadata))
                    continue;

                var chunk = new DocumentChunk
                {
                    Id = reader.GetString(0),
                    DocumentId = reader.GetString(1),
                    ChunkIndex = reader.GetInt32(2),
                    TotalChunks = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                    Content = reader.GetString(3),
                    TokenCount = reader.GetInt32(4),
                    Metadata = metadata,
                    Embedding = null
                };

                // BM25 점수 (음수, 낮을수록 좋음) -> 양수로 변환
                var bm25Score = reader.GetFloat(6);
                results.Add((chunk, -bm25Score)); // 음수를 양수로 변환
            }

            LogFts5SearchCompleted(_logger, results.Count, escapedQuery);
            return results;
        }
        catch (Exception ex)
        {
            LogFts5SearchFailed(_logger, ex);
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
        var words = escaped.Split(FtsQuerySeparators, StringSplitOptions.RemoveEmptyEntries);
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
            LogFts5Disabled(_logger);
            return Enumerable.Empty<DocumentChunk>();
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var results = await SearchWithFts5Async(context, textQuery, topK, filters: null, cancellationToken);
        return results.Select(r => r.Chunk);
    }

    public async Task<bool> HasVectorsForDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.VectorChunks
                .AnyAsync(c => c.DocumentId == documentId, cancellationToken);
        }
        catch (Exception ex)
        {
            LogGetByDocumentFailed(_logger, ex, documentId);
            return false;
        }
    }

    public async Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
            {
                return await _fallbackStore.Value.GetByDocumentIdAsync(documentId, cancellationToken);
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entities = await context.VectorChunks
                .Where(c => c.DocumentId == documentId)
                .OrderBy(c => c.ChunkIndex)
                .ToListAsync(cancellationToken);

            return entities.Select(e => new DocumentChunk
            {
                Id = e.Id,
                DocumentId = e.DocumentId,
                ChunkIndex = e.ChunkIndex,
                TotalChunks = e.TotalChunks ?? 0,
                Content = e.Content,
                TokenCount = e.TokenCount,
                Metadata = e.Metadata,
                Embedding = null
            });
        }
        catch (Exception ex)
        {
            LogGetByDocumentFailed(_logger, ex, documentId);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
            {
                return await _fallbackStore.Value.DeleteAsync(id, cancellationToken);
            }

            // SQLite는 동시 쓰기를 지원하지 않으므로 직렬화
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await using var context = await OpenContextAsync(cancellationToken);
                using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    // AsTracking is load-bearing on every write path in this class: the context
                    // is registered NoTracking, so a plain query returns a detached instance and
                    // Remove() tries to attach it. If the same row is already tracked — a Store in
                    // the same scope leaves it tracked after SaveChanges — that attach throws
                    // "another instance with the same key value is already being tracked".
                    // AsTracking resolves to the instance already in the tracker instead.
                    var entity = await context.VectorChunks
                        .AsTracking()
                        .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

                    if (entity == null)
                        return false;

                    context.VectorChunks.Remove(entity);

                    // vec0 테이블에서도 삭제
                    if (VecTableActive)
                    {
                        await context.DeleteVectorFromVecTableAsync(id, cancellationToken);
                    }

                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (Exception ex)
        {
            LogVectorDeleteFailed(_logger, ex, id);
            throw;
        }
    }

    public async Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
            {
                return await _fallbackStore.Value.DeleteByDocumentIdAsync(documentId, cancellationToken);
            }

            // SQLite는 동시 쓰기를 지원하지 않으므로 직렬화
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await using var context = await OpenContextAsync(cancellationToken);
                using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    var entities = await context.VectorChunks
                        .AsTracking()
                        .Where(c => c.DocumentId == documentId)
                        .ToListAsync(cancellationToken);

                    if (entities.Count == 0)
                        return false;

                    // vec0 테이블에서 벡터들 삭제
                    if (VecTableActive)
                    {
                        foreach (var entity in entities)
                        {
                            await context.DeleteVectorFromVecTableAsync(entity.Id, cancellationToken);
                        }
                    }

                    context.VectorChunks.RemoveRange(entities);
                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (Exception ex)
        {
            LogDeleteByDocumentFailed(_logger, ex, documentId);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
        {
            return await _fallbackStore.Value.ExistsAsync(id, cancellationToken);
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.VectorChunks.AnyAsync(c => c.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> DeleteByFilterAsync(
        Dictionary<string, object> filters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.Count == 0)
            throw new ArgumentException(
                "Filter must contain at least one key/value; use ClearAsync to remove all vectors.",
                nameof(filters));

        await EnsureInitializedAsync(cancellationToken);

        if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
        {
            return await _fallbackStore.Value.DeleteByFilterAsync(filters, cancellationToken);
        }

        // SQLite는 동시 쓰기를 지원하지 않으므로 직렬화
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var context = await OpenContextAsync(cancellationToken);
            using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var deleteMatcher = MetadataFilterMatcher.Compile(filters);
                var all = await context.VectorChunks.AsTracking().ToListAsync(cancellationToken);
                var matched = all
                    .Where(e => deleteMatcher.Matches(e.DocumentId, e.Metadata))
                    .ToList();

                if (matched.Count == 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return 0;
                }

                // vec0 테이블에서 매칭된 벡터들 삭제
                if (VecTableActive)
                {
                    foreach (var entity in matched)
                    {
                        await context.DeleteVectorFromVecTableAsync(entity.Id, cancellationToken);
                    }
                }

                context.VectorChunks.RemoveRange(matched);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return matched.Count;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// One transaction over all three tables. The <c>vector_chunks</c> row is updated in place (its rowid, and with it
    /// the full-text row, stays; the FTS update trigger re-indexes the unchanged content). The vec0 row cannot change its
    /// key, so its stored vector bytes are copied to the new id and the old row deleted — the embedding is never
    /// recomputed.
    /// </remarks>
    public async Task<int> ReassignDocumentAsync(
        string oldDocumentId,
        string newDocumentId,
        IReadOnlyDictionary<string, string> chunkIdMap,
        IReadOnlyDictionary<string, object?>? metadataUpdates = null,
        CancellationToken cancellationToken = default)
    {
        DocumentReassignment.ValidateArguments(oldDocumentId, newDocumentId, chunkIdMap);
        await EnsureInitializedAsync(cancellationToken);

        if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
        {
            return await _fallbackStore.Value.ReassignDocumentAsync(
                oldDocumentId, newDocumentId, chunkIdMap, metadataUpdates, cancellationToken);
        }

        // SQLite는 동시 쓰기를 지원하지 않으므로 직렬화
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var context = await OpenContextAsync(cancellationToken);
            using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var rows = await context.VectorChunks
                    .Where(c => c.DocumentId == oldDocumentId)
                    .ToListAsync(cancellationToken);
                if (rows.Count == 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return 0;
                }

                DocumentReassignment.EnsureCovered(rows.Select(r => r.Id), chunkIdMap);

                if (await context.VectorChunks.AnyAsync(c => c.DocumentId == newDocumentId, cancellationToken))
                    throw DocumentReassignment.TargetDocumentNotEmpty(newDocumentId);

                var newIds = rows.Select(r => chunkIdMap[r.Id]).ToList();
                var taken = await context.VectorChunks
                    .Where(c => newIds.Contains(c.Id))
                    .Select(c => c.Id)
                    .ToListAsync(cancellationToken);
                if (taken.Count > 0)
                    throw DocumentReassignment.TargetChunkIdsTaken(taken);

                foreach (var row in rows)
                {
                    var newId = chunkIdMap[row.Id];
                    var metadata = DocumentReassignment.RewriteMetadata(
                        row.Metadata, oldDocumentId, newDocumentId, newId, chunkIdMap, metadataUpdates);
                    // Same serialization as the EF Core value converter on the Metadata column.
                    var metadataJson = System.Text.Json.JsonSerializer.Serialize(
                        metadata, (System.Text.Json.JsonSerializerOptions?)null);

                    await context.Database.ExecuteSqlRawAsync(
                        "UPDATE \"vector_chunks\" SET \"Id\" = {0}, \"DocumentId\" = {1}, \"Metadata\" = {2} WHERE \"Id\" = {3}",
                        new object[] { newId, newDocumentId, metadataJson, row.Id },
                        cancellationToken);

                    if (VecTableActive)
                        await context.MoveVectorInVecTablesAsync(row.Id, newId, cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return rows.Count;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await GetAsync(id, cancellationToken);
    }

    public async Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
        {
            return await _fallbackStore.Value.GetChunksByIdsAsync(ids, cancellationToken);
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.VectorChunks
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(cancellationToken);

        return entities.Select(e => new DocumentChunk
        {
            Id = e.Id,
            DocumentId = e.DocumentId,
            ChunkIndex = e.ChunkIndex,
            TotalChunks = e.TotalChunks ?? 0,
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
            await EnsureInitializedAsync(cancellationToken);

            if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
            {
                return await _fallbackStore.Value.UpdateAsync(chunk, cancellationToken);
            }

            // SQLite는 동시 쓰기를 지원하지 않으므로 직렬화
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await using var context = await OpenContextAsync(cancellationToken);
                using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    // AsTracking is load-bearing: the context is registered NoTracking, so
                    // without it the entity below is untracked, SaveChangesAsync finds no changes,
                    // and this method reports success over a row it never wrote. AsTracking also
                    // resolves to the instance already tracked from an earlier Store in the same
                    // scope, which a bare Update(entity) would reject as a duplicate key.
                    var entity = await context.VectorChunks
                        .AsTracking()
                        .FirstOrDefaultAsync(c => c.Id == chunk.Id, cancellationToken);

                    if (entity == null)
                        return false;

                    entity.Content = chunk.Content;
                    entity.TokenCount = chunk.TokenCount;
                    entity.Metadata = MetadataHelper.ForStorage(chunk);

                    // 벡터 업데이트
                    if (chunk.Embedding != null && VecTableActive)
                    {
                        await context.StoreVectorInVecTableAsync(chunk.Id, chunk.Embedding, cancellationToken);
                    }

                    // An update that changes nothing is a legitimate no-op; an update that had
                    // changes but wrote no rows is the silent failure above, so say so.
                    var hadChanges = context.ChangeTracker.HasChanges();
                    var written = await context.SaveChangesAsync(cancellationToken);
                    if (hadChanges && written == 0)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return false;
                    }

                    await transaction.CommitAsync(cancellationToken);

                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (Exception ex)
        {
            LogVectorUpdateFailed(_logger, ex, chunk.Id);
            throw;
        }
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
        {
            return await _fallbackStore.Value.CountAsync(cancellationToken);
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.VectorChunks.CountAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return await CountAsync(cancellationToken);
    }

    public async Task<int> GetDistinctDocumentCountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
        {
            return await _fallbackStore.Value.GetDistinctDocumentCountAsync(cancellationToken);
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.VectorChunks
            .Select(c => c.DocumentId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            if (!_sqliteVecAvailable && _options.FallbackToInMemoryOnError)
            {
                await _fallbackStore.Value.ClearAsync(cancellationToken);
                return;
            }

            // SQLite는 동시 쓰기를 지원하지 않으므로 직렬화
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await using var context = await OpenContextAsync(cancellationToken);
                using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    // vec0 테이블 클리어
                    if (VecTableActive)
                    {
                        var clearSql = $"DELETE FROM {_options.GetVecTableName()}";
                        await context.Database.ExecuteSqlRawAsync(clearSql, cancellationToken);
                    }

                    // 메타데이터 테이블 클리어
                    context.VectorChunks.RemoveRange(context.VectorChunks.AsTracking());
                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    LogStoreClearCompleted(_logger);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (Exception ex)
        {
            LogStoreClearFailed(_logger, ex);
            throw;
        }
    }

    private static async Task EnsureModelTablesAsync(SQLiteVecDbContext context, CancellationToken cancellationToken)
    {
        // Per owned table: creates vector_chunks even when the vec0 virtual table already exists (where
        // EnsureCreated would do nothing), and adds nullable columns an older database lacks. The
        // backfill then gives pre-column rows their real TotalChunks instead of a silent 0.
        SQLiteSchemaProvisioner.Provision(context);
        await TotalChunksBackfill.RunAsync(context, "vector_chunks", cancellationToken);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        // Fast path: already initialized against the CURRENT effective vec table. When the bound
        // fingerprint drifts on the shared options (a later BindIdentity in another scope), the
        // effective table name changes and we must re-initialize so the new vec0 table exists —
        // otherwise writes target a table that was never created ("no such table").
        if (_initialized && CurrentVecTableName() == _initializedTableName)
            return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized && CurrentVecTableName() == _initializedTableName)
                return;

            // Model tables first, unconditionally. EF's EnsureCreated is a no-op on a database that
            // already has any table, so it must run before the vec0 virtual table exists — a store
            // used without the hosted initializer (plain ServiceCollection, inline processing) used to
            // end up with the vec0 table and no vector_chunks ("no such table"). A database already
            // left in that mixed state is repaired through the relational creator.
            // A context of its own for provisioning; the flags below stay on the store.
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await EnsureModelTablesAsync(context, cancellationToken);

            if (_options.UseSQLiteVec)
            {
                // SQLite 확장은 연결 수준에서 로드되므로, 현재 연결에서 확장을 로드해야 함. The result is kept as a fact
                // of the process and the extension file; every later operation loads the extension on its own
                // connection (OpenContextAsync).
                var connection = context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await context.Database.OpenConnectionAsync(cancellationToken);
                }

                // 확장 로드 시도 (per-connection: SQLite 확장은 연결마다 로드 필요)
                _sqliteVecAvailable = await _extensionLoader.LoadExtensionAsync(
                    (Microsoft.Data.Sqlite.SqliteConnection)connection, cancellationToken);

                // Everything below needs a bound identity (the vec0 table is named after its fingerprint). Keyword-only,
                // there is none: the model tables above are the whole store.
                if (_sqliteVecAvailable && _options.EmbeddingFingerprint is not null)
                {
                    // Identity-dependent legacy migration runs here, at first bound access, because
                    // the hosted startup initializer defers it when no identity was bound yet.
                    await context.MigrateLegacyVecTableAsync(
                        (Microsoft.Data.Sqlite.SqliteConnection)connection, cancellationToken);

                    // vec0 테이블 생성 (이미 존재하면 무시)
                    await _extensionLoader.CreateVecTableAsync(
                        (Microsoft.Data.Sqlite.SqliteConnection)connection,
                        _options.GetVecTableName(),
                        _options.VectorDimension,
                        _options.VecTableOptions,
                        cancellationToken);

                    // Warm up vec0 query plan JIT so the first user batch is not cold-started.
                    // CreateVecTableAsync ensures the virtual table exists; this LIMIT 0 SELECT
                    // forces sqlite-vec to compile its query plan against the table's vec0 schema.
                    // Without this, the first user-triggered INSERT pays a 1-3 second JIT cost.
                    try
                    {
                        // GetVecTableName() returns a sanitized identifier (chunk_embeddings_{hex}) —
                        // no SQL injection risk. Build the string outside the call to satisfy EF1002.
                        var warmupSql = "SELECT count(*) FROM " + _options.GetVecTableName() + " LIMIT 0";
                        await context.Database.ExecuteSqlRawAsync(warmupSql, cancellationToken);
                        LogVecJitWarmupCompleted(_logger);
                    }
                    catch (Exception warmupEx)
                    {
                        // Warmup failure is non-fatal; first batch will pay the cold-start cost as before.
                        LogVecJitWarmupFailed(_logger, warmupEx);
                    }

                    // Non-destructive cross-fingerprint orphan scan at the one point the effective
                    // fingerprint is guaranteed bound (GetVecTableName above would have thrown otherwise).
                    // Running it here — rather than from a startup hook whose ordering vs the identity
                    // binder is not guaranteed — ensures the WARN actually fires for live orphan tables.
                    if (_options.EnableStartupCrossFingerprintOrphanReport)
                    {
                        try
                        {
                            await context.DetectCrossFingerprintOrphanTablesAsync(cancellationToken);
                        }
                        catch (Exception scanEx)
                        {
                            // Diagnostic is best-effort; failure must not block initialization.
                            LogCrossFingerprintScanFailed(_logger, scanEx);
                        }
                    }
                }
            }
            else
            {
                _sqliteVecAvailable = false;
            }

            _initializedTableName = CurrentVecTableName();
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<bool> VerifyHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            if (!_sqliteVecAvailable)
                return true; // Not using sqlite-vec — nothing to verify

            await using var context = await OpenContextAsync(cancellationToken);
            var testId = $"__health_check_{Guid.NewGuid():N}";
            var dimension = _options.VectorDimension;
            var testVector = new float[dimension];
            var vectorString = "[" + string.Join(",", testVector.Select(f => f.ToString("F6", CultureInfo.InvariantCulture))) + "]";
            var tableName = _options.GetVecTableName();

            // Test INSERT
            var insertSql = $"INSERT INTO {tableName} (chunk_id, embedding) VALUES ({{0}}, {{1}})";
            await context.Database.ExecuteSqlRawAsync(insertSql, [testId, vectorString], cancellationToken);

            // Cleanup
            var deleteSql = $"DELETE FROM {tableName} WHERE chunk_id = {{0}}";
            await context.Database.ExecuteSqlRawAsync(deleteSql, [testId], cancellationToken);

            LogHealthCheckPassed(_logger);
            return true;
        }
        catch (Exception ex) when (IsSqliteVecError(ex))
        {
            LogHealthCheckFailed(_logger, ex);

            // Attempt recovery (the failed operation's context is already disposed here)
            await using var recoveryContext = await OpenContextAsync(cancellationToken);
            if (await TryRecreateVecTableAsync(recoveryContext, cancellationToken))
            {
                LogVecTableRecovered(_logger);
                return false; // Recovered but data lost — caller should re-index
            }

            _sqliteVecAvailable = false;
            return false;
        }
        catch (Exception ex)
        {
            LogHealthCheckFailed(_logger, ex);
            return false;
        }
    }

    /// <summary>
    /// Opens a context for one operation. While sqlite-vec is in use the connection is opened here and the
    /// extension loaded on it: an extension belongs to one connection, and each context gets its own
    /// (pooled) connection, so vec0 SQL run without this would fail — or, on the delete paths that
    /// tolerate a vec0 error, leave the vector behind.
    /// </summary>
    private async Task<SQLiteVecDbContext> OpenContextAsync(CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        if (!_sqliteVecAvailable)
            return context;

        try
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
            await _extensionLoader.LoadExtensionAsync(
                (Microsoft.Data.Sqlite.SqliteConnection)context.Database.GetDbConnection(), cancellationToken);
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Disposes the initialization and write lock semaphores.
    /// </summary>
    public void Dispose()
    {
        _initLock.Dispose();
        _writeLock.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Checks if an exception is a sqlite-vec internal error.
    /// </summary>
    private static bool IsSqliteVecError(Exception ex)
    {
        return ex is Microsoft.Data.Sqlite.SqliteException sqliteEx
            && sqliteEx.SqliteErrorCode == 1
            && ex.Message.Contains("sqlite-vec error", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Attempts to drop and recreate the vec0 virtual table.
    /// </summary>
    private async Task<bool> TryRecreateVecTableAsync(SQLiteVecDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            var connection = (Microsoft.Data.Sqlite.SqliteConnection)context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await context.Database.OpenConnectionAsync(cancellationToken);

            return await _extensionLoader.RecreateVecTableAsync(
                connection,
                _options.GetVecTableName(),
                _options.VectorDimension,
                _options.VecTableOptions,
                cancellationToken);
        }
        catch (Exception ex)
        {
            LogVecTableRecoveryFailed(_logger, ex);
            return false;
        }
    }

}
