using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Uqeb.Api.Data;

#nullable disable

namespace Uqeb.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260611200000_UpdateStatisticsAndVerifyIndexes")]
    public class UpdateStatisticsAndVerifyIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DECLARE @missingIndexes TABLE (IndexName NVARCHAR(256));
INSERT INTO @missingIndexes (IndexName)
SELECT expected.Name
FROM (VALUES
    ('IX_Assignments_Status_RequiresReply_ReplyStatus_DueDate_DepartmentId'),
    ('IX_AuditLogs_TransactionId_CreatedAt'),
    ('IX_FollowUps_TransactionId_CreatedAt'),
    ('IX_Attachments_TransactionId')
) AS expected(Name)
WHERE NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.name = expected.Name
      AND i.object_id = OBJECT_ID(
          CASE expected.Name
              WHEN 'IX_Assignments_Status_RequiresReply_ReplyStatus_DueDate_DepartmentId' THEN 'Assignments'
              WHEN 'IX_AuditLogs_TransactionId_CreatedAt' THEN 'AuditLogs'
              WHEN 'IX_FollowUps_TransactionId_CreatedAt' THEN 'FollowUps'
              WHEN 'IX_Attachments_TransactionId' THEN 'Attachments'
          END
      )
);

IF EXISTS (SELECT 1 FROM @missingIndexes)
BEGIN
    DECLARE @msg NVARCHAR(MAX) = N'Missing performance indexes: ' +
        (SELECT STRING_AGG(IndexName, N', ') FROM @missingIndexes);
    THROW 51000, @msg, 1;
END

UPDATE STATISTICS [Transactions];
UPDATE STATISTICS [Assignments];
UPDATE STATISTICS [AuditLogs];
UPDATE STATISTICS [TransactionOutgoingDepartments];
UPDATE STATISTICS [FollowUps];
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
