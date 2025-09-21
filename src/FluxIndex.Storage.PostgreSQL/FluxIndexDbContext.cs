using Microsoft.EntityFrameworkCore;
using FluxIndex.Storage.PostgreSQL.Entities;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Pgvector.EntityFrameworkCore;
using System;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// FluxIndex PostgreSQL 데이터베이스 컨텍스트
/// </summary>
public class FluxIndexDbContext : DbContext
{
    public FluxIndexDbContext(DbContextOptions<FluxIndexDbContext> options)
        : base(options)
    {
    }

    public DbSet<VectorDocument> Documents { get; set; }
    public DbSet<VectorChunk> Chunks { get; set; }
    public DbSet<ChunkHierarchyEntity> ChunkHierarchies { get; set; }
    public DbSet<ChunkRelationshipEntity> ChunkRelationships { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PostgreSQL pgvector 확장 활성화
        modelBuilder.HasPostgresExtension("vector");

        // Document 엔티티 설정
        modelBuilder.Entity<VectorDocument>(entity =>
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
            
            entity.Property(e => e.Metadata)
                .HasColumnName("metadata")
                .HasColumnType("jsonb");
            
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.ExternalId)
                .HasDatabaseName("idx_documents_external_id");
            
            entity.HasIndex(e => e.ContentHash)
                .HasDatabaseName("idx_documents_content_hash");
        });

        // Chunk 엔티티 설정
        modelBuilder.Entity<VectorChunk>(entity =>
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
            
            entity.Property(e => e.Embedding)
                .HasColumnName("embedding")
                .HasColumnType("vector(1536)"); // Default dimension, can be changed
            
            entity.Property(e => e.TokenCount)
                .HasColumnName("token_count");
            
            entity.Property(e => e.Metadata)
                .HasColumnName("metadata")
                .HasColumnType("jsonb");
            
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

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

            // Vector 검색을 위한 HNSW 인덱스 (pgvector 0.5.0+)
            // 마이그레이션 후 수동으로 추가 필요:
            // CREATE INDEX idx_chunks_embedding_hnsw ON chunks USING hnsw (embedding vector_cosine_ops);
        });
    }
}

/// <summary>
/// 벡터 문서 엔티티
/// </summary>
public class VectorDocument
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Source { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public Dictionary<string, object>? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    public ICollection<VectorChunk> Chunks { get; set; } = new List<VectorChunk>();
}

/// <summary>
/// 벡터 청크 엔티티
/// </summary>
public class VectorChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public float[]? Embedding { get; set; }
    public int TokenCount { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    
    public VectorDocument Document { get; set; } = null!;
}

/// <summary>
/// DocumentChunk 별칭 (기존 VectorChunk와의 호환성)
/// </summary>
public class DocumentChunk : VectorChunk
{
}