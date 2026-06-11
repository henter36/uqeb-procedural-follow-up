using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Uqeb.Api.Data;

#nullable disable

namespace Uqeb.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260611130000_OptimizeReportsQueriesAndPagination")]
    public class OptimizeReportsQueriesAndPagination : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Assignments_DueDate' AND object_id = OBJECT_ID('Assignments'))
    CREATE INDEX [IX_Assignments_DueDate] ON [Assignments] ([DueDate]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Assignments_TransactionId_RequiresReply_ReplyStatus_Status' AND object_id = OBJECT_ID('Assignments'))
    CREATE INDEX [IX_Assignments_TransactionId_RequiresReply_ReplyStatus_Status] ON [Assignments] ([TransactionId], [RequiresReply], [ReplyStatus], [Status]);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assignments_DueDate",
                table: "Assignments");

            migrationBuilder.DropIndex(
                name: "IX_Assignments_TransactionId_RequiresReply_ReplyStatus_Status",
                table: "Assignments");
        }
    }
}
