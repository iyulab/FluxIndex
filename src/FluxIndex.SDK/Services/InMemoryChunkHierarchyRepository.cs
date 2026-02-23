using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.SDK.Services;

/// <summary>
/// Interface for chunk hierarchy repositories that support file persistence
/// </summary>
public interface IPersistableHierarchyRepository
{
    /// <summary>
    /// Saves the hierarchy data to a file
    /// </summary>
    Task SaveToFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the hierarchy data from a file
    /// </summary>
    Task LoadFromFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the persistence file path if configured
    /// </summary>
    string? PersistencePath { get; }

    /// <summary>
    /// Gets whether auto-save is enabled
    /// </summary>
    bool AutoSaveEnabled { get; }
}

/// <summary>
/// In-memory chunk hierarchy repository (SDK) with optional file persistence
/// </summary>
public partial class InMemoryChunkHierarchyRepository : IChunkHierarchyRepository, IPersistableHierarchyRepository, IDisposable
{
    private static readonly JsonSerializerOptions s_persistenceJsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly ConcurrentDictionary<string, ChunkHierarchy> _hierarchies = new();
    private readonly ConcurrentDictionary<string, ChunkRelationshipExtended> _relationships = new();
    private readonly ILogger<InMemoryChunkHierarchyRepository> _logger;
    private readonly string? _persistencePath;
    private readonly bool _autoSave;
    private readonly SemaphoreSlim _persistenceLock = new(1, 1);

    /// <summary>
    /// Creates a new in-memory chunk hierarchy repository without persistence
    /// </summary>
    public InMemoryChunkHierarchyRepository(ILogger<InMemoryChunkHierarchyRepository>? logger = null)
    {
        _logger = logger ?? NullLogger<InMemoryChunkHierarchyRepository>.Instance;
        _persistencePath = null;
        _autoSave = false;
    }

    /// <summary>
    /// Creates a new in-memory chunk hierarchy repository with optional file persistence
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="persistencePath">Path to the persistence file (null for no persistence)</param>
    /// <param name="autoSave">If true, automatically saves after each modification</param>
    /// <param name="loadExisting">If true and file exists, loads data on construction</param>
    public InMemoryChunkHierarchyRepository(
        ILogger<InMemoryChunkHierarchyRepository>? logger,
        string? persistencePath,
        bool autoSave = false,
        bool loadExisting = true)
    {
        _logger = logger ?? NullLogger<InMemoryChunkHierarchyRepository>.Instance;
        _persistencePath = persistencePath;
        _autoSave = autoSave;

        if (loadExisting && !string.IsNullOrEmpty(persistencePath) && File.Exists(persistencePath))
        {
            LoadFromFileAsync(persistencePath).GetAwaiter().GetResult();
        }
    }

    /// <inheritdoc />
    public string? PersistencePath => _persistencePath;

    /// <inheritdoc />
    public bool AutoSaveEnabled => _autoSave;

    /// <summary>
    /// Get chunk hierarchy info
    /// </summary>
    public Task<ChunkHierarchy?> GetHierarchyAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chunkId))
            throw new ArgumentException("Chunk ID cannot be empty.", nameof(chunkId));

        _hierarchies.TryGetValue(chunkId, out var hierarchy);
        return Task.FromResult(hierarchy);
    }

    /// <summary>
    /// Save chunk hierarchy info
    /// </summary>
    public async Task SaveHierarchyAsync(ChunkHierarchy hierarchy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);

        hierarchy.UpdatedAt = DateTime.UtcNow;
        _hierarchies.AddOrUpdate(hierarchy.ChunkId, hierarchy, (key, existing) =>
        {
            // Update existing hierarchy info
            existing.ParentChunkId = hierarchy.ParentChunkId;
            existing.ChildChunkIds = hierarchy.ChildChunkIds;
            existing.HierarchyLevel = hierarchy.HierarchyLevel;
            existing.RecommendedWindowSize = hierarchy.RecommendedWindowSize;
            existing.Boundary = hierarchy.Boundary;
            existing.Metadata = hierarchy.Metadata;
            existing.UpdatedAt = DateTime.UtcNow;
            return existing;
        });

        LogChunkHierarchySaved(_logger, hierarchy.ChunkId);

        await AutoSaveIfEnabledAsync(cancellationToken);
    }

    /// <summary>
    /// Get all children of a parent chunk
    /// </summary>
    public Task<IReadOnlyList<ChunkHierarchy>> GetChildrenAsync(string parentChunkId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentChunkId))
            throw new ArgumentException("Parent chunk ID cannot be empty.", nameof(parentChunkId));

        var children = _hierarchies.Values
            .Where(h => h.ParentChunkId == parentChunkId)
            .OrderBy(h => h.HierarchyLevel)
            .ToList();

        return Task.FromResult<IReadOnlyList<ChunkHierarchy>>(children);
    }

    /// <summary>
    /// Get all chunks at a specific level
    /// </summary>
    public Task<IReadOnlyList<ChunkHierarchy>> GetChunksByLevelAsync(string documentId, int level, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty.", nameof(documentId));

        if (level < 0)
            throw new ArgumentException("Hierarchy level must be 0 or greater.", nameof(level));

        // Simple implementation: assumes ChunkId contains documentId
        var chunks = _hierarchies.Values
            .Where(h => h.ChunkId.StartsWith(documentId, StringComparison.Ordinal) && h.HierarchyLevel == level)
            .OrderBy(h => h.Boundary.StartPosition)
            .ToList();

        return Task.FromResult<IReadOnlyList<ChunkHierarchy>>(chunks);
    }

    /// <summary>
    /// Save chunk relationship
    /// </summary>
    public async Task SaveRelationshipAsync(ChunkRelationshipExtended relationship, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relationship);

        _relationships.AddOrUpdate(relationship.Id, relationship, (key, existing) =>
        {
            // Update existing relationship info
            existing.SourceChunkId = relationship.SourceChunkId;
            existing.TargetChunkId = relationship.TargetChunkId;
            existing.Type = relationship.Type;
            existing.Strength = relationship.Strength;
            existing.Direction = relationship.Direction;
            existing.Description = relationship.Description;
            existing.Metadata = relationship.Metadata;
            return existing;
        });

        LogChunkRelationshipSaved(_logger, relationship.Id);

        await AutoSaveIfEnabledAsync(cancellationToken);
    }

    /// <summary>
    /// Get chunk relationships
    /// </summary>
    public Task<IReadOnlyList<ChunkRelationshipExtended>> GetRelationshipsAsync(
        string chunkId,
        IEnumerable<RelationshipType>? relationshipTypes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chunkId))
            throw new ArgumentException("Chunk ID cannot be empty.", nameof(chunkId));

        var query = _relationships.Values
            .Where(r => r.SourceChunkId == chunkId || r.TargetChunkId == chunkId);

        if (relationshipTypes != null)
        {
            var typesList = relationshipTypes.ToList();
            if (typesList.Count > 0)
            {
                query = query.Where(r => typesList.Contains(r.Type));
            }
        }

        var relationships = query
            .OrderByDescending(r => r.Strength)
            .ToList();

        return Task.FromResult<IReadOnlyList<ChunkRelationshipExtended>>(relationships);
    }

    /// <summary>
    /// Get hierarchy statistics
    /// </summary>
    public Task<HierarchyStatistics> GetHierarchyStatisticsAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty.", nameof(documentId));

        // Get all hierarchy info for the document
        var hierarchies = _hierarchies.Values
            .Where(h => h.ChunkId.StartsWith(documentId, StringComparison.Ordinal))
            .ToList();

        if (hierarchies.Count == 0)
        {
            return Task.FromResult(new HierarchyStatistics
            {
                TotalChunks = 0,
                MaxDepth = 0,
                AverageBranchingFactor = 0,
                OrphanChunks = 0,
                LeafChunks = 0,
                LevelDistribution = new Dictionary<int, int>(),
                RelationshipStatistics = new Dictionary<RelationshipType, RelationshipStats>()
            });
        }

        // Calculate basic statistics
        var totalChunks = hierarchies.Count;
        var maxDepth = hierarchies.Max(h => h.Metadata.Depth);
        var orphanChunks = hierarchies.Count(h => h.ParentChunkId == null);
        var leafChunks = hierarchies.Count(h => h.ChildChunkIds.Count == 0);

        // Level distribution
        var levelDistribution = hierarchies
            .GroupBy(h => h.HierarchyLevel)
            .ToDictionary(g => g.Key, g => g.Count());

        // Calculate average branching factor
        var parentChunks = hierarchies.Where(h => h.ChildChunkIds.Count > 0).ToList();
        var averageBranchingFactor = parentChunks.Count != 0
            ? parentChunks.Average(h => h.ChildChunkIds.Count)
            : 0.0;

        // Get relationship statistics
        var documentChunkIds = hierarchies.Select(h => h.ChunkId).ToHashSet();
        var relationships = _relationships.Values
            .Where(r => documentChunkIds.Contains(r.SourceChunkId))
            .ToList();

        var relationshipStats = relationships
            .GroupBy(r => r.Type)
            .ToDictionary(
                g => g.Key,
                g => new RelationshipStats
                {
                    Count = g.Count(),
                    AverageStrength = g.Average(r => r.Strength),
                    MaxStrength = g.Max(r => r.Strength),
                    MinStrength = g.Min(r => r.Strength)
                });

        return Task.FromResult(new HierarchyStatistics
        {
            TotalChunks = totalChunks,
            MaxDepth = maxDepth,
            AverageBranchingFactor = averageBranchingFactor,
            OrphanChunks = orphanChunks,
            LeafChunks = leafChunks,
            LevelDistribution = levelDistribution,
            RelationshipStatistics = relationshipStats
        });
    }

    #region Persistence Methods

    /// <inheritdoc />
    public async Task SaveToFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await _persistenceLock.WaitAsync(cancellationToken);
        try
        {
            LogSavingHierarchyData(_logger, filePath);

            var data = new HierarchyRepositoryData
            {
                Version = 1,
                SavedAt = DateTime.UtcNow,
                Hierarchies = _hierarchies.Values.Select(h => new ChunkHierarchySerializable
                {
                    ChunkId = h.ChunkId,
                    ParentChunkId = h.ParentChunkId,
                    ChildChunkIds = h.ChildChunkIds.ToList(),
                    HierarchyLevel = h.HierarchyLevel,
                    RecommendedWindowSize = h.RecommendedWindowSize,
                    BoundaryStartPosition = h.Boundary.StartPosition,
                    BoundaryEndPosition = h.Boundary.EndPosition,
                    BoundaryType = h.Boundary.Type.ToString(),
                    MetadataDepth = h.Metadata.Depth,
                    MetadataDescendantCount = h.Metadata.DescendantCount,
                    MetadataSiblingCount = h.Metadata.SiblingCount,
                    MetadataHierarchyWeight = h.Metadata.HierarchyWeight,
                    CreatedAt = h.CreatedAt,
                    UpdatedAt = h.UpdatedAt
                }).ToList(),
                Relationships = _relationships.Values.Select(r => new ChunkRelationshipSerializable
                {
                    Id = r.Id,
                    SourceChunkId = r.SourceChunkId,
                    TargetChunkId = r.TargetChunkId,
                    Type = r.Type.ToString(),
                    Strength = r.Strength,
                    Direction = r.Direction.ToString(),
                    Description = r.Description
                }).ToList()
            };

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, data, s_persistenceJsonOptions, cancellationToken);

            LogHierarchyDataSaved(_logger, _hierarchies.Count, _relationships.Count);
        }
        finally
        {
            _persistenceLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task LoadFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Hierarchy persistence file not found: {filePath}");
        }

        await _persistenceLock.WaitAsync(cancellationToken);
        try
        {
            LogLoadingHierarchyData(_logger, filePath);

            await using var stream = File.OpenRead(filePath);
            var data = await JsonSerializer.DeserializeAsync<HierarchyRepositoryData>(stream, cancellationToken: cancellationToken);

            if (data == null)
            {
                throw new InvalidDataException("Invalid hierarchy data format");
            }

            _hierarchies.Clear();
            _relationships.Clear();

            // Restore hierarchies
            foreach (var h in data.Hierarchies)
            {
                var boundaryType = Enum.TryParse<BoundaryType>(h.BoundaryType, out var bt) ? bt : BoundaryType.Sentence;

                var hierarchy = new ChunkHierarchy
                {
                    ChunkId = h.ChunkId,
                    ParentChunkId = h.ParentChunkId,
                    ChildChunkIds = h.ChildChunkIds,
                    HierarchyLevel = h.HierarchyLevel,
                    RecommendedWindowSize = h.RecommendedWindowSize,
                    Boundary = new ChunkBoundary
                    {
                        StartPosition = h.BoundaryStartPosition,
                        EndPosition = h.BoundaryEndPosition,
                        Type = boundaryType
                    },
                    Metadata = new HierarchyMetadata
                    {
                        Depth = h.MetadataDepth,
                        DescendantCount = h.MetadataDescendantCount,
                        SiblingCount = h.MetadataSiblingCount,
                        HierarchyWeight = h.MetadataHierarchyWeight
                    },
                    CreatedAt = h.CreatedAt,
                    UpdatedAt = h.UpdatedAt
                };

                _hierarchies[h.ChunkId] = hierarchy;
            }

            // Restore relationships
            foreach (var r in data.Relationships)
            {
                var relType = Enum.TryParse<RelationshipType>(r.Type, out var rt) ? rt : RelationshipType.Semantic;
                var direction = Enum.TryParse<RelationshipDirection>(r.Direction, out var rd) ? rd : RelationshipDirection.Bidirectional;

                var relationship = new ChunkRelationshipExtended
                {
                    Id = r.Id,
                    SourceChunkId = r.SourceChunkId,
                    TargetChunkId = r.TargetChunkId,
                    Type = relType,
                    Strength = r.Strength,
                    Direction = direction,
                    Description = r.Description ?? string.Empty
                };

                _relationships[r.Id] = relationship;
            }

            LogHierarchyDataLoaded(_logger, _hierarchies.Count, _relationships.Count);
        }
        finally
        {
            _persistenceLock.Release();
        }
    }

    private async Task AutoSaveIfEnabledAsync(CancellationToken cancellationToken)
    {
        if (_autoSave && !string.IsNullOrEmpty(_persistencePath))
        {
            await SaveToFileAsync(_persistencePath, cancellationToken);
        }
    }

    #endregion

    /// <summary>
    /// Clear all hierarchy info (for testing)
    /// </summary>
    public void Clear()
    {
        _hierarchies.Clear();
        _relationships.Clear();
        LogHierarchyCleared(_logger);
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Chunk hierarchy saved: {ChunkId}")]
    private static partial void LogChunkHierarchySaved(ILogger logger, string chunkId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Chunk relationship saved: {RelationshipId}")]
    private static partial void LogChunkRelationshipSaved(ILogger logger, string relationshipId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saving hierarchy data to {FilePath}")]
    private static partial void LogSavingHierarchyData(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hierarchy data saved successfully: {HierarchyCount} hierarchies, {RelationshipCount} relationships")]
    private static partial void LogHierarchyDataSaved(ILogger logger, int hierarchyCount, int relationshipCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loading hierarchy data from {FilePath}")]
    private static partial void LogLoadingHierarchyData(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hierarchy data loaded successfully: {HierarchyCount} hierarchies, {RelationshipCount} relationships")]
    private static partial void LogHierarchyDataLoaded(ILogger logger, int hierarchyCount, int relationshipCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "All hierarchy info has been cleared.")]
    private static partial void LogHierarchyCleared(ILogger logger);

    #endregion

    /// <summary>
    /// Get current hierarchy count
    /// </summary>
    public int GetHierarchyCount() => _hierarchies.Count;

    /// <summary>
    /// Get current relationship count
    /// </summary>
    public int GetRelationshipCount() => _relationships.Count;

    /// <summary>
    /// Disposes the persistence lock semaphore.
    /// </summary>
    public void Dispose()
    {
        _persistenceLock.Dispose();
        GC.SuppressFinalize(this);
    }
}

#region Persistence Data Classes

internal sealed class HierarchyRepositoryData
{
    public int Version { get; set; }
    public DateTime SavedAt { get; set; }
    public List<ChunkHierarchySerializable> Hierarchies { get; set; } = new();
    public List<ChunkRelationshipSerializable> Relationships { get; set; } = new();
}

internal sealed class ChunkHierarchySerializable
{
    public string ChunkId { get; set; } = string.Empty;
    public string? ParentChunkId { get; set; }
    public List<string> ChildChunkIds { get; set; } = new();
    public int HierarchyLevel { get; set; }
    public int RecommendedWindowSize { get; set; }
    public int BoundaryStartPosition { get; set; }
    public int BoundaryEndPosition { get; set; }
    public string BoundaryType { get; set; } = "Sentence";
    public int MetadataDepth { get; set; }
    public int MetadataDescendantCount { get; set; }
    public int MetadataSiblingCount { get; set; }
    public double MetadataHierarchyWeight { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

internal sealed class ChunkRelationshipSerializable
{
    public string Id { get; set; } = string.Empty;
    public string SourceChunkId { get; set; } = string.Empty;
    public string TargetChunkId { get; set; } = string.Empty;
    public string Type { get; set; } = "Semantic";
    public double Strength { get; set; }
    public string Direction { get; set; } = "Bidirectional";
    public string? Description { get; set; }
}

#endregion
