using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDataQualityReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataQualityReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IssueKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TransactionId = table.Column<int>(type: "int", nullable: true),
                    RuleCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsReviewed = table.Column<bool>(type: "bit", nullable: false),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataQualityReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DataQualityReviews_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataQualityReviews_IssueKey",
                table: "DataQualityReviews",
                column: "IssueKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataQualityReviews_ReviewedByUserId",
                table: "DataQualityReviews",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DataQualityReviews_TransactionId_RuleCode",
                table: "DataQualityReviews",
                columns: new[] { "TransactionId", "RuleCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataQualityReviews");
        }
    }
}
