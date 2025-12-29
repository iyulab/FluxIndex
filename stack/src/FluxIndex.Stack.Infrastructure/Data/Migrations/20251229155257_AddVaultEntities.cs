using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluxIndex.Stack.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVaultEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WatchedFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsRecursive = table.Column<bool>(type: "boolean", nullable: false),
                    IncludePatterns = table.Column<string>(type: "jsonb", nullable: false),
                    ExcludePatterns = table.Column<string>(type: "jsonb", nullable: false),
                    AutoMemorize = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastScannedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchedFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackedFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePath = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileExtension = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FileModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MemorizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    WatchedFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedFiles_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrackedFiles_WatchedFolders_WatchedFolderId",
                        column: x => x.WatchedFolderId,
                        principalTable: "WatchedFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TrackedFileVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackedFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HasExtract = table.Column<bool>(type: "boolean", nullable: false),
                    HasChunks = table.Column<bool>(type: "boolean", nullable: false),
                    HasImages = table.Column<bool>(type: "boolean", nullable: false),
                    HasQA = table.Column<bool>(type: "boolean", nullable: false),
                    HasEnrichment = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedFileVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedFileVersions_TrackedFiles_TrackedFileId",
                        column: x => x.TrackedFileId,
                        principalTable: "TrackedFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedFiles_ContentHash",
                table: "TrackedFiles",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedFiles_DocumentId",
                table: "TrackedFiles",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedFiles_SourcePath",
                table: "TrackedFiles",
                column: "SourcePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedFiles_Status",
                table: "TrackedFiles",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedFiles_WatchedFolderId",
                table: "TrackedFiles",
                column: "WatchedFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedFileVersions_TrackedFileId",
                table: "TrackedFileVersions",
                column: "TrackedFileId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedFileVersions_TrackedFileId_Version",
                table: "TrackedFileVersions",
                columns: new[] { "TrackedFileId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchedFolders_CollectionId",
                table: "WatchedFolders",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchedFolders_Path",
                table: "WatchedFolders",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchedFolders_Status",
                table: "WatchedFolders",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackedFileVersions");

            migrationBuilder.DropTable(
                name: "TrackedFiles");

            migrationBuilder.DropTable(
                name: "WatchedFolders");
        }
    }
}
