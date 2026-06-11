using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    public partial class RefineTransactionFormAndDepartmentReports : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TransactionOutgoingDepartments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionId = table.Column<int>(type: "int", nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionOutgoingDepartments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionOutgoingDepartments_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TransactionOutgoingDepartments_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TransactionOutgoingDepartments_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionOutgoingDepartments_CreatedById",
                table: "TransactionOutgoingDepartments",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionOutgoingDepartments_DepartmentId",
                table: "TransactionOutgoingDepartments",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionOutgoingDepartments_TransactionId_DepartmentId",
                table: "TransactionOutgoingDepartments",
                columns: new[] { "TransactionId", "DepartmentId" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransactionOutgoingDepartments");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Transactions");
        }
    }
}
