using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    public partial class AddReportPaginationAndIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Transactions_CreatedAt' AND object_id = OBJECT_ID('Transactions'))
    CREATE INDEX [IX_Transactions_CreatedAt] ON [Transactions] ([CreatedAt]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Transactions_IncomingSourceType' AND object_id = OBJECT_ID('Transactions'))
    CREATE INDEX [IX_Transactions_IncomingSourceType] ON [Transactions] ([IncomingSourceType]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Transactions_ResponseCompleted' AND object_id = OBJECT_ID('Transactions'))
    CREATE INDEX [IX_Transactions_ResponseCompleted] ON [Transactions] ([ResponseCompleted]);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_CreatedAt",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_IncomingSourceType",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_ResponseCompleted",
                table: "Transactions");
        }
    }
}
