using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentResponseWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DepartmentResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionId = table.Column<int>(type: "int", nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    ResponseText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedByUserId = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepartmentResponses_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DepartmentResponses_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DepartmentResponses_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DepartmentResponses_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DepartmentResponseAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DepartmentResponseId = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedByUserId = table.Column<int>(type: "int", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentResponseAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepartmentResponseAttachments_DepartmentResponses_DepartmentResponseId",
                        column: x => x.DepartmentResponseId,
                        principalTable: "DepartmentResponses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DepartmentResponseAttachments_Users_DeletedByUserId",
                        column: x => x.DeletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DepartmentResponseAttachments_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentResponseAttachments_DeletedByUserId",
                table: "DepartmentResponseAttachments",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentResponseAttachments_DepartmentResponseId",
                table: "DepartmentResponseAttachments",
                column: "DepartmentResponseId");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentResponseAttachments_DepartmentResponseId_IsDeleted",
                table: "DepartmentResponseAttachments",
                columns: new[] { "DepartmentResponseId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentResponseAttachments_UploadedByUserId",
                table: "DepartmentResponseAttachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentResponses_DepartmentId",
                table: "DepartmentResponses",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentResponses_ReviewedByUserId",
                table: "DepartmentResponses",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentResponses_Status_CreatedAt",
                table: "DepartmentResponses",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentResponses_SubmittedByUserId",
                table: "DepartmentResponses",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentResponses_TransactionId_DepartmentId",
                table: "DepartmentResponses",
                columns: new[] { "TransactionId", "DepartmentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DepartmentResponseAttachments");

            migrationBuilder.DropTable(
                name: "DepartmentResponses");
        }
    }
}
