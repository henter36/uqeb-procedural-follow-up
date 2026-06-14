-- Verify performance indexes exist locally
SELECT
    expected.IndexName,
    CASE WHEN i.name IS NULL THEN 'MISSING' ELSE 'OK' END AS Status
FROM (VALUES
    ('IX_Assignments_Status_RequiresReply_ReplyStatus_DueDate_DepartmentId', 'Assignments'),
    ('IX_AuditLogs_TransactionId_CreatedAt', 'AuditLogs'),
    ('IX_FollowUps_TransactionId_CreatedAt', 'FollowUps'),
    ('IX_Attachments_TransactionId', 'Attachments')
) AS expected(IndexName, TableName)
LEFT JOIN sys.indexes i
    ON i.name = expected.IndexName
   AND i.object_id = OBJECT_ID(expected.TableName);

-- Refresh statistics after migrations
UPDATE STATISTICS [Transactions];
UPDATE STATISTICS [Assignments];
UPDATE STATISTICS [AuditLogs];
UPDATE STATISTICS [TransactionOutgoingDepartments];
UPDATE STATISTICS [FollowUps];
