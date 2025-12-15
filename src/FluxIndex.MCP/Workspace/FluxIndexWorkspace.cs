using FluxIndex.SDK;
using Microsoft.Extensions.Logging;

namespace FluxIndex.MCP.Workspace;

/// <summary>
/// Manages a FluxIndex workspace including initialization, configuration, and context creation
/// </summary>
public class FluxIndexWorkspace : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly WorkspaceConfig _config;
    private readonly ILogger<FluxIndexWorkspace>? _logger;
    private IFluxIndexContext? _context;

    public string WorkspaceRoot => _workspaceRoot;
    public WorkspaceConfig Config => _config;
    public string WorkspaceDirectory => WorkspaceLocator.GetWorkspaceDirectory(_workspaceRoot);
    public string DatabasePath => WorkspaceLocator.GetDatabasePath(_workspaceRoot);
    public string ConfigPath => WorkspaceLocator.GetConfigPath(_workspaceRoot);

    private FluxIndexWorkspace(string workspaceRoot, WorkspaceConfig config, ILogger<FluxIndexWorkspace>? logger = null)
    {
        _workspaceRoot = workspaceRoot;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Open an existing workspace
    /// </summary>
    public static FluxIndexWorkspace Open(string? startPath = null, ILogger<FluxIndexWorkspace>? logger = null)
    {
        var workspaceRoot = WorkspaceLocator.FindWorkspaceRoot(startPath);
        if (workspaceRoot == null)
        {
            throw new InvalidOperationException(
                "No FluxIndex workspace found. Run 'fluxindex init' to create one.");
        }

        var configPath = WorkspaceLocator.GetConfigPath(workspaceRoot);
        var config = WorkspaceConfig.Load(configPath);

        return new FluxIndexWorkspace(workspaceRoot, config, logger);
    }

    /// <summary>
    /// Initialize a new workspace
    /// </summary>
    public static FluxIndexWorkspace Initialize(
        string? targetPath = null,
        WorkspaceConfig? config = null,
        ILogger<FluxIndexWorkspace>? logger = null)
    {
        targetPath ??= Directory.GetCurrentDirectory();
        config ??= new WorkspaceConfig();

        var workspaceDir = WorkspaceLocator.GetWorkspaceDirectory(targetPath);

        // Check if workspace already exists
        if (Directory.Exists(workspaceDir))
        {
            throw new InvalidOperationException(
                $"Workspace already exists at {workspaceDir}");
        }

        // Create workspace directory structure
        Directory.CreateDirectory(workspaceDir);
        Directory.CreateDirectory(Path.Combine(workspaceDir, "cache"));
        Directory.CreateDirectory(Path.Combine(workspaceDir, "logs"));

        // Save config
        var configPath = WorkspaceLocator.GetConfigPath(targetPath);
        config.Save(configPath);

        logger?.LogInformation("Initialized FluxIndex workspace at {Path}", workspaceDir);

        return new FluxIndexWorkspace(targetPath, config, logger);
    }

    /// <summary>
    /// Get or create the FluxIndexContext for this workspace
    /// </summary>
    public IFluxIndexContext GetContext()
    {
        if (_context != null)
        {
            return _context;
        }

        var builder = FluxIndexContext.CreateBuilder();

        // Configure SQLite storage
        builder.UseSQLite(DatabasePath);

        // Configure embedding service based on config
        ConfigureEmbeddingService(builder);

        // Configure completion service if available
        if (_config.Completion != null)
        {
            ConfigureCompletionService(builder);
        }

        _context = builder.Build();
        _logger?.LogInformation("Created FluxIndexContext for workspace at {Path}", _workspaceRoot);

        return _context;
    }

    private void ConfigureEmbeddingService(FluxIndexContextBuilder builder)
    {
        switch (_config.Embedding.Provider.ToLowerInvariant())
        {
            case "local":
            case "localai":
                // Use local AI embedder (ONNX-based, no API key required)
                builder.UseLocalAIEmbedding();
                break;

            default:
                // Default to LocalAI for unknown providers
                // External AI providers should be implemented by consuming applications
                builder.UseLocalAIEmbedding();
                break;
        }
    }

    private void ConfigureCompletionService(FluxIndexContextBuilder builder)
    {
        // Completion service configuration can be extended here
        // Currently OpenAI is used for both embedding and completion
    }

    /// <summary>
    /// Save the current configuration to disk
    /// </summary>
    public void SaveConfig()
    {
        _config.Save(ConfigPath);
        _logger?.LogInformation("Saved configuration to {Path}", ConfigPath);
    }

    /// <summary>
    /// Check if a file path is within the workspace (sandbox check)
    /// </summary>
    public bool IsPathAllowed(string path)
        => WorkspaceLocator.IsPathWithinWorkspace(_workspaceRoot, path);

    /// <summary>
    /// Get relative path from workspace root
    /// </summary>
    public string GetRelativePath(string absolutePath)
        => Path.GetRelativePath(_workspaceRoot, absolutePath);

    public void Dispose()
    {
        if (_context is IDisposable disposable)
        {
            disposable.Dispose();
        }
        _context = null;
    }
}
