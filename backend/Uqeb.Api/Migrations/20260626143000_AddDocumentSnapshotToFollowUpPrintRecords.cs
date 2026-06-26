using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    public partial class AddDocumentSnapshotToFollowUpPrintRecords : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentSnapshotJson",
                table: "FollowUpLetterPrintRecords",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentSnapshotJson",
                table: "FollowUpLetterPrintRecords");
        }
    }
}
