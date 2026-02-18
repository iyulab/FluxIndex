using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#pragma warning disable CA1861 // Prefer 'static readonly' fields over constant array arguments (auto-generated migration)
#nullable disable

namespace FluxIndex.Stack.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdaptiveEmbeddingSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "AiProviderSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ApiKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefaultEmbedding = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefaultLlm = table.Column<bool>(type: "boolean", nullable: false),
                    EmbeddingModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LlmModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EndpointUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AdditionalConfig = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviderSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    KeyPrefix = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RateLimitPerMinute = table.Column<int>(type: "integer", nullable: true),
                    RateLimitPerDay = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Settings_ChunkSize = table.Column<int>(type: "integer", nullable: false),
                    Settings_ChunkOverlap = table.Column<int>(type: "integer", nullable: false),
                    Settings_ChunkingStrategy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Settings_EnableQAGeneration = table.Column<bool>(type: "boolean", nullable: false),
                    Settings_EnableEnrichment = table.Column<bool>(type: "boolean", nullable: false),
                    Settings_CustomSettings = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmbeddingModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Dimension = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EmbeddingCount = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmbeddingModels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SourcePath = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IndexedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ChunkCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SearchHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Query = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ResultCount = table.Column<int>(type: "integer", nullable: false),
                    ExecutionTimeMs = table.Column<double>(type: "double precision", nullable: false),
                    SearchType = table.Column<int>(type: "integer", nullable: false),
                    ApiKeyPrefix = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SearchHistories_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ReindexingJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceModelId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalChunks = table.Column<int>(type: "integer", nullable: false),
                    ProcessedChunks = table.Column<int>(type: "integer", nullable: false),
                    FailedChunks = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    DeleteOldEmbeddings = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReindexingJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReindexingJobs_EmbeddingModels_SourceModelId",
                        column: x => x.SourceModelId,
                        principalTable: "EmbeddingModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ReindexingJobs_EmbeddingModels_TargetModelId",
                        column: x => x.TargetModelId,
                        principalTable: "EmbeddingModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    TokenCount = table.Column<int>(type: "integer", nullable: false),
                    StartPosition = table.Column<int>(type: "integer", nullable: false),
                    EndPosition = table.Column<int>(type: "integer", nullable: false),
                    Metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentChunks_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IndexingJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalChunks = table.Column<int>(type: "integer", nullable: false),
                    ProcessedChunks = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexingJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndexingJobs_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChunkEmbeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmbeddingModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector", nullable: false),
                    Dimension = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChunkEmbeddings_DocumentChunks_ChunkId",
                        column: x => x.ChunkId,
                        principalTable: "DocumentChunks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChunkEmbeddings_EmbeddingModels_EmbeddingModelId",
                        column: x => x.EmbeddingModelId,
                        principalTable: "EmbeddingModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IndexingJobLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Phase = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexingJobLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndexingJobLogs_IndexingJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "IndexingJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderSettings_IsDefaultEmbedding",
                table: "AiProviderSettings",
                column: "IsDefaultEmbedding");

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderSettings_IsDefaultLlm",
                table: "AiProviderSettings",
                column: "IsDefaultLlm");

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderSettings_ProviderName",
                table: "AiProviderSettings",
                column: "ProviderName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyHash",
                table: "ApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyPrefix",
                table: "ApiKeys",
                column: "KeyPrefix");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_Name",
                table: "ApiKeys",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChunkEmbeddings_ChunkId",
                table: "ChunkEmbeddings",
                column: "ChunkId");

            migrationBuilder.CreateIndex(
                name: "IX_ChunkEmbeddings_ChunkId_EmbeddingModelId",
                table: "ChunkEmbeddings",
                columns: new[] { "ChunkId", "EmbeddingModelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChunkEmbeddings_EmbeddingModelId",
                table: "ChunkEmbeddings",
                column: "EmbeddingModelId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_Name",
                table: "Collections",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_DocumentId",
                table: "DocumentChunks",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_Embedding",
                table: "DocumentChunks",
                column: "Embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_CollectionId",
                table: "Documents",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ContentHash",
                table: "Documents",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Status",
                table: "Documents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EmbeddingModels_IsActive",
                table: "EmbeddingModels",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_EmbeddingModels_ModelKey",
                table: "EmbeddingModels",
                column: "ModelKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmbeddingModels_ProviderName",
                table: "EmbeddingModels",
                column: "ProviderName");

            migrationBuilder.CreateIndex(
                name: "IX_IndexingJobLogs_CreatedAt",
                table: "IndexingJobLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IndexingJobLogs_JobId",
                table: "IndexingJobLogs",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_IndexingJobs_CreatedAt",
                table: "IndexingJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IndexingJobs_DocumentId",
                table: "IndexingJobs",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_IndexingJobs_Status",
                table: "IndexingJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ReindexingJobs_CreatedAt",
                table: "ReindexingJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReindexingJobs_Priority",
                table: "ReindexingJobs",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_ReindexingJobs_SourceModelId",
                table: "ReindexingJobs",
                column: "SourceModelId");

            migrationBuilder.CreateIndex(
                name: "IX_ReindexingJobs_Status",
                table: "ReindexingJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ReindexingJobs_Status_Priority",
                table: "ReindexingJobs",
                columns: new[] { "Status", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_ReindexingJobs_TargetModelId",
                table: "ReindexingJobs",
                column: "TargetModelId");

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistories_CollectionId",
                table: "SearchHistories",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistories_CreatedAt",
                table: "SearchHistories",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiProviderSettings");

            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "ChunkEmbeddings");

            migrationBuilder.DropTable(
                name: "IndexingJobLogs");

            migrationBuilder.DropTable(
                name: "ReindexingJobs");

            migrationBuilder.DropTable(
                name: "SearchHistories");

            migrationBuilder.DropTable(
                name: "DocumentChunks");

            migrationBuilder.DropTable(
                name: "IndexingJobs");

            migrationBuilder.DropTable(
                name: "EmbeddingModels");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Collections");
        }
    }
}
