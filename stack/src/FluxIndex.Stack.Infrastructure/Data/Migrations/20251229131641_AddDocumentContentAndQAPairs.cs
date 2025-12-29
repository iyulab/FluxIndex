using System.Collections.Generic;
using FluxIndex.Stack.Domain.Entities;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace FluxIndex.Stack.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentContentAndQAPairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractedContent",
                table: "Documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<List<DocumentQAPair>>(
                name: "QAPairs",
                table: "Documents",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AlterColumn<Vector>(
                name: "Embedding",
                table: "DocumentChunks",
                type: "vector(384)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(1536)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtractedContent",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "QAPairs",
                table: "Documents");

            migrationBuilder.AlterColumn<Vector>(
                name: "Embedding",
                table: "DocumentChunks",
                type: "vector(1536)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(384)",
                oldNullable: true);
        }
    }
}
