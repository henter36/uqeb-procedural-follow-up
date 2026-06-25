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
    [Fact]
    public async Task GetHistoricalReportAsync_classifies_missing_transaction_and_repairable_assignment_links()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(nameof(GetHistoricalReportAsync_classifies_missing_transaction_and_repairable_assignment_links))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var db = new AppDbContext(options);
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

        Assert.Equal(2, report.TotalAuditsScanned);
        Assert.Equal(1, report.MissingTransactionIdCount);
        Assert.Equal(2, report.MissingEntityIdCount);
        Assert.Contains(report.Issues, issue => issue.Classification == "missing_transaction_id");
        Assert.Contains(report.Issues, issue => issue.Classification == "repairable_assignment_link");
    }
}
