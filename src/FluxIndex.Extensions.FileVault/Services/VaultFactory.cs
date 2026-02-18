using System.Collections.Concurrent;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// Factory for creating tenant-scoped IVault instances.
///
/// Architecture:
/// - Shared services (stateless): IContentHasher, IGitService, IFileWatcherService
/// - Shared integration services: IExtractor, IChunker, IVectorStore, IEmbeddingService
/// - Tenant-scoped services: IVaultStorageService, IVaultQueueService, IVaultPipeline, IVault
/// </summary>
public sealed partial class VaultFactory : IVaultFactory
{
    private readonly ConcurrentDictionary<string, VaultContext> _vaults = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly FileVaultOptions _defaultOptions;
    private readonly object _createLock = new();
    private bool _disposed;

    // Shared services (stateless, can be reused across all tenants)
    private readonly IContentHasher _sharedHasher;
    private readonly IGitService _sharedGitService;
    private readonly IFileWatcherService _sharedFileWatcher;

    // Shared integration services (for document processing)
    private readonly IExtractor? _sharedExtractor;
    private readonly IChunker? _sharedChunker;
    private readonly IVectorStore? _sharedVectorStore;
    private readonly IEmbeddingService? _sharedEmbeddingService;

    public VaultFactory(
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        IOptions<FileVaultOptions> options,
        IContentHasher hasher,
        IGitService gitService,
        IFileWatcherService fileWatcher,
        IExtractor? extractor = null,
        IChunker? chunker = null,
        IVectorStore? vectorStore = null,
        IEmbeddingService? embeddingService = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = _loggerFactory.CreateLogger<VaultFactory>();
        _defaultOptions = options?.Value ?? new FileVaultOptions();

        // Store shared services
        _sharedHasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _sharedGitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _sharedFileWatcher = fileWatcher ?? throw new ArgumentNullException(nameof(fileWatcher));
        _sharedExtractor = extractor;
        _sharedChunker = chunker;
        _sharedVectorStore = vectorStore;
        _sharedEmbeddingService = embeddingService;
    }

    public IVault GetOrCreate(string tenantId)
    {
        return GetOrCreate(tenantId, null);
    }

    public IVault GetOrCreate(string tenantId, Action<FileVaultOptions>? configureOptions)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (_vaults.TryGetValue(tenantId, out var existingContext))
        {
            return existingContext.Vault;
        }

        lock (_createLock)
        {
            // Double-check after acquiring lock
            if (_vaults.TryGetValue(tenantId, out existingContext))
            {
                return existingContext.Vault;
            }

            var context = CreateVaultContext(tenantId, configureOptions);
            _vaults[tenantId] = context;

            LogCreatedVault(_logger, tenantId, context.VaultBasePath);

            return context.Vault;
        }
    }

    public VaultContext? GetContext(string tenantId)
    {
        _vaults.TryGetValue(tenantId, out var context);
        return context;
    }

    public bool Exists(string tenantId)
    {
        if (_vaults.ContainsKey(tenantId))
            return true;

        // Check if vault directory exists on disk
        var vaultPath = GetTenantVaultPath(tenantId);
        return Directory.Exists(vaultPath);
    }

    public IEnumerable<string> DiscoverTenants()
    {
        var basePath = _defaultOptions.VaultBasePath
            ?? Path.Combine(Directory.GetCurrentDirectory(), _defaultOptions.VaultDirectoryName);

        if (!Directory.Exists(basePath))
            yield break;

        foreach (var dir in Directory.GetDirectories(basePath))
        {
            var tenantId = Path.GetFileName(dir);
            var vaultPath = Path.Combine(dir, _defaultOptions.VaultDirectoryName);

            // Check for vault marker (queue.db or any entry directories)
            if (Directory.Exists(vaultPath) || File.Exists(Path.Combine(dir, "queue.db")))
            {
                yield return tenantId;
            }
        }
    }

    public IReadOnlyCollection<string> GetActiveTenants()
    {
        return _vaults.Keys.ToList().AsReadOnly();
    }

    public IEnumerable<VaultContext> GetAllContexts() => _vaults.Values;

    public async Task DisposeAsync(string tenantId)
    {
        if (_vaults.TryRemove(tenantId, out var context))
        {
            await DisposeContextAsync(context);

            LogDisposedVault(_logger, tenantId);
        }
    }

    public async Task DisposeAllAsync()
    {
        var contexts = _vaults.Values.ToList();
        _vaults.Clear();

        foreach (var context in contexts)
        {
            await DisposeContextAsync(context);
        }
    }

    private VaultContext CreateVaultContext(string tenantId, Action<FileVaultOptions>? configureOptions)
    {
        // Clone default options and apply tenant-specific configuration
        var tenantOptions = CloneOptions(_defaultOptions);
        tenantOptions.VaultBasePath = GetTenantVaultPath(tenantId);
        configureOptions?.Invoke(tenantOptions);

        // Ensure directory exists
        Directory.CreateDirectory(tenantOptions.VaultBasePath);

        var optionsWrapper = MicrosoftOptions.Create(tenantOptions);

        // Create tenant-specific storage service
        var storageLogger = _loggerFactory.CreateLogger<VaultStorageService>();
        var storage = new VaultStorageService(storageLogger, _sharedGitService, optionsWrapper);

        // Create tenant-specific queue service
        var queueLogger = _loggerFactory.CreateLogger<VaultQueueService>();
        var queue = new VaultQueueService(queueLogger, optionsWrapper);

        // Create tenant-specific pipeline (needs tenant-specific storage)
        var pipelineLogger = _loggerFactory.CreateLogger<VaultPipeline>();
        var pipeline = new VaultPipeline(
            _sharedGitService,
            _sharedHasher,
            storage,
            pipelineLogger,
            optionsWrapper,
            _sharedExtractor,
            _sharedChunker,
            _sharedVectorStore,
            _sharedEmbeddingService);

        // Create VaultManager with mixed shared/tenant-specific services
        var managerLogger = _loggerFactory.CreateLogger<VaultManager>();
        var vault = new VaultManager(
            _sharedHasher,
            _sharedGitService,
            pipeline,
            queue,
            _sharedFileWatcher,
            storage,
            managerLogger,
            optionsWrapper);

        return new VaultContext
        {
            TenantId = tenantId,
            VaultBasePath = tenantOptions.VaultBasePath,
            Vault = vault,
            QueueService = queue,
            StorageService = storage,
            Pipeline = pipeline,
            Options = tenantOptions
        };
    }

    private string GetTenantVaultPath(string tenantId)
    {
        var basePath = _defaultOptions.VaultBasePath
            ?? Path.Combine(Directory.GetCurrentDirectory(), _defaultOptions.VaultDirectoryName);

        return Path.Combine(basePath, tenantId, _defaultOptions.VaultDirectoryName);
    }

    private static FileVaultOptions CloneOptions(FileVaultOptions source)
    {
        return new FileVaultOptions
        {
            VaultDirectoryName = source.VaultDirectoryName,
            VaultBasePath = source.VaultBasePath,
            MaxFileSizeMB = source.MaxFileSizeMB,
            EnableRealTimeWatch = source.EnableRealTimeWatch,
            DebounceDelayMs = source.DebounceDelayMs,
            WatcherBufferSize = source.WatcherBufferSize,
            VersionRetentionCount = source.VersionRetentionCount,
            AutoCleanupOrphans = source.AutoCleanupOrphans,
            DefaultIncludePatterns = [.. source.DefaultIncludePatterns],
            DefaultExcludePatterns = [.. source.DefaultExcludePatterns],
            MaxConcurrentProcessing = source.MaxConcurrentProcessing,
            QueuePollingIntervalMs = source.QueuePollingIntervalMs,
            EnableAutoRetry = source.EnableAutoRetry,
            MaxRetryCount = source.MaxRetryCount,
            RetryDelayMs = source.RetryDelayMs,
            EnableBackgroundProcessing = source.EnableBackgroundProcessing,
            Chunking = new ChunkingDefaults
            {
                MaxChunkSize = source.Chunking.MaxChunkSize,
                OverlapSize = source.Chunking.OverlapSize,
                Strategy = source.Chunking.Strategy,
                Language = source.Chunking.Language
            }
        };
    }

    private static async Task DisposeContextAsync(VaultContext context)
    {
        // Dispose queue service (closes SQLite connection)
        if (context.QueueService is IDisposable disposableQueue)
        {
            disposableQueue.Dispose();
        }

        // Give a moment for any pending operations
        await Task.Delay(100);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Synchronously dispose all contexts
        foreach (var context in _vaults.Values)
        {
            if (context.QueueService is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _vaults.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await DisposeAllAsync();
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Created vault for tenant {TenantId} at {Path}")]
    private static partial void LogCreatedVault(ILogger logger, string tenantId, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disposed vault for tenant {TenantId}")]
    private static partial void LogDisposedVault(ILogger logger, string tenantId);

    #endregion
}
