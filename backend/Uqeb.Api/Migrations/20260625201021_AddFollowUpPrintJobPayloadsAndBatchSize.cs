using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFollowUpPrintJobPayloadsAndBatchSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FollowUpPrintIdempotencyKeys_UserId_Key_Operation",
                table: "FollowUpPrintIdempotencyKeys");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpLetterPrintRecords_BatchJobPartId",
                table: "FollowUpLetterPrintRecords");

            migrationBuilder.AddColumn<int>(
                name: "BatchSize",
                table: "FollowUpPrintJobs",
                type: "int",
                nullable: false,
                defaultValue: 25);

            migrationBuilder.AddColumn<int>(
                name: "NextPayloadOrdinal",
                table: "FollowUpPrintJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "FollowUpPrintIdempotencyKeys",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "FollowUpPrintJobPayloads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobId = table.Column<int>(type: "int", nullable: false),
                    PayloadOrdinal = table.Column<int>(type: "int", nullable: false),
                    TransactionId = table.Column<int>(type: "int", nullable: false),
                    TargetDepartmentId = table.Column<int>(type: "int", nullable: true),
                    TargetEntityId = table.Column<int>(type: "int", nullable: true),
                    TargetEntityName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FollowUpSequence = table.Column<int>(type: "int", nullable: false),
                    ResponseDeadlineDays = table.Column<int>(type: "int", nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PartId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowUpPrintJobPayloads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FollowUpPrintJobPayloads_FollowUpPrintJobParts_PartId",
                        column: x => x.PartId,
                        principalTable: "FollowUpPrintJobParts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FollowUpPrintJobPayloads_FollowUpPrintJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "FollowUpPrintJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPrintIdempotencyKeys_UserId_Operation_Key",
                table: "FollowUpPrintIdempotencyKeys",
                columns: new[] { "UserId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_BatchJobPartId_TransactionId_TargetDepartmentId_TargetEntityId_FollowUpSequence",
                table: "FollowUpLetterPrintRecords",
                columns: new[] { "BatchJobPartId", "TransactionId", "TargetDepartmentId", "TargetEntityId", "FollowUpSequence" },
                unique: true,
                filter: "[BatchJobPartId] IS NOT NULL AND [TargetDepartmentId] IS NOT NULL AND [TargetEntityId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPrintJobPayloads_JobId_PayloadOrdinal",
                table: "FollowUpPrintJobPayloads",
                columns: new[] { "JobId", "PayloadOrdinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPrintJobPayloads_JobId_TransactionId_TargetDepartmentId_TargetEntityId_FollowUpSequence",
                table: "FollowUpPrintJobPayloads",
                columns: new[] { "JobId", "TransactionId", "TargetDepartmentId", "TargetEntityId", "FollowUpSequence" },
                unique: true,
                filter: "[TargetDepartmentId] IS NOT NULL AND [TargetEntityId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPrintJobPayloads_PartId",
                table: "FollowUpPrintJobPayloads",
                column: "PartId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FollowUpPrintJobPayloads");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpPrintIdempotencyKeys_UserId_Operation_Key",
                table: "FollowUpPrintIdempotencyKeys");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpLetterPrintRecords_BatchJobPartId_TransactionId_TargetDepartmentId_TargetEntityId_FollowUpSequence",
                table: "FollowUpLetterPrintRecords");

            migrationBuilder.DropColumn(
                name: "BatchSize",
                table: "FollowUpPrintJobs");

            migrationBuilder.DropColumn(
                name: "NextPayloadOrdinal",
                table: "FollowUpPrintJobs");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "FollowUpPrintIdempotencyKeys");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPrintIdempotencyKeys_UserId_Key_Operation",
                table: "FollowUpPrintIdempotencyKeys",
                columns: new[] { "UserId", "Key", "Operation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_BatchJobPartId",
                table: "FollowUpLetterPrintRecords",
                column: "BatchJobPartId");
        }
    }
}
