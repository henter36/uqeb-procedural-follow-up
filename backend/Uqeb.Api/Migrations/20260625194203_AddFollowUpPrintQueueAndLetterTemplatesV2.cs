using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFollowUpPrintQueueAndLetterTemplatesV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CreatedById",
                table: "LetterTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "LetterTemplates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "LetterTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "LetterTemplates",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "LetterTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TemplateType",
                table: "LetterTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UpdatedById",
                table: "LetterTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE LetterTemplates
                SET IsDefault = 1, TemplateType = 1, SortOrder = 0
                WHERE Code = 'follow_up_letter';
                """);

            migrationBuilder.CreateTable(
                name: "FollowUpPrintIdempotencyKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ResultId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowUpPrintIdempotencyKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FollowUpPrintIdempotencyKeys_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FollowUpPrintJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestedById = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FilterSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    ResponseDeadlineDays = table.Column<int>(type: "int", nullable: true),
                    ExcludeRecentlyPrinted = table.Column<bool>(type: "bit", nullable: false),
                    PrintedLetterExclusionDays = table.Column<int>(type: "int", nullable: false),
                    DaysSinceLastFollowUp = table.Column<int>(type: "int", nullable: false),
                    TotalTransactions = table.Column<int>(type: "int", nullable: false),
                    TotalLetters = table.Column<int>(type: "int", nullable: false),
                    ProcessedLetters = table.Column<int>(type: "int", nullable: false),
                    ReadyLetters = table.Column<int>(type: "int", nullable: false),
                    FailedLetters = table.Column<int>(type: "int", nullable: false),
                    SkippedLetters = table.Column<int>(type: "int", nullable: false),
                    TotalParts = table.Column<int>(type: "int", nullable: false),
                    ReadyParts = table.Column<int>(type: "int", nullable: false),
                    PrintedParts = table.Column<int>(type: "int", nullable: false),
                    CurrentPart = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReadyAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LeaseOwner = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowUpPrintJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FollowUpPrintJobs_LetterTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "LetterTemplates",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FollowUpPrintJobs_Users_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Link = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FollowUpPrintJobParts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobId = table.Column<int>(type: "int", nullable: false),
                    PartNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LetterCount = table.Column<int>(type: "int", nullable: false),
                    EstimatedPages = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadyAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PrintedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowUpPrintJobParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FollowUpPrintJobParts_FollowUpPrintJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "FollowUpPrintJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FollowUpLetterPrintRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionId = table.Column<int>(type: "int", nullable: false),
                    TargetDepartmentId = table.Column<int>(type: "int", nullable: true),
                    TargetEntityId = table.Column<int>(type: "int", nullable: true),
                    TargetEntityNameSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    FollowUpSequence = table.Column<int>(type: "int", nullable: false),
                    ResponseDeadlineDays = table.Column<int>(type: "int", nullable: true),
                    PrintRequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PrintRequestedById = table.Column<int>(type: "int", nullable: false),
                    PrintConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PrintConfirmedById = table.Column<int>(type: "int", nullable: true),
                    RegisteredFollowUpId = table.Column<int>(type: "int", nullable: true),
                    RegisteredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsCancelled = table.Column<bool>(type: "bit", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledById = table.Column<int>(type: "int", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BatchJobId = table.Column<int>(type: "int", nullable: true),
                    BatchJobPartId = table.Column<int>(type: "int", nullable: true),
                    ReprintOfId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowUpLetterPrintRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FollowUpLetterPrintRecords_Departments_TargetDepartmentId",
                        column: x => x.TargetDepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FollowUpLetterPrintRecords_ExternalParties_TargetEntityId",
                        column: x => x.TargetEntityId,
                        principalTable: "ExternalParties",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FollowUpLetterPrintRecords_FollowUpLetterPrintRecords_ReprintOfId",
                        column: x => x.ReprintOfId,
                        principalTable: "FollowUpLetterPrintRecords",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FollowUpLetterPrintRecords_FollowUpPrintJobParts_BatchJobPartId",
                        column: x => x.BatchJobPartId,
                        principalTable: "FollowUpPrintJobParts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FollowUpLetterPrintRecords_FollowUpPrintJobs_BatchJobId",
                        column: x => x.BatchJobId,
                        principalTable: "FollowUpPrintJobs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FollowUpLetterPrintRecords_FollowUps_RegisteredFollowUpId",
                        column: x => x.RegisteredFollowUpId,
                        principalTable: "FollowUps",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FollowUpLetterPrintRecords_LetterTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "LetterTemplates",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FollowUpLetterPrintRecords_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FollowUpLetterPrintRecords_Users_CancelledById",
                        column: x => x.CancelledById,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FollowUpLetterPrintRecords_Users_PrintConfirmedById",
                        column: x => x.PrintConfirmedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FollowUpLetterPrintRecords_Users_PrintRequestedById",
                        column: x => x.PrintRequestedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LetterTemplates_CreatedById",
                table: "LetterTemplates",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_LetterTemplates_UpdatedById",
                table: "LetterTemplates",
                column: "UpdatedById");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_BatchJobId",
                table: "FollowUpLetterPrintRecords",
                column: "BatchJobId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_BatchJobPartId",
                table: "FollowUpLetterPrintRecords",
                column: "BatchJobPartId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_CancelledById",
                table: "FollowUpLetterPrintRecords",
                column: "CancelledById");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_PrintConfirmedById",
                table: "FollowUpLetterPrintRecords",
                column: "PrintConfirmedById");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_PrintRequestedById",
                table: "FollowUpLetterPrintRecords",
                column: "PrintRequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_RegisteredFollowUpId",
                table: "FollowUpLetterPrintRecords",
                column: "RegisteredFollowUpId",
                filter: "[RegisteredFollowUpId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_ReprintOfId",
                table: "FollowUpLetterPrintRecords",
                column: "ReprintOfId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_TargetDepartmentId",
                table: "FollowUpLetterPrintRecords",
                column: "TargetDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_TargetEntityId",
                table: "FollowUpLetterPrintRecords",
                column: "TargetEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_TemplateId",
                table: "FollowUpLetterPrintRecords",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_TransactionId_PrintRequestedAt",
                table: "FollowUpLetterPrintRecords",
                columns: new[] { "TransactionId", "PrintRequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPrintIdempotencyKeys_UserId_Key_Operation",
                table: "FollowUpPrintIdempotencyKeys",
                columns: new[] { "UserId", "Key", "Operation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPrintJobParts_JobId_PartNumber",
                table: "FollowUpPrintJobParts",
                columns: new[] { "JobId", "PartNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPrintJobs_RequestedById",
                table: "FollowUpPrintJobs",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPrintJobs_Status_CreatedAt",
                table: "FollowUpPrintJobs",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPrintJobs_TemplateId",
                table: "FollowUpPrintJobs",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_UserId_IsRead_CreatedAt",
                table: "UserNotifications",
                columns: new[] { "UserId", "IsRead", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_LetterTemplates_Users_CreatedById",
                table: "LetterTemplates",
                column: "CreatedById",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_LetterTemplates_Users_UpdatedById",
                table: "LetterTemplates",
                column: "UpdatedById",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LetterTemplates_Users_CreatedById",
                table: "LetterTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_LetterTemplates_Users_UpdatedById",
                table: "LetterTemplates");

            migrationBuilder.DropTable(
                name: "FollowUpLetterPrintRecords");

            migrationBuilder.DropTable(
                name: "FollowUpPrintIdempotencyKeys");

            migrationBuilder.DropTable(
                name: "UserNotifications");

            migrationBuilder.DropTable(
                name: "FollowUpPrintJobParts");

            migrationBuilder.DropTable(
                name: "FollowUpPrintJobs");

            migrationBuilder.DropIndex(
                name: "IX_LetterTemplates_CreatedById",
                table: "LetterTemplates");

            migrationBuilder.DropIndex(
                name: "IX_LetterTemplates_UpdatedById",
                table: "LetterTemplates");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "LetterTemplates");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "LetterTemplates");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "LetterTemplates");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "LetterTemplates");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "LetterTemplates");

            migrationBuilder.DropColumn(
                name: "TemplateType",
                table: "LetterTemplates");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                table: "LetterTemplates");
        }
    }
}
