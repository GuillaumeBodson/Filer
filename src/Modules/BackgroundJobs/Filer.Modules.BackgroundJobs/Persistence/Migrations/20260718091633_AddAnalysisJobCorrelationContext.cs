using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Filer.Modules.BackgroundJobs.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisJobCorrelationContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationContext",
                schema: "jobs",
                table: "AnalysisJobs",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrelationContext",
                schema: "jobs",
                table: "AnalysisJobs");
        }
    }
}
