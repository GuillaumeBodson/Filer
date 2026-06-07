using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Filer.Modules.Folders.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "folders");

            migrationBuilder.CreateTable(
                name: "Folders",
                schema: "folders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Folders_Folders_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "folders",
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Folders_OwnerId",
                schema: "folders",
                table: "Folders",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Folders_OwnerId_ParentId_Name",
                schema: "folders",
                table: "Folders",
                columns: new[] { "OwnerId", "ParentId", "Name" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Folders_ParentId",
                schema: "folders",
                table: "Folders",
                column: "ParentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Folders",
                schema: "folders");
        }
    }
}
