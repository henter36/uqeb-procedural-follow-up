using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLetterNumberToAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LetterNumber",
                table: "Assignments",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LetterNumber",
                table: "Assignments");
        }
    }
}
