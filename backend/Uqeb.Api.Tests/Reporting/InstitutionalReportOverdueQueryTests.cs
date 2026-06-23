using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Reports;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportOverdueQueryTests
{
    [Fact]
    public async Task OverdueFilter_MatchesIsOverdueOnSameDataset()
    {
        var dbName = $"overdue-query-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var user = new User
        {
            Username = "overdue-test",
            PasswordHash = "hash",
            FullName = "Overdue Test",
            Role = UserRole.Admin,
            IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var dept = new Department { Name = "إدارة", IsActive = true };
        db.Departments.Add(dept);
        await db.SaveChangesAsync();

        var today = DateTime.UtcNow.Date;
        var scenarios = new[]
        {
            ("open-not-overdue", TransactionStatus.New, today.AddDays(2), false, (DateTime?)null, false),
            ("status-overdue", TransactionStatus.Overdue, today, false, (DateTime?)null, true),
            ("response-due-past", TransactionStatus.New, today.AddDays(-3), true, today.AddDays(-1), true),
            ("assignment-due-past", TransactionStatus.New, today, true, today.AddDays(-2), true),
            ("closed-old-due", TransactionStatus.Closed, today.AddDays(-10), true, today.AddDays(-20), false),
        };

        foreach (var (label, status, incomingDate, requiresResponse, responseDue, _) in scenarios)
        {
            var tx = new Transaction
            {
                InternalTrackingNumber = $"INT-{label}",
                IncomingNumber = $"IN-{label}",
                IncomingDate = incomingDate,
                Subject = label,
                Status = status,
                RequiresResponse = requiresResponse,
                ResponseDueDate = responseDue,
                ClosedAt = status == TransactionStatus.Closed ? today.AddDays(-1) : null,
                CreatedById = user.Id,
            };
            db.Transactions.Add(tx);
            await db.SaveChangesAsync();

            if (label == "assignment-due-past")
            {
                db.Assignments.Add(new Assignment
                {
                    TransactionId = tx.Id,
                    DepartmentId = dept.Id,
                    Status = AssignmentStatus.Active,
                    RequiresReply = true,
                    ReplyStatus = ReplyStatus.Pending,
                    DueDate = today.AddDays(-2),
                    AssignedDate = today.AddDays(-5),
                    CreatedById = user.Id,
                });
                await db.SaveChangesAsync();
            }
        }

        var query = db.Transactions.AsNoTracking();
        query = InstitutionalReportSnapshotQuery.ApplyReportTypeFilter(
            query,
            InstitutionalReportType.OverdueTransactions,
            singleTransactionId: null);
        var filteredIds = await query.Select(t => t.IncomingNumber).ToListAsync();

        var allRows = await db.Transactions.AsNoTracking()
            .Select(t => new InstitutionalReportSnapshotQuery.SnapshotRow
            {
                Id = t.Id,
                InternalTrackingNumber = t.InternalTrackingNumber,
                IncomingNumber = t.IncomingNumber,
                IncomingDate = t.IncomingDate,
                Subject = t.Subject,
                Priority = t.Priority,
                Status = t.Status,
                RequiresResponse = t.RequiresResponse,
                ResponseCompleted = t.ResponseCompleted,
                ResponseDueDate = t.ResponseDueDate,
                ClosedAt = t.ClosedAt,
                UpdatedAt = t.UpdatedAt,
                CreatedAt = t.CreatedAt,
                Assignments = t.Assignments.Select(a => new InstitutionalReportSnapshotQuery.AssignmentRow
                {
                    DepartmentId = a.DepartmentId,
                    DepartmentName = a.Department.Name,
                    RequiresReply = a.RequiresReply,
                    ReplyStatus = a.ReplyStatus,
                    Status = a.Status,
                    DueDate = a.DueDate,
                }).ToList(),
                OutgoingDepartments = t.OutgoingDepartments.Select(o => new InstitutionalReportSnapshotQuery.DepartmentRow
                {
                    DepartmentId = o.DepartmentId,
                    DepartmentName = o.Department.Name,
                }).ToList(),
            })
            .ToListAsync();

        var expected = allRows
            .Select(r => InstitutionalReportSnapshotQuery.MapRowToSnapshot(r, today))
            .Where(s => InstitutionalReportMetricsCalculator.IsOverdue(s, today))
            .Select(s => s.IncomingNumber)
            .OrderBy(x => x)
            .ToList();

        Assert.Equal(expected, filteredIds.OrderBy(x => x).ToList());
        Assert.Contains("IN-status-overdue", filteredIds);
        Assert.Contains("IN-response-due-past", filteredIds);
        Assert.Contains("IN-assignment-due-past", filteredIds);
        Assert.DoesNotContain("IN-open-not-overdue", filteredIds);
        Assert.DoesNotContain("IN-closed-old-due", filteredIds);
    }
}
