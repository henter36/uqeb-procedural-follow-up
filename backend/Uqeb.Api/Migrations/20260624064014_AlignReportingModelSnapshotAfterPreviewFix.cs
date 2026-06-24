using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    /// <inheritdoc />
    public partial class AlignReportingModelSnapshotAfterPreviewFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Metadata-only migration.
            // ReportNumberSequences.Year was already created as a non-IDENTITY column.
            // This migration aligns the EF model snapshot with ValueGeneratedNever().
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No schema rollback is required.
        }
    }
}