using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddScopeDepartmentIdAndTargetShapeConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add ScopeDepartmentId to FollowUpPrintJobs for safe department scoping.
            migrationBuilder.AddColumn<int>(
                name: "ScopeDepartmentId",
                table: "FollowUpPrintJobs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPrintJobs_ScopeDepartmentId",
                table: "FollowUpPrintJobs",
                column: "ScopeDepartmentId");

            // 2. Verify no existing FollowUpPrintJobPayloads violate the XOR target shape rule.
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM [FollowUpPrintJobPayloads]
                    WHERE NOT (
                        ([TargetDepartmentId] IS NOT NULL AND [TargetEntityId] IS NULL) OR
                        ([TargetEntityId] IS NOT NULL AND [TargetDepartmentId] IS NULL)
                    )
                )
                BEGIN
                    THROW 51030, 'Cannot add XOR target shape constraint to FollowUpPrintJobPayloads: existing rows have both or neither target field set.', 1;
                END
                """);

            // 3. Replace the combined non-filtered unique index on FollowUpPrintJobPayloads
            //    with two precise filtered unique indexes, one per target shape.
            migrationBuilder.DropIndex(
                name: "IX_FollowUpPrintJobPayloads_JobId_TransactionId_TargetDepartmentId_TargetEntityId_FollowUpSequence",
                table: "FollowUpPrintJobPayloads");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPrintJobPayloads_JobId_Tx_Dept_Seq",
                table: "FollowUpPrintJobPayloads",
                columns: new[] { "JobId", "TransactionId", "TargetDepartmentId", "FollowUpSequence" },
                unique: true,
                filter: "[TargetDepartmentId] IS NOT NULL AND [TargetEntityId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPrintJobPayloads_JobId_Tx_Entity_Seq",
                table: "FollowUpPrintJobPayloads",
                columns: new[] { "JobId", "TransactionId", "TargetEntityId", "FollowUpSequence" },
                unique: true,
                filter: "[TargetEntityId] IS NOT NULL AND [TargetDepartmentId] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpPrintJobPayloads_TargetShape",
                table: "FollowUpPrintJobPayloads",
                sql: "([TargetDepartmentId] IS NOT NULL AND [TargetEntityId] IS NULL) OR ([TargetEntityId] IS NOT NULL AND [TargetDepartmentId] IS NULL)");

            // 4. Verify no existing FollowUpLetterPrintRecords violate the XOR target shape rule.
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM [FollowUpLetterPrintRecords]
                    WHERE NOT (
                        ([TargetDepartmentId] IS NOT NULL AND [TargetEntityId] IS NULL) OR
                        ([TargetEntityId] IS NOT NULL AND [TargetDepartmentId] IS NULL)
                    )
                )
                BEGIN
                    THROW 51031, 'Cannot add XOR target shape constraint to FollowUpLetterPrintRecords: existing rows have both or neither target field set.', 1;
                END
                """);

            // 5. Replace the combined non-filtered unique index on FollowUpLetterPrintRecords
            //    with two precise filtered unique indexes for batch records.
            migrationBuilder.DropIndex(
                name: "IX_FollowUpLetterPrintRecords_BatchJobPartId_TransactionId_TargetDepartmentId_TargetEntityId_FollowUpSequence",
                table: "FollowUpLetterPrintRecords");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_Part_Tx_Dept_Seq",
                table: "FollowUpLetterPrintRecords",
                columns: new[] { "BatchJobPartId", "TransactionId", "TargetDepartmentId", "FollowUpSequence" },
                unique: true,
                filter: "[BatchJobPartId] IS NOT NULL AND [TargetDepartmentId] IS NOT NULL AND [TargetEntityId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_Part_Tx_Entity_Seq",
                table: "FollowUpLetterPrintRecords",
                columns: new[] { "BatchJobPartId", "TransactionId", "TargetEntityId", "FollowUpSequence" },
                unique: true,
                filter: "[BatchJobPartId] IS NOT NULL AND [TargetEntityId] IS NOT NULL AND [TargetDepartmentId] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpLetterPrintRecords_TargetShape",
                table: "FollowUpLetterPrintRecords",
                sql: "([TargetDepartmentId] IS NOT NULL AND [TargetEntityId] IS NULL) OR ([TargetEntityId] IS NOT NULL AND [TargetDepartmentId] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpLetterPrintRecords_TargetShape",
                table: "FollowUpLetterPrintRecords");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpLetterPrintRecords_Part_Tx_Entity_Seq",
                table: "FollowUpLetterPrintRecords");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpLetterPrintRecords_Part_Tx_Dept_Seq",
                table: "FollowUpLetterPrintRecords");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_BatchJobPartId_TransactionId_TargetDepartmentId_TargetEntityId_FollowUpSequence",
                table: "FollowUpLetterPrintRecords",
                columns: new[] { "BatchJobPartId", "TransactionId", "TargetDepartmentId", "TargetEntityId", "FollowUpSequence" },
                unique: true);

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpPrintJobPayloads_TargetShape",
                table: "FollowUpPrintJobPayloads");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpPrintJobPayloads_JobId_Tx_Entity_Seq",
                table: "FollowUpPrintJobPayloads");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpPrintJobPayloads_JobId_Tx_Dept_Seq",
                table: "FollowUpPrintJobPayloads");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPrintJobPayloads_JobId_TransactionId_TargetDepartmentId_TargetEntityId_FollowUpSequence",
                table: "FollowUpPrintJobPayloads",
                columns: new[] { "JobId", "TransactionId", "TargetDepartmentId", "TargetEntityId", "FollowUpSequence" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_FollowUpPrintJobs_ScopeDepartmentId",
                table: "FollowUpPrintJobs");

            migrationBuilder.DropColumn(
                name: "ScopeDepartmentId",
                table: "FollowUpPrintJobs");
        }
    }
}
