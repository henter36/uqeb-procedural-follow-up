using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportOverdueSqlServerIntegrationTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("UQEB_REFERENCE_DATA_TEST_CONNECTION");

    private static bool IsSqlServerAvailable()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            return false;

        try
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task OverdueQuery_MatchesSnapshotAndKpi_OnSqlServer()
    {
        if (!IsSqlServerAvailable())
            return;

        var today = DateTime.UtcNow.Date;
        var databaseName = $"Uqeb_Overdue_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(ConnectionString!) { InitialCatalog = databaseName };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var user = new User
        {
            Username = "overdue-sql",
            PasswordHash = "hash",
            FullName = "Overdue SQL",
            Role = UserRole.Admin,
            IsActive = true,
        };
        db.Users.Add(user);
        var dept = new Department { Name = "إدارة", IsActive = true };
        db.Departments.Add(dept);
        await db.SaveChangesAsync();

        await SeedMixedScenarioAsync(db, user.Id, dept.Id, today);

        var query = InstitutionalReportOverdueQuery.ApplyOverdueFilter(db.Transactions.AsNoTracking(), today);
        var queryIds = await query.Select(t => t.IncomingNumber).OrderBy(x => x).ToListAsync();

        var rows = await db.Transactions.AsNoTracking()
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

        var snapshots = rows.Select(r => InstitutionalReportSnapshotQuery.MapRowToSnapshot(r, today)).ToList();
        var expectedIds = snapshots
            .Where(s => InstitutionalReportMetricsCalculator.IsOverdue(s, today))
            .Select(s => s.IncomingNumber)
            .OrderBy(x => x)
            .ToList();

        var metrics = InstitutionalReportMetricsCalculator.Calculate(snapshots, today);

        Assert.Equal(expectedIds, queryIds);
        Assert.Equal(expectedIds.Count, metrics.OverdueCount);
        Assert.Contains("IN-pending-overdue", queryIds);
        Assert.DoesNotContain("IN-mixed-not-overdue", queryIds);
        Assert.DoesNotContain("IN-replied-future", queryIds);
        Assert.DoesNotContain("IN-inactive-old", queryIds);
    }

    private static async Task SeedMixedScenarioAsync(
        AppDbContext db,
        int userId,
        int deptId,
        DateTime today)
    {
        async Task AddAsync(string label, TransactionStatus status, params (AssignmentStatus st, bool req, ReplyStatus reply, DateTime? due)[] assignments)
        {
            var tx = new Transaction
            {
                InternalTrackingNumber = $"INT-{label}",
                IncomingNumber = $"IN-{label}",
                IncomingDate = today.AddDays(-5),
                Subject = label,
                Status = status,
                CreatedById = userId,
            };
            db.Transactions.Add(tx);
            await db.SaveChangesAsync();

            foreach (var (st, req, reply, due) in assignments)
            {
                db.Assignments.Add(new Assignment
                {
                    TransactionId = tx.Id,
                    DepartmentId = deptId,
                    Status = st,
                    RequiresReply = req,
                    ReplyStatus = reply,
                    DueDate = due,
                    AssignedDate = today.AddDays(-5),
                    CreatedById = userId,
                });
            }

            await db.SaveChangesAsync();
        }

        await AddAsync(
            "mixed-not-overdue",
            TransactionStatus.New,
            (AssignmentStatus.Active, false, ReplyStatus.Pending, today.AddDays(-10)),
            (AssignmentStatus.Active, true, ReplyStatus.Pending, today.AddDays(5)));

        await AddAsync(
            "replied-future",
            TransactionStatus.New,
            (AssignmentStatus.Active, true, ReplyStatus.Replied, today.AddDays(-10)),
            (AssignmentStatus.Active, true, ReplyStatus.Pending, today.AddDays(3)));

        await AddAsync(
            "pending-overdue",
            TransactionStatus.New,
            (AssignmentStatus.Active, true, ReplyStatus.Pending, today.AddDays(-2)));

        await AddAsync(
            "inactive-old",
            TransactionStatus.New,
            (AssignmentStatus.Completed, true, ReplyStatus.Pending, today.AddDays(-30)),
            (AssignmentStatus.Active, true, ReplyStatus.Pending, today.AddDays(2)));
    }
}
