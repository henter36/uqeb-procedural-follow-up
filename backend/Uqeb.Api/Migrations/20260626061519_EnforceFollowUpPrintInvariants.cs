using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    /// <inheritdoc />
    public partial class EnforceFollowUpPrintInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM [FollowUpPrintIdempotencyKeys]
                    WHERE LEN([Key]) > 128 OR LEN([Operation]) > 64 OR LEN([RequestHash]) > 64
                )
                BEGIN
                    THROW 51001, 'Cannot reduce FollowUpPrintIdempotencyKeys column lengths because existing data exceeds the new limits.', 1;
                END
                """);

            migrationBuilder.DropIndex(
                name: "IX_FollowUpPrintIdempotencyKeys_UserId_Operation_Key",
                table: "FollowUpPrintIdempotencyKeys");

            migrationBuilder.AlterColumn<string>(
                name: "RequestHash",
                table: "FollowUpPrintIdempotencyKeys",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Operation",
                table: "FollowUpPrintIdempotencyKeys",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "FollowUpPrintIdempotencyKeys",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.Sql("""
                ;WITH RankedDefaults AS (
                    SELECT
                        [Id],
                        [TemplateType],
                        ROW_NUMBER() OVER (
                            PARTITION BY [TemplateType]
                            ORDER BY CASE WHEN [IsActive] = 1 THEN 0 ELSE 1 END, [SortOrder], [Id]
                        ) AS [rn]
                    FROM [LetterTemplates]
                    WHERE [IsDefault] = 1
                )
                UPDATE t
                SET [IsDefault] = CASE WHEN r.[rn] = 1 THEN 1 ELSE 0 END,
                    [IsActive] = CASE WHEN r.[rn] = 1 THEN 1 ELSE t.[IsActive] END
                FROM [LetterTemplates] t
                INNER JOIN [RankedDefaults] r ON r.[Id] = t.[Id];
                """);

            migrationBuilder.CreateIndex(
                name: "IX_LetterTemplates_TemplateType",
                table: "LetterTemplates",
                column: "TemplateType",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LetterTemplates_DefaultRequiresActive",
                table: "LetterTemplates",
                sql: "[IsDefault] = 0 OR [IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPrintIdempotencyKeys_UserId_Operation_Key",
                table: "FollowUpPrintIdempotencyKeys",
                columns: new[] { "UserId", "Operation", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LetterTemplates_TemplateType",
                table: "LetterTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LetterTemplates_DefaultRequiresActive",
                table: "LetterTemplates");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpPrintIdempotencyKeys_UserId_Operation_Key",
                table: "FollowUpPrintIdempotencyKeys");

            migrationBuilder.AlterColumn<string>(
                name: "RequestHash",
                table: "FollowUpPrintIdempotencyKeys",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldUnicode: false,
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Operation",
                table: "FollowUpPrintIdempotencyKeys",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "FollowUpPrintIdempotencyKeys",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPrintIdempotencyKeys_UserId_Operation_Key",
                table: "FollowUpPrintIdempotencyKeys",
                columns: new[] { "UserId", "Operation", "Key" },
                unique: true);
        }
    }
}
