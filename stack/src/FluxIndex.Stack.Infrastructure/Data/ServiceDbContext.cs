using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Vault.Entities;
using FluxIndex.Stack.Vault.Enums;
using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Stack.Infrastructure.Data;

/// <summary>
/// Entity Framework Core DbContext for FluxIndex Service.
/// </summary>
public class ServiceDbContext : DbContext
{
    /// <summary>
    /// Embedding vector dimension. Defaults to 384 (LMSupply MiniLM).
    /// Set via AddDbContext configuration based on FluxIndex:Embedding:Dimension setting.
    /// </summary>
    public static int EmbeddingDimension { get; set; } = 384;

    public ServiceDbContext(DbContextOptions<ServiceDbContext> options) : base(options)
    {
    }

    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<IndexingJob> IndexingJobs => Set<IndexingJob>();
    public DbSet<IndexingJobLog> IndexingJobLogs => Set<IndexingJobLog>();
    public DbSet<SearchHistory> SearchHistories => Set<SearchHistory>();
    public DbSet<AiProviderSettings> AiProviderSettings => Set<AiProviderSettings>();

    // Adaptive embedding system entities
    public DbSet<EmbeddingModel> EmbeddingModels => Set<EmbeddingModel>();
    public DbSet<ChunkEmbedding> ChunkEmbeddings => Set<ChunkEmbedding>();
    public DbSet<ReindexingJob> ReindexingJobs => Set<ReindexingJob>();

    // Vault entities
    public DbSet<WatchedFolder> WatchedFolders => Set<WatchedFolder>();
    public DbSet<TrackedFile> TrackedFiles => Set<TrackedFile>();
    public DbSet<TrackedFileVersion> TrackedFileVersions => Set<TrackedFileVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        // Collection configuration
        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.OwnsOne(e => e.Settings, settings =>
            {
                settings.Property(s => s.ChunkingStrategy).HasMaxLength(50);
                settings.Property(s => s.CustomSettings)
                    .HasColumnType("jsonb");
            });
            entity.HasMany(e => e.Documents)
                .WithOne(d => d.Collection)
                .HasForeignKey(d => d.CollectionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Document configuration
        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.SourceType).HasMaxLength(50);
            entity.Property(e => e.SourcePath).HasMaxLength(2000);
            entity.Property(e => e.ContentHash).HasMaxLength(64);
            entity.HasIndex(e => e.ContentHash);
            entity.HasIndex(e => e.CollectionId);
            entity.HasIndex(e => e.Status);
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");
            // Document-level extracted content (full text)
            entity.Property(e => e.ExtractedContent)
                .HasColumnType("text");
            // Document-level Q&A pairs stored as JSON
            entity.Property(e => e.QAPairs)
                .HasColumnType("jsonb");
        });

        // DocumentChunk configuration
        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Embedding)
                .HasColumnType($"vector({EmbeddingDimension})"); // Dynamic dimension from config
            entity.HasIndex(e => e.DocumentId);
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");
            entity.HasOne(e => e.Document)
                .WithMany()
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Create HNSW index for vector similarity search
            entity.HasIndex(e => e.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
        });

        // ApiKey configuration
        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.KeyHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => e.KeyHash).IsUnique();
            entity.Property(e => e.KeyPrefix).HasMaxLength(12).IsRequired();
            entity.HasIndex(e => e.KeyPrefix);
        });

        // IndexingJob configuration
        modelBuilder.Entity<IndexingJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.HasOne(e => e.Document)
                .WithMany()
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // IndexingJobLog configuration
        modelBuilder.Entity<IndexingJobLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.JobId);
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.Message).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.Details).HasMaxLength(4000);
            entity.Property(e => e.Phase).HasMaxLength(100);
            entity.HasOne(e => e.Job)
                .WithMany()
                .HasForeignKey(e => e.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // SearchHistory configuration
        modelBuilder.Entity<SearchHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Query).HasMaxLength(2000).IsRequired();
            entity.HasIndex(e => e.CollectionId);
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.ApiKeyPrefix).HasMaxLength(12);
            entity.HasOne(e => e.Collection)
                .WithMany()
                .HasForeignKey(e => e.CollectionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // AiProviderSettings configuration
        modelBuilder.Entity<AiProviderSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProviderName).HasMaxLength(50).IsRequired();
            entity.HasIndex(e => e.ProviderName).IsUnique();
            entity.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ApiKey).HasMaxLength(500);
            entity.Property(e => e.EmbeddingModel).HasMaxLength(100);
            entity.Property(e => e.LlmModel).HasMaxLength(100);
            entity.Property(e => e.EndpointUrl).HasMaxLength(500);
            entity.Property(e => e.AdditionalConfig).HasColumnType("jsonb");
            entity.HasIndex(e => e.IsDefaultEmbedding);
            entity.HasIndex(e => e.IsDefaultLlm);
        });

        // EmbeddingModel configuration
        modelBuilder.Entity<EmbeddingModel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ModelKey).HasMaxLength(200).IsRequired();
            entity.HasIndex(e => e.ModelKey).IsUnique();
            entity.Property(e => e.ProviderName).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ModelName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.ProviderName);
        });

        // ChunkEmbedding configuration
        modelBuilder.Entity<ChunkEmbedding>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ChunkId);
            entity.HasIndex(e => e.EmbeddingModelId);
            entity.HasIndex(e => new { e.ChunkId, e.EmbeddingModelId }).IsUnique();

            // Vector column without fixed dimension - will be set dynamically per model
            // Using raw SQL for column type since dimension varies
            entity.Property(e => e.Embedding)
                .HasColumnType("vector");

            entity.HasOne(e => e.Chunk)
                .WithMany(c => c.ChunkEmbeddings)
                .HasForeignKey(e => e.ChunkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Model)
                .WithMany(m => m.ChunkEmbeddings)
                .HasForeignKey(e => e.EmbeddingModelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ReindexingJob configuration
        modelBuilder.Entity<ReindexingJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Priority);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.Status, e.Priority });
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);

            entity.HasOne(e => e.TargetModel)
                .WithMany()
                .HasForeignKey(e => e.TargetModelId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceModel)
                .WithMany()
                .HasForeignKey(e => e.SourceModelId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ===== Vault Entities =====

        // WatchedFolder configuration
        modelBuilder.Entity<WatchedFolder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Path).HasMaxLength(2000).IsRequired();
            entity.HasIndex(e => e.Path).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.IncludePatterns).HasColumnType("jsonb");
            entity.Property(e => e.ExcludePatterns).HasColumnType("jsonb");
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CollectionId);
        });

        // TrackedFile configuration
        modelBuilder.Entity<TrackedFile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourcePath).HasMaxLength(2000).IsRequired();
            entity.HasIndex(e => e.SourcePath).IsUnique();
            entity.Property(e => e.FileName).HasMaxLength(500).IsRequired();
            entity.Property(e => e.FileExtension).HasMaxLength(50);
            entity.Property(e => e.ContentHash).HasMaxLength(64);
            entity.HasIndex(e => e.ContentHash);
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.WatchedFolderId);
            entity.HasIndex(e => e.DocumentId);

            entity.HasOne(e => e.WatchedFolder)
                .WithMany(f => f.TrackedFiles)
                .HasForeignKey(e => e.WatchedFolderId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Document)
                .WithMany()
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // TrackedFileVersion configuration
        modelBuilder.Entity<TrackedFileVersion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ContentHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => e.TrackedFileId);
            entity.HasIndex(e => new { e.TrackedFileId, e.Version }).IsUnique();

            entity.HasOne(e => e.TrackedFile)
                .WithMany(f => f.Versions)
                .HasForeignKey(e => e.TrackedFileId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
