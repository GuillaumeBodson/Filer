using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Filer.Modules.Tags.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tags");

            migrationBuilder.CreateTable(
                name: "Tags",
                schema: "tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_OwnerId",
                schema: "tags",
                table: "Tags",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_OwnerId_Name",
                schema: "tags",
                table: "Tags",
                columns: new[] { "OwnerId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tags",
                schema: "tags");
        }
    }
}
