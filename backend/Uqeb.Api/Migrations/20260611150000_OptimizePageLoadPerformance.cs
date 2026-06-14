using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Uqeb.Api.Data;

#nullable disable

namespace Uqeb.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260611150000_OptimizePageLoadPerformance")]
    public class OptimizePageLoadPerformance : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Transactions_Status_IncomingDate' AND object_id = OBJECT_ID('Transactions'))
    CREATE INDEX [IX_Transactions_Status_IncomingDate] ON [Transactions] ([Status], [IncomingDate]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Transactions_RequiresResponse_ResponseCompleted_ResponseDueDate' AND object_id = OBJECT_ID('Transactions'))
    CREATE INDEX [IX_Transactions_RequiresResponse_ResponseCompleted_ResponseDueDate] ON [Transactions] ([RequiresResponse], [ResponseCompleted], [ResponseDueDate]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Transactions_CategoryId_IncomingDate' AND object_id = OBJECT_ID('Transactions'))
    CREATE INDEX [IX_Transactions_CategoryId_IncomingDate] ON [Transactions] ([CategoryId], [IncomingDate]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Transactions_IncomingSourceType_IncomingDate' AND object_id = OBJECT_ID('Transactions'))
    CREATE INDEX [IX_Transactions_IncomingSourceType_IncomingDate] ON [Transactions] ([IncomingSourceType], [IncomingDate]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Assignments_DepartmentId_Status_ReplyStatus' AND object_id = OBJECT_ID('Assignments'))
    CREATE INDEX [IX_Assignments_DepartmentId_Status_ReplyStatus] ON [Assignments] ([DepartmentId], [Status], [ReplyStatus]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TransactionOutgoingDepartments_DepartmentId_TransactionId' AND object_id = OBJECT_ID('TransactionOutgoingDepartments'))
    CREATE INDEX [IX_TransactionOutgoingDepartments_DepartmentId_TransactionId] ON [TransactionOutgoingDepartments] ([DepartmentId], [TransactionId]);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_Status_IncomingDate",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_RequiresResponse_ResponseCompleted_ResponseDueDate",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_CategoryId_IncomingDate",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_IncomingSourceType_IncomingDate",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Assignments_DepartmentId_Status_ReplyStatus",
                table: "Assignments");

            migrationBuilder.DropIndex(
                name: "IX_TransactionOutgoingDepartments_DepartmentId_TransactionId",
                table: "TransactionOutgoingDepartments");
        }
    }
}
