using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Filer.Modules.BackgroundJobs.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisJobRetryScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                schema: "jobs",
                table: "AnalysisJobs",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                schema: "jobs",
                table: "AnalysisJobs");
        }
    }
}
