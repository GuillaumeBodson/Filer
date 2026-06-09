using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Filer.Modules.Documents.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTags",
                schema: "documents",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTags", x => new { x.DocumentId, x.TagId });
                    table.ForeignKey(
                        name: "FK_DocumentTags_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "documents",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTags_TagId",
                schema: "documents",
                table: "DocumentTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTags",
                schema: "documents");
        }
    }
}
