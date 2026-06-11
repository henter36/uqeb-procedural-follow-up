using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    public partial class AddIncomingSourceTypeAndPerformanceTests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IncomingFromDepartmentId",
                table: "Transactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IncomingSourceType",
                table: "Transactions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_IncomingFromDepartmentId",
                table: "Transactions",
                column: "IncomingFromDepartmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Departments_IncomingFromDepartmentId",
                table: "Transactions",
                column: "IncomingFromDepartmentId",
                principalTable: "Departments",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Departments_IncomingFromDepartmentId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_IncomingFromDepartmentId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "IncomingFromDepartmentId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "IncomingSourceType",
                table: "Transactions");
        }
    }
}
