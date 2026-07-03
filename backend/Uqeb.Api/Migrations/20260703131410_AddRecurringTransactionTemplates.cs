using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringTransactionTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecurringPeriodKey",
                table: "Transactions",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecurringPeriodLabel",
                table: "Transactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecurringTemplateId",
                table: "Transactions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringTransactionTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubjectTemplate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecurrenceType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IncomingSourceType = table.Column<int>(type: "int", nullable: false),
                    IncomingFromPartyId = table.Column<int>(type: "int", nullable: true),
                    IncomingFromDepartmentId = table.Column<int>(type: "int", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    ResponseType = table.Column<int>(type: "int", nullable: false),
                    RequiresResponse = table.Column<bool>(type: "bit", nullable: false),
                    DefaultRequiredAction = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DueDaysAfterPeriodEnd = table.Column<int>(type: "int", nullable: false),
                    DefaultReplyDueDays = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastGeneratedPeriodKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedById = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PausedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PausedById = table.Column<int>(type: "int", nullable: true),
                    ResumedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResumedById = table.Column<int>(type: "int", nullable: true),
                    TerminatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TerminatedById = table.Column<int>(type: "int", nullable: true),
                    TerminationReason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringTransactionTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringTransactionTemplates_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecurringTransactionTemplates_Departments_IncomingFromDepartmentId",
                        column: x => x.IncomingFromDepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecurringTransactionTemplates_ExternalParties_IncomingFromPartyId",
                        column: x => x.IncomingFromPartyId,
                        principalTable: "ExternalParties",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecurringTransactionTemplates_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecurringTransactionTemplates_Users_PausedById",
                        column: x => x.PausedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecurringTransactionTemplates_Users_ResumedById",
                        column: x => x.ResumedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecurringTransactionTemplates_Users_TerminatedById",
                        column: x => x.TerminatedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RecurringTransactionTemplateDepartments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringTransactionTemplateDepartments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringTransactionTemplateDepartments_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecurringTransactionTemplateDepartments_RecurringTransactionTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "RecurringTransactionTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_RecurringTemplateId_RecurringPeriodKey",
                table: "Transactions",
                columns: new[] { "RecurringTemplateId", "RecurringPeriodKey" },
                unique: true,
                filter: "[RecurringTemplateId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactionTemplateDepartments_DepartmentId",
                table: "RecurringTransactionTemplateDepartments",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactionTemplateDepartments_TemplateId_DepartmentId",
                table: "RecurringTransactionTemplateDepartments",
                columns: new[] { "TemplateId", "DepartmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactionTemplates_CategoryId",
                table: "RecurringTransactionTemplates",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactionTemplates_CreatedById",
                table: "RecurringTransactionTemplates",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactionTemplates_IncomingFromDepartmentId",
                table: "RecurringTransactionTemplates",
                column: "IncomingFromDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactionTemplates_IncomingFromPartyId",
                table: "RecurringTransactionTemplates",
                column: "IncomingFromPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactionTemplates_PausedById",
                table: "RecurringTransactionTemplates",
                column: "PausedById");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactionTemplates_RecurrenceType",
                table: "RecurringTransactionTemplates",
                column: "RecurrenceType");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactionTemplates_ResumedById",
                table: "RecurringTransactionTemplates",
                column: "ResumedById");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactionTemplates_Status",
                table: "RecurringTransactionTemplates",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactionTemplates_TerminatedById",
                table: "RecurringTransactionTemplates",
                column: "TerminatedById");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_RecurringTransactionTemplates_RecurringTemplateId",
                table: "Transactions",
                column: "RecurringTemplateId",
                principalTable: "RecurringTransactionTemplates",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_RecurringTransactionTemplates_RecurringTemplateId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "RecurringTransactionTemplateDepartments");

            migrationBuilder.DropTable(
                name: "RecurringTransactionTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_RecurringTemplateId_RecurringPeriodKey",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RecurringPeriodKey",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RecurringPeriodLabel",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RecurringTemplateId",
                table: "Transactions");
        }
    }
}
