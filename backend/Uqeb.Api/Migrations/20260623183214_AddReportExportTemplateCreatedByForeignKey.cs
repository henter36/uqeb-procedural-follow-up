using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReportExportTemplateCreatedByForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ReportExportTemplates_CreatedById",
                table: "ReportExportTemplates",
                column: "CreatedById");

            migrationBuilder.AddForeignKey(
                name: "FK_ReportExportTemplates_Users_CreatedById",
                table: "ReportExportTemplates",
                column: "CreatedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReportExportTemplates_Users_CreatedById",
                table: "ReportExportTemplates");

            migrationBuilder.DropIndex(
                name: "IX_ReportExportTemplates_CreatedById",
                table: "ReportExportTemplates");
        }
    }
}
