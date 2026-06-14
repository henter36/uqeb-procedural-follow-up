using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Uqeb.Api.Data;

#nullable disable

namespace Uqeb.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260611160000_FixDashboardAndAuditIndexes")]
    public class FixDashboardAndAuditIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Assignments_TransactionId' AND object_id = OBJECT_ID('Assignments'))
    CREATE INDEX [IX_Assignments_TransactionId] ON [Assignments] ([TransactionId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Assignments_DepartmentId_Status_RequiresReply_ReplyStatus_DueDate' AND object_id = OBJECT_ID('Assignments'))
    CREATE INDEX [IX_Assignments_DepartmentId_Status_RequiresReply_ReplyStatus_DueDate] ON [Assignments] ([DepartmentId], [Status], [RequiresReply], [ReplyStatus], [DueDate]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Assignments_Status_RequiresReply_ReplyStatus_DueDate_DepartmentId' AND object_id = OBJECT_ID('Assignments'))
    CREATE INDEX [IX_Assignments_Status_RequiresReply_ReplyStatus_DueDate_DepartmentId] ON [Assignments] ([Status], [RequiresReply], [ReplyStatus], [DueDate], [DepartmentId]) INCLUDE ([TransactionId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLogs_TransactionId_CreatedAt' AND object_id = OBJECT_ID('AuditLogs'))
    CREATE INDEX [IX_AuditLogs_TransactionId_CreatedAt] ON [AuditLogs] ([TransactionId], [CreatedAt] DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FollowUps_TransactionId_CreatedAt' AND object_id = OBJECT_ID('FollowUps'))
    CREATE INDEX [IX_FollowUps_TransactionId_CreatedAt] ON [FollowUps] ([TransactionId], [CreatedAt] DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Attachments_TransactionId' AND object_id = OBJECT_ID('Attachments'))
    CREATE INDEX [IX_Attachments_TransactionId] ON [Attachments] ([TransactionId]);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Assignments_TransactionId", table: "Assignments");
            migrationBuilder.DropIndex(name: "IX_Assignments_DepartmentId_Status_RequiresReply_ReplyStatus_DueDate", table: "Assignments");
            migrationBuilder.DropIndex(name: "IX_Assignments_Status_RequiresReply_ReplyStatus_DueDate_DepartmentId", table: "Assignments");
            migrationBuilder.DropIndex(name: "IX_AuditLogs_TransactionId_CreatedAt", table: "AuditLogs");
            migrationBuilder.DropIndex(name: "IX_FollowUps_TransactionId_CreatedAt", table: "FollowUps");
            migrationBuilder.DropIndex(name: "IX_Attachments_TransactionId", table: "Attachments");
        }
    }
}
