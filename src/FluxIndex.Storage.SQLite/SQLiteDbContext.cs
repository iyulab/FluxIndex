using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// SQLite 데이터베이스 컨텍스트 (로컬 개발용)
/// </summary>
public class SQLiteDbContext : DbContext
{
    public SQLiteDbContext(DbContextOptions<SQLiteDbContext> options)
        : base(options)
    {
    }

    public DbSet<SQLiteDocument> Documents { get; set; }
    public DbSet<SQLiteChunk> Chunks { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Document 엔티티 설정
        modelBuilder.Entity<SQLiteDocument>(entity =>
        {
            entity.ToTable("documents");
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id)
                .HasColumnName("id");
            
            entity.Property(e => e.ExternalId)
                .HasColumnName("external_id")
                .HasMaxLength(255)
                .IsRequired();
            
            entity.Property(e => e.Title)
                .HasColumnName("title")
                .HasMaxLength(500);
            
            entity.Property(e => e.Source)
                .HasColumnName("source")
                .HasMaxLength(1000);
            
            entity.Property(e => e.ContentHash)
                .HasColumnName("content_hash")
                .HasMaxLength(64)
                .IsRequired();
            
            entity.Property(e => e.MetadataJson)
                .HasColumnName("metadata_json");
            
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at");
            
            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at");

            entity.HasIndex(e => e.ExternalId)
                .HasDatabaseName("idx_documents_external_id");
            
            entity.HasIndex(e => e.ContentHash)
                .HasDatabaseName("idx_documents_content_hash");
        });

        // Chunk 엔티티 설정
        modelBuilder.Entity<SQLiteChunk>(entity =>
        {
            entity.ToTable("chunks");
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id)
                .HasColumnName("id");
            
            entity.Property(e => e.DocumentId)
                .HasColumnName("document_id");
            
            entity.Property(e => e.ChunkIndex)
                .HasColumnName("chunk_index");
            
            entity.Property(e => e.Content)
                .HasColumnName("content")
                .IsRequired();
            
            entity.Property(e => e.EmbeddingJson)
                .HasColumnName("embedding_json");
            
            entity.Property(e => e.TokenCount)
                .HasColumnName("token_count");
            
            entity.Property(e => e.MetadataJson)
                .HasColumnName("metadata_json");
            
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at");

            // 관계 설정
            entity.HasOne(e => e.Document)
                .WithMany(d => d.Chunks)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // 인덱스 설정
            entity.HasIndex(e => e.DocumentId)
                .HasDatabaseName("idx_chunks_document_id");
            
            entity.HasIndex(e => new { e.DocumentId, e.ChunkIndex })
                .HasDatabaseName("idx_chunks_document_chunk")
                .IsUnique();
        });
    }
}

/// <summary>
/// SQLite 문서 엔티티
/// </summary>
public class SQLiteDocument
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Source { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    public ICollection<SQLiteChunk> Chunks { get; set; } = new List<SQLiteChunk>();
    
    // JSON 변환 헬퍼
    public Dictionary<string, object>? GetMetadata()
    {
        if (string.IsNullOrEmpty(MetadataJson))
            return null;
        
        return JsonSerializer.Deserialize<Dictionary<string, object>>(MetadataJson);
    }
    
    public void SetMetadata(Dictionary<string, object>? metadata)
    {
        MetadataJson = metadata != null 
            ? JsonSerializer.Serialize(metadata) 
            : null;
    }
}

/// <summary>
/// SQLite 청크 엔티티
/// </summary>
public class SQLiteChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? EmbeddingJson { get; set; }
    public int TokenCount { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; }
    
    public SQLiteDocument Document { get; set; } = null!;
    
    // JSON 변환 헬퍼
    public float[]? GetEmbedding()
    {
        if (string.IsNullOrEmpty(EmbeddingJson))
            return null;
        
        return JsonSerializer.Deserialize<float[]>(EmbeddingJson);
    }
    
    public void SetEmbedding(float[]? embedding)
    {
        EmbeddingJson = embedding != null 
            ? JsonSerializer.Serialize(embedding) 
            : null;
    }
    
    public Dictionary<string, object>? GetMetadata()
    {
        if (string.IsNullOrEmpty(MetadataJson))
            return null;
        
        return JsonSerializer.Deserialize<Dictionary<string, object>>(MetadataJson);
    }
    
    public void SetMetadata(Dictionary<string, object>? metadata)
    {
        MetadataJson = metadata != null 
            ? JsonSerializer.Serialize(metadata) 
            : null;
    }
}