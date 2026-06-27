using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatoryDefaultsToLetterTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultSignatoryName",
                table: "LetterTemplates",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultSignatoryPosition",
                table: "LetterTemplates",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultSignatoryRank",
                table: "LetterTemplates",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultSignatoryName",
                table: "LetterTemplates");

            migrationBuilder.DropColumn(
                name: "DefaultSignatoryPosition",
                table: "LetterTemplates");

            migrationBuilder.DropColumn(
                name: "DefaultSignatoryRank",
                table: "LetterTemplates");
        }
    }
}
