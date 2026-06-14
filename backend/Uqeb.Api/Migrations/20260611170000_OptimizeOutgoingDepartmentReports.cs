using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Uqeb.Api.Data;

#nullable disable

namespace Uqeb.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260611170000_OptimizeOutgoingDepartmentReports")]
    public class OptimizeOutgoingDepartmentReports : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Transactions_Status_IncomingDate_ClosedAt' AND object_id = OBJECT_ID('Transactions'))
    CREATE INDEX [IX_Transactions_Status_IncomingDate_ClosedAt] ON [Transactions] ([Status], [IncomingDate], [ClosedAt]);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_Status_IncomingDate_ClosedAt",
                table: "Transactions");
        }
    }
}
