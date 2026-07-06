using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentTransactionsReportTemplateOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DetailSortBy",
                table: "ReportExportTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "GroupDetailsByDepartment",
                table: "ReportExportTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetailSortBy",
                table: "ReportExportTemplates");

            migrationBuilder.DropColumn(
                name: "GroupDetailsByDepartment",
                table: "ReportExportTemplates");
        }
    }
}
