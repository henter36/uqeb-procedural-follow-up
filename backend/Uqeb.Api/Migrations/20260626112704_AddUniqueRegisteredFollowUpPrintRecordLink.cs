using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueRegisteredFollowUpPrintRecordLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM [FollowUpLetterPrintRecords]
                    WHERE [RegisteredFollowUpId] IS NOT NULL
                    GROUP BY [RegisteredFollowUpId]
                    HAVING COUNT(*) > 1
                )
                BEGIN
                    THROW 51032, 'Cannot add unique index: duplicate RegisteredFollowUpId links exist in FollowUpLetterPrintRecords.', 1;
                END;
                """);

            migrationBuilder.DropIndex(
                name: "IX_FollowUpLetterPrintRecords_RegisteredFollowUpId",
                table: "FollowUpLetterPrintRecords");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_RegisteredFollowUpId_Linked",
                table: "FollowUpLetterPrintRecords",
                column: "RegisteredFollowUpId",
                unique: true,
                filter: "[RegisteredFollowUpId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FollowUpLetterPrintRecords_RegisteredFollowUpId_Linked",
                table: "FollowUpLetterPrintRecords");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpLetterPrintRecords_RegisteredFollowUpId",
                table: "FollowUpLetterPrintRecords",
                column: "RegisteredFollowUpId",
                filter: "[RegisteredFollowUpId] IS NULL");
        }
    }
}
