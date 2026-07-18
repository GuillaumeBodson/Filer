using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Filer.Modules.Documents.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                schema: "documents",
                table: "Documents",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', translate(\"FileName\", '._-', '   ')), 'A') || setweight(jsonb_to_tsvector('simple', coalesce(\"Metadata\", '{}'::jsonb), '[\"string\"]'), 'B')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_SearchVector",
                schema: "documents",
                table: "Documents",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_SearchVector",
                schema: "documents",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                schema: "documents",
                table: "Documents");
        }
    }
}
