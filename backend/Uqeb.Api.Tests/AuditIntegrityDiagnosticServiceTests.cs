using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class AuditIntegrityDiagnosticServiceTests
{
    private static AppDbContext CreateDb(string name)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));

        return new AppDbContext(builder.Options);
    }

    [Fact]
    public async Task GetHistoricalReportAsync_classifies_missing_transaction_and_repairable_assignment_links()
    {
        await using var db = CreateDb(nameof(GetHistoricalReportAsync_classifies_missing_transaction_and_repairable_assignment_links));
        db.AuditLogs.AddRange(
            new AuditLog
            {
                Id = 1,
                UserId = 1,
                Action = AuditAction.AddAssignment,
                EntityName = "Assignment",
                EntityId = null,
                TransactionId = null,
                NewValue = """{"departmentId":10}""",
                CreatedAt = DateTime.UtcNow
            },
            new AuditLog
            {
                Id = 2,
                UserId = 1,
                Action = AuditAction.AddAssignment,
                EntityName = "Assignment",
                EntityId = null,
                TransactionId = 5,
                NewValue = """{"departmentId":10}""",
                CreatedAt = DateTime.UtcNow
            });
        db.Assignments.Add(new Assignment
        {
            Id = 99,
            TransactionId = 5,
            DepartmentId = 10,
            AssignedDate = DateTime.UtcNow.Date,
            RequiresReply = true,
            ReplyStatus = ReplyStatus.Pending,
            Status = AssignmentStatus.Active,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new AuditIntegrityDiagnosticService(db);
        var report = await service.GetHistoricalReportAsync();

        Assert.Equal(AuditIntegrityDiagnosticService.ScanLimit, report.ScanLimit);
        Assert.Equal(2, report.TotalAuditsAvailable);
        Assert.Equal(2, report.TotalAuditsScanned);
        Assert.False(report.IsTruncated);
        Assert.Equal(1, report.MissingTransactionIdCount);
        Assert.Equal(2, report.MissingEntityIdCount);
        Assert.Equal(1, report.RepairableAssignmentLinkCount);
        Assert.Equal(0, report.RepairableOutgoingDepartmentLinkCount);
        Assert.Equal(1, report.TotalRepairableLinkCount);
        Assert.Contains(report.Issues, issue => issue.Classification == "missing_transaction_id");
        Assert.Contains(report.Issues, issue => issue.Classification == "repairable_assignment_link");
    }

    [Fact]
    public async Task LoadAssignmentsByTransactionAsync_batches_queries_for_large_transaction_id_sets()
    {
        await using var db = CreateDb(nameof(LoadAssignmentsByTransactionAsync_batches_queries_for_large_transaction_id_sets));
        var service = new AuditIntegrityDiagnosticService(db);

        var transactionIds = Enumerable.Range(1, 2501).ToArray();
        db.Assignments.AddRange(
            CreateAssignment(1, 10, 1001),
            CreateAssignment(1500, 10, 15001),
            CreateAssignment(2501, 11, 25011));
        await db.SaveChangesAsync();

        var result = await service.LoadAssignmentsByTransactionAsync(transactionIds, CancellationToken.None);

        Assert.Equal(3, AuditIntegrityDiagnosticService.GetAssignmentBatchQueryCount(transactionIds.Length));
        Assert.Equal(3, result.Count);
        Assert.True(result.ContainsKey(1));
        Assert.True(result.ContainsKey(1500));
        Assert.True(result.ContainsKey(2501));
    }

    [Fact]
    public async Task GetHistoricalReportAsync_reports_truncation_metadata_when_more_audits_exist_than_scan_limit()
    {
        await using var db = CreateDb(nameof(GetHistoricalReportAsync_reports_truncation_metadata_when_more_audits_exist_than_scan_limit));
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var audits = Enumerable.Range(1, AuditIntegrityDiagnosticService.ScanLimit + 100)
            .Select(i => new AuditLog
            {
                Id = i,
                UserId = 1,
                Action = AuditAction.Update,
                EntityName = "Transaction",
                EntityId = i,
                TransactionId = i,
                CreatedAt = now.AddMinutes(-i)
            })
            .ToList();
        db.AuditLogs.AddRange(audits);
        await db.SaveChangesAsync();

        var service = new AuditIntegrityDiagnosticService(db);
        var report = await service.GetHistoricalReportAsync();

        Assert.Equal(AuditIntegrityDiagnosticService.ScanLimit + 100, report.TotalAuditsAvailable);
        Assert.Equal(AuditIntegrityDiagnosticService.ScanLimit, report.TotalAuditsScanned);
        Assert.True(report.IsTruncated);
        Assert.Equal(now.AddMinutes(-1), report.NewestAuditAt);
        Assert.Equal(now.AddMinutes(-AuditIntegrityDiagnosticService.ScanLimit), report.OldestAuditAt);
    }

    [Fact]
    public async Task GetHistoricalReportAsync_uses_stable_order_when_created_at_matches()
    {
        await using var db = CreateDb(nameof(GetHistoricalReportAsync_uses_stable_order_when_created_at_matches));
        var createdAt = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        db.AuditLogs.AddRange(
            new AuditLog { Id = 10, UserId = 1, Action = AuditAction.AddAssignment, EntityName = "Assignment", EntityId = null, TransactionId = null, NewValue = """{"departmentId":1}""", CreatedAt = createdAt },
            new AuditLog { Id = 20, UserId = 1, Action = AuditAction.AddAssignment, EntityName = "Assignment", EntityId = null, TransactionId = null, NewValue = """{"departmentId":2}""", CreatedAt = createdAt });
        await db.SaveChangesAsync();

        var service = new AuditIntegrityDiagnosticService(db);
        var report = await service.GetHistoricalReportAsync();

        Assert.Equal(createdAt, report.NewestAuditAt);
        Assert.Equal(createdAt, report.OldestAuditAt);
        Assert.Equal(20, report.Issues.First().AuditLogId);
    }

    [Fact]
    public async Task GetHistoricalReportAsync_counts_each_classification_once()
    {
        await using var db = CreateDb(nameof(GetHistoricalReportAsync_counts_each_classification_once));
        db.AuditLogs.AddRange(
            new AuditLog
            {
                Id = 1,
                UserId = 1,
                Action = AuditAction.AddAssignment,
                EntityName = "Assignment",
                TransactionId = null,
                EntityId = null,
                NewValue = """{"departmentId":10}""",
                CreatedAt = DateTime.UtcNow
            },
            new AuditLog
            {
                Id = 2,
                UserId = 1,
                Action = AuditAction.Update,
                EntityName = "TransactionOutgoingDepartments",
                TransactionId = 5,
                EntityId = null,
                CreatedAt = DateTime.UtcNow
            },
            new AuditLog
            {
                Id = 3,
                UserId = 1,
                Action = AuditAction.AddAssignment,
                EntityName = "Assignment",
                TransactionId = 6,
                EntityId = null,
                NewValue = """{"departmentId":10}""",
                CreatedAt = DateTime.UtcNow
            },
            new AuditLog
            {
                Id = 4,
                UserId = 1,
                Action = AuditAction.AddAssignment,
                EntityName = "Assignment",
                TransactionId = 7,
                EntityId = null,
                NewValue = """{"departmentId":10}""",
                CreatedAt = DateTime.UtcNow
            },
            new AuditLog
            {
                Id = 5,
                UserId = 1,
                Action = AuditAction.Update,
                EntityName = "Transaction",
                TransactionId = 8,
                EntityId = null,
                CreatedAt = DateTime.UtcNow
            });
        db.Assignments.AddRange(
            CreateAssignment(6, 10, 601),
            CreateAssignment(7, 10, 701),
            CreateAssignment(7, 10, 702));
        await db.SaveChangesAsync();

        var service = new AuditIntegrityDiagnosticService(db);
        var report = await service.GetHistoricalReportAsync();

        Assert.Equal(1, report.RepairableAssignmentLinkCount);
        Assert.Equal(1, report.RepairableOutgoingDepartmentLinkCount);
        Assert.Equal(2, report.TotalRepairableLinkCount);
        Assert.Equal(3, report.AmbiguousCount);
        Assert.Equal(5, report.Issues.Count);
        Assert.Equal(1, report.Issues.Count(issue => issue.Classification == "missing_transaction_id"));
        Assert.Equal(1, report.Issues.Count(issue => issue.Classification == "repairable_missing_entity_id"));
        Assert.Equal(1, report.Issues.Count(issue => issue.Classification == "repairable_assignment_link"));
        Assert.Equal(1, report.Issues.Count(issue => issue.Classification == "ambiguous_assignment_link"));
        Assert.Equal(1, report.Issues.Count(issue => issue.Classification == "ambiguous_missing_entity_id"));
    }

    [Fact]
    public async Task GetHistoricalReportAsync_does_not_modify_tracked_entities()
    {
        await using var db = CreateDb(nameof(GetHistoricalReportAsync_does_not_modify_tracked_entities));
        var audit = new AuditLog
        {
            Id = 1,
            UserId = 1,
            Action = AuditAction.Update,
            EntityName = "Transaction",
            EntityId = 1,
            TransactionId = 1,
            CreatedAt = DateTime.UtcNow
        };
        var assignment = CreateAssignment(1, 10);
        db.AuditLogs.Add(audit);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        db.Attach(audit);
        db.Attach(assignment);
        var auditEntry = db.Entry(audit);
        var assignmentEntry = db.Entry(assignment);
        var savesBefore = db.ChangeTracker.Entries().Count(entry => entry.State is EntityState.Modified or EntityState.Deleted);

        var service = new AuditIntegrityDiagnosticService(db);
        _ = await service.GetHistoricalReportAsync();

        Assert.Equal(savesBefore, db.ChangeTracker.Entries().Count(entry => entry.State is EntityState.Modified or EntityState.Deleted));
        Assert.Equal(EntityState.Unchanged, auditEntry.State);
        Assert.Equal(EntityState.Unchanged, assignmentEntry.State);
        Assert.Equal(0, await db.SaveChangesAsync());
    }

    private static Assignment CreateAssignment(int transactionId, int departmentId, int id = 0) => new()
    {
        Id = id,
        TransactionId = transactionId,
        DepartmentId = departmentId,
        AssignedDate = DateTime.UtcNow.Date,
        RequiresReply = true,
        ReplyStatus = ReplyStatus.Pending,
        Status = AssignmentStatus.Active,
        CreatedById = 1,
        CreatedAt = DateTime.UtcNow
    };
}
