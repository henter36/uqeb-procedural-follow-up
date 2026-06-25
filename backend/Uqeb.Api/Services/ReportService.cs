using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Reports;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public interface IReportService
{
    Task<DashboardSummaryDto> GetDashboardSummaryAsync();
    Task<List<TransactionListDto>> GetDashboardActionRequiredAsync();
    Task<List<DepartmentOverdueDto>> GetDashboardTopOverdueDepartmentsAsync();
    Task<List<ExternalPartyReportDto>> GetDashboardTopIncomingPartiesAsync();
    Task<List<CategoryDistributionDto>> GetDashboardCategoryDistributionAsync();
    Task<List<StatusDistributionDto>> GetDashboardStatusDistributionAsync();
    Task<DashboardDto> GetDashboardAsync();
    Task<List<TransactionListDto>> GetOverdueAsync(ReportFilterRequest? filter = null);
    Task<List<TransactionListDto>> GetOpenAsync(ReportFilterRequest? filter = null);
    Task<List<TransactionListDto>> GetWaitingRepliesAsync(ReportFilterRequest? filter = null);
    Task<List<TransactionListDto>> GetPendingResponseAsync(ReportFilterRequest? filter = null);
    Task<List<TransactionListDto>> GetResponseRequiredAsync(ReportFilterRequest? filter = null);
    Task<List<TransactionListDto>> GetOverdueResponsesAsync(ReportFilterRequest? filter = null);
    Task<List<TransactionListDto>> GetPendingAssignmentsAsync(ReportFilterRequest? filter = null);
    Task<List<TransactionListDto>> GetPartialRepliesAsync(ReportFilterRequest? filter = null);
    Task<List<DepartmentReportDto>> GetByDepartmentAsync(ReportFilterRequest? filter = null);
    Task<List<ExternalPartyReportDto>> GetByExternalPartyAsync(ReportFilterRequest? filter = null);
    Task<List<ExternalPartyReportDto>> GetByIncomingPartyAsync(ReportFilterRequest? filter = null);
    Task<List<CategoryDistributionDto>> GetByCategoryAsync(ReportFilterRequest? filter = null);
    Task<List<OutgoingPartyReportDto>> GetByOutgoingPartyAsync(ReportFilterRequest? filter = null);
    Task<List<OutgoingDepartmentReportDto>> GetByOutgoingDepartmentAsync(ReportFilterRequest? filter = null);
    Task<List<DepartmentSummaryDto>> GetDepartmentSummaryAsync(ReportFilterRequest? filter = null);
    Task<List<DepartmentSummaryDto>> GetDepartmentIncomingClosedAsync(ReportFilterRequest? filter = null);
    Task<byte[]> ExportDepartmentIncomingClosedExcelAsync(ReportFilterRequest? filter = null);
    Task<byte[]> ExportDepartmentIncomingClosedPdfAsync(ReportFilterRequest? filter = null);
    Task<List<MonthlyReportDto>> GetMonthlyAsync(int year);
    Task<byte[]> ExportToExcelAsync(string reportType, ReportFilterRequest? filter = null, CancellationToken cancellationToken = default);
    Task<PagedResult<ReportTransactionRowDto>> GetResponseRequiredDetailsAsync(ReportPagedFilterRequest filter);
    Task<PagedResult<ReportTransactionRowDto>> GetOverdueResponsesDetailsAsync(ReportPagedFilterRequest filter);
    Task<PagedResult<ReportTransactionRowDto>> GetOpenAssignmentsDetailsAsync(ReportPagedFilterRequest filter);
    Task<PagedResult<ReportTransactionRowDto>> GetPartialRepliesDetailsAsync(ReportPagedFilterRequest filter);
    Task<PagedResult<ReportTransactionRowDto>> GetOverdueDetailsAsync(ReportPagedFilterRequest filter);
    Task<PagedResult<ReportTransactionRowDto>> GetWaitingReplyDetailsAsync(ReportPagedFilterRequest filter);
    Task<PagedResult<ReportTransactionRowDto>> GetOpenDetailsAsync(ReportPagedFilterRequest filter);
    Task<byte[]> ExportReportDetailsExcelAsync(string reportType, ReportPagedFilterRequest filter, bool currentPageOnly, CancellationToken cancellationToken = default);
    Task<ReportSectionCountsDto> GetPageSummaryAsync(ReportFilterRequest? filter = null);
}

public class ReportService : IReportService
{
    private const int ExportBatchSize = 1000;

    private readonly AppDbContext _db;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ReportService(AppDbContext db, IDbContextFactory<AppDbContext> dbFactory)
    {
        _db = db;
        _dbFactory = dbFactory;
    }

    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync()
    {
        var stats = await LoadDashboardAggregateStatsAsync(DateTime.UtcNow);
        return stats?.ToSummaryDto() ?? new DashboardSummaryDto();
    }

    public Task<List<TransactionListDto>> GetDashboardActionRequiredAsync() =>
        LoadActionRequiredAsync(DateTime.UtcNow);

    public Task<List<DepartmentOverdueDto>> GetDashboardTopOverdueDepartmentsAsync() =>
        LoadTopOverdueDepartmentsAsync(DateTime.UtcNow);

    public async Task<List<ExternalPartyReportDto>> GetDashboardTopIncomingPartiesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Transactions.AsNoTracking()
            .Where(t => t.IncomingFrom != null)
            .GroupBy(t => t.IncomingFrom!)
            .Select(g => new ExternalPartyReportDto { PartyName = g.Key, TransactionCount = g.Count() })
            .OrderByDescending(x => x.TransactionCount)
            .Take(5)
            .ToListAsync();
    }

    public async Task<List<CategoryDistributionDto>> GetDashboardCategoryDistributionAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Transactions.AsNoTracking()
            .GroupBy(t => new
            {
                t.CategoryId,
                Name = t.CategoryEntity != null ? t.CategoryEntity.Name : (t.Category ?? "بدون تصنيف")
            })
            .Select(g => new CategoryDistributionDto
            {
                CategoryId = g.Key.CategoryId,
                CategoryName = g.Key.Name,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ToListAsync();
    }

    public async Task<List<StatusDistributionDto>> GetDashboardStatusDistributionAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Transactions.AsNoTracking()
            .GroupBy(t => t.Status)
            .Select(g => new StatusDistributionDto { Status = g.Key.ToString(), Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync();
    }

    public async Task<DashboardDto> GetDashboardAsync()
    {
        var now = DateTime.UtcNow;
        var statsTask = LoadDashboardAggregateStatsAsync(now);
        var topOverdueTask = GetDashboardTopOverdueDepartmentsAsync();
        var topIncomingTask = GetDashboardTopIncomingPartiesAsync();
        var byCategoryTask = GetDashboardCategoryDistributionAsync();
        var byStatusTask = GetDashboardStatusDistributionAsync();
        var actionRequiredTask = GetDashboardActionRequiredAsync();

        await Task.WhenAll(statsTask, topOverdueTask, topIncomingTask, byCategoryTask, byStatusTask, actionRequiredTask);

        var stats = await statsTask ?? DashboardAggregateStats.Empty;

        return new DashboardDto
        {
            TotalOpen = stats.TotalOpen,
            NewCount = stats.NewCount,
            WaitingForReply = stats.WaitingForReply,
            PartiallyReplied = stats.PartiallyReplied,
            OverdueCount = stats.OverdueCount,
            RequiresResponse = stats.RequiresResponsePending,
            RequiresResponsePending = stats.RequiresResponsePending,
            ResponseOverdueCount = stats.ResponseOverdueCount,
            ResponseCompleted = stats.ResponseCompleted,
            ReadyForResponse = stats.ReadyForResponse,
            ClosedCount = stats.ClosedCount,
            ClosedThisMonth = stats.ClosedThisMonth,
            AverageCompletionDays = stats.AverageCompletionDays,
            TopOverdueDepartments = await topOverdueTask,
            TopIncomingParties = await topIncomingTask,
            ByCategory = await byCategoryTask,
            ByStatus = await byStatusTask,
            ActionRequired = await actionRequiredTask
        };
    }

    public Task<List<TransactionListDto>> GetOverdueAsync(ReportFilterRequest? filter = null) =>
        GetLegacyReportListAsync(filter, GetOverdueDetailsAsync);

    public Task<List<TransactionListDto>> GetOpenAsync(ReportFilterRequest? filter = null) =>
        GetLegacyReportListAsync(filter, GetOpenDetailsAsync);

    public Task<List<TransactionListDto>> GetWaitingRepliesAsync(ReportFilterRequest? filter = null) =>
        GetLegacyReportListAsync(filter, GetWaitingReplyDetailsAsync);

    public Task<List<TransactionListDto>> GetPendingResponseAsync(ReportFilterRequest? filter = null) =>
        GetLegacyReportListAsync(filter, GetResponseRequiredDetailsAsync);

    public Task<List<TransactionListDto>> GetResponseRequiredAsync(ReportFilterRequest? filter = null) =>
        GetPendingResponseAsync(filter);

    public Task<List<TransactionListDto>> GetOverdueResponsesAsync(ReportFilterRequest? filter = null) =>
        GetLegacyReportListAsync(filter, GetOverdueResponsesDetailsAsync);

    public Task<List<TransactionListDto>> GetPendingAssignmentsAsync(ReportFilterRequest? filter = null) =>
        GetLegacyReportListAsync(filter, GetOpenAssignmentsDetailsAsync);

    public Task<List<TransactionListDto>> GetPartialRepliesAsync(ReportFilterRequest? filter = null) =>
        GetLegacyReportListAsync(filter, GetPartialRepliesDetailsAsync);

    public Task<PagedResult<ReportTransactionRowDto>> GetResponseRequiredDetailsAsync(ReportPagedFilterRequest filter) =>
        GetReportDetailsPagedAsync(filter, q =>
        {
            var now = DateTime.UtcNow;
            return q.Where(t => t.RequiresResponse && !t.ResponseCompleted);
        }, orderByOverdue: false);

    public Task<PagedResult<ReportTransactionRowDto>> GetOverdueResponsesDetailsAsync(ReportPagedFilterRequest filter) =>
        GetReportDetailsPagedAsync(filter, q =>
        {
            var now = DateTime.UtcNow;
            return q.Where(t => t.RequiresResponse && !t.ResponseCompleted && t.ResponseDueDate.HasValue && t.ResponseDueDate < now);
        }, orderByOverdue: true);

    public Task<PagedResult<ReportTransactionRowDto>> GetOpenAssignmentsDetailsAsync(ReportPagedFilterRequest filter) =>
        GetReportDetailsPagedAsync(filter, q => q.Where(t =>
            t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied && a.Status == AssignmentStatus.Active)),
            orderByOverdue: false);

    public Task<PagedResult<ReportTransactionRowDto>> GetPartialRepliesDetailsAsync(ReportPagedFilterRequest filter) =>
        GetReportDetailsPagedAsync(filter, ApplyPartialRepliesFilter, orderByOverdue: false);

    public Task<PagedResult<ReportTransactionRowDto>> GetOverdueDetailsAsync(ReportPagedFilterRequest filter) =>
        GetReportDetailsPagedAsync(filter, q =>
        {
            var now = DateTime.UtcNow;
            return q.Where(t =>
                (t.RequiresResponse && !t.ResponseCompleted && t.ResponseDueDate.HasValue && t.ResponseDueDate < now) ||
                t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                    && a.Status == AssignmentStatus.Active && a.DueDate.HasValue && a.DueDate < now));
        }, orderByOverdue: true);

    public Task<PagedResult<ReportTransactionRowDto>> GetWaitingReplyDetailsAsync(ReportPagedFilterRequest filter) =>
        GetReportDetailsPagedAsync(filter, q => q.Where(t =>
            t.Status == TransactionStatus.WaitingForReply || t.Status == TransactionStatus.PartiallyReplied),
            orderByOverdue: false);

    public Task<PagedResult<ReportTransactionRowDto>> GetOpenDetailsAsync(ReportPagedFilterRequest filter) =>
        GetReportDetailsPagedAsync(filter, q => q.Where(t =>
            t.Status != TransactionStatus.Closed && t.Status != TransactionStatus.Cancelled && t.Status != TransactionStatus.Archived),
            orderByOverdue: false);

    public async Task<byte[]> ExportReportDetailsExcelAsync(
        string reportType,
        ReportPagedFilterRequest filter,
        bool currentPageOnly,
        CancellationToken cancellationToken = default)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("التقرير");
        WriteReportExcelHeaders(ws);
        var row = 2;

        if (currentPageOnly)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paged = await GetDetailsByReportTypeAsync(reportType, filter);
            WriteReportExcelRows(ws, row, paged.Items);
        }
        else
        {
            DateTime? lastCreatedAt = null;
            int? lastId = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = await GetReportDetailsExportBatchAsync(reportType, filter, lastCreatedAt, lastId, ExportBatchSize);
                if (batch.Count == 0)
                    break;

                row = WriteReportExcelRows(ws, row, batch);
                var last = batch[^1];
                lastCreatedAt = last.CreatedAt;
                lastId = last.Id;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        ws.RightToLeft = true;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteReportExcelHeaders(IXLWorksheet ws)
    {
        ws.Cell(1, 1).Value = "رقم التتبع";
        ws.Cell(1, 2).Value = "رقم الوارد";
        ws.Cell(1, 3).Value = "تاريخ الوارد";
        ws.Cell(1, 4).Value = "الموضوع";
        ws.Cell(1, 5).Value = "الإدارة";
        ws.Cell(1, 6).Value = "الجهة الوارد منها";
        ws.Cell(1, 7).Value = "الحالة";
        ws.Cell(1, 8).Value = "التصنيف";
        ws.Cell(1, 9).Value = "الأولوية";
        ws.Cell(1, 10).Value = "أيام التأخر";
    }

    private static int WriteReportExcelRows(IXLWorksheet ws, int startRow, IReadOnlyList<ReportTransactionRowDto> data)
    {
        var row = startRow;
        foreach (var t in data)
        {
            ws.Cell(row, 1).Value = t.InternalTrackingNumber;
            ws.Cell(row, 2).Value = t.IncomingNumber;
            ws.Cell(row, 3).Value = t.IncomingDate.ToString("yyyy-MM-dd");
            ws.Cell(row, 4).Value = t.Subject;
            ws.Cell(row, 5).Value = t.OutgoingDepartmentsDisplayNames.Count > 0
                ? string.Join("، ", t.OutgoingDepartmentsDisplayNames) : "-";
            ws.Cell(row, 6).Value = t.IncomingFromDisplayName ?? "";
            ws.Cell(row, 7).Value = t.Status;
            ws.Cell(row, 8).Value = t.CategoryName ?? "";
            ws.Cell(row, 9).Value = t.Priority;
            ws.Cell(row, 10).Value = t.DaysOverdue.HasValue ? t.DaysOverdue.Value : "";
            row++;
        }
        return row;
    }

    private async Task<List<ReportTransactionRowDto>> GetReportDetailsExportBatchAsync(
        string reportType,
        ReportPagedFilterRequest filter,
        DateTime? lastCreatedAt,
        int? lastId,
        int batchSize)
    {
        var now = DateTime.UtcNow;
        var query = ApplyReportFilter(_db.Transactions.AsNoTracking(), filter);
        query = ApplyExportReportPredicate(reportType, query, now);

        if (lastCreatedAt.HasValue && lastId.HasValue)
        {
            query = query.Where(t =>
                t.CreatedAt < lastCreatedAt.Value
                || (t.CreatedAt == lastCreatedAt.Value && t.Id < lastId.Value));
        }

        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Take(batchSize)
            .Select(t => new ReportDetailRowProjection
            {
                Id = t.Id,
                InternalTrackingNumber = t.InternalTrackingNumber,
                IncomingNumber = t.IncomingNumber,
                IncomingDate = t.IncomingDate,
                Subject = t.Subject,
                IncomingFrom = t.IncomingFrom,
                IncomingSourceType = t.IncomingSourceType,
                IncomingFromPartyName = t.IncomingFromParty != null ? t.IncomingFromParty.Name : null,
                IncomingFromDepartmentName = t.IncomingFromDepartment != null ? t.IncomingFromDepartment.Name : null,
                CategoryName = t.CategoryEntity != null ? t.CategoryEntity.Name : t.Category,
                Priority = t.Priority,
                Status = t.Status,
                ResponseType = t.ResponseType,
                ResponseDueDate = t.ResponseDueDate,
                RequiresResponse = t.RequiresResponse,
                ResponseCompleted = t.ResponseCompleted,
                ResponseDueDays = t.ResponseDueDays,
                CreatedAt = t.CreatedAt,
                OutgoingDepartmentsDisplayNames = t.OutgoingDepartments.Select(o => o.Department.Name).ToList(),
                AssignmentDueDate = t.Assignments
                    .Where(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                        && a.Status == AssignmentStatus.Active && a.DueDate.HasValue)
                    .Min(a => (DateTime?)a.DueDate),
                LastFollowUpDate = t.FollowUps.Any()
                    ? t.FollowUps.Max(f => f.CreatedAt > f.FollowUpDate ? f.CreatedAt : f.FollowUpDate)
                    : (DateTime?)null
            })
            .ToListAsync();

        return MapReportDetailRows(rows, now);
    }

    private static IQueryable<Models.Entities.Transaction> ApplyExportReportPredicate(
        string reportType,
        IQueryable<Models.Entities.Transaction> query,
        DateTime now) =>
        reportType.ToLower() switch
        {
            "response-required" or "pending-response" => query.Where(t => t.RequiresResponse && !t.ResponseCompleted),
            "overdue-responses" => query.Where(t =>
                t.RequiresResponse && !t.ResponseCompleted
                && t.ResponseDueDate.HasValue && t.ResponseDueDate < now),
            "open-assignments" or "pending-assignments" => query.Where(t =>
                t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                    && a.Status == AssignmentStatus.Active)),
            "partial-replies" => ApplyPartialRepliesFilter(query),
            "overdue" => query.Where(t =>
                (t.RequiresResponse && !t.ResponseCompleted && t.ResponseDueDate.HasValue && t.ResponseDueDate < now)
                || t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                    && a.Status == AssignmentStatus.Active && a.DueDate.HasValue && a.DueDate < now)),
            "waiting-reply" or "waiting" or "waiting-replies" => query.Where(t =>
                t.Status == TransactionStatus.WaitingForReply || t.Status == TransactionStatus.PartiallyReplied),
            "open" => query.Where(t =>
                t.Status != TransactionStatus.Closed && t.Status != TransactionStatus.Cancelled && t.Status != TransactionStatus.Archived),
            _ => query.Where(t =>
                t.Status != TransactionStatus.Closed && t.Status != TransactionStatus.Cancelled && t.Status != TransactionStatus.Archived)
        };

    private Task<PagedResult<ReportTransactionRowDto>> GetDetailsByReportTypeAsync(string reportType, ReportPagedFilterRequest filter) =>
        reportType.ToLower() switch
        {
            "response-required" or "pending-response" => GetResponseRequiredDetailsAsync(filter),
            "overdue-responses" => GetOverdueResponsesDetailsAsync(filter),
            "open-assignments" or "pending-assignments" => GetOpenAssignmentsDetailsAsync(filter),
            "partial-replies" => GetPartialRepliesDetailsAsync(filter),
            "overdue" => GetOverdueDetailsAsync(filter),
            "waiting-reply" or "waiting" or "waiting-replies" => GetWaitingReplyDetailsAsync(filter),
            "open" => GetOpenDetailsAsync(filter),
            _ => GetOpenDetailsAsync(filter)
        };

    public async Task<List<DepartmentReportDto>> GetByDepartmentAsync(ReportFilterRequest? filter = null)
    {
        var now = DateTime.UtcNow;
        var query = _db.Assignments.AsNoTracking().AsQueryable();
        if (filter?.DepartmentId.HasValue == true)
            query = query.Where(a => a.DepartmentId == filter.DepartmentId);

        return await query
            .GroupBy(a => new { a.DepartmentId, a.Department.Name })
            .Select(g => new DepartmentReportDto
            {
                DepartmentId = g.Key.DepartmentId,
                DepartmentName = g.Key.Name,
                TotalAssigned = g.Count(),
                Pending = g.Count(a => a.RequiresReply && a.ReplyStatus == ReplyStatus.Pending),
                Replied = g.Count(a => a.ReplyStatus == ReplyStatus.Replied),
                Overdue = g.Count(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied && a.DueDate < now)
            })
            .ToListAsync();
    }

    public async Task<List<ExternalPartyReportDto>> GetByExternalPartyAsync(ReportFilterRequest? filter = null) =>
        await GetByIncomingPartyAsync(filter);

    public async Task<List<ExternalPartyReportDto>> GetByIncomingPartyAsync(ReportFilterRequest? filter = null)
    {
        var query = ApplyReportFilter(_db.Transactions.AsNoTracking(), filter);
        return await query
            .Select(t => new
            {
                PartyName = t.IncomingSourceType == IncomingSourceType.Internal
                    ? (t.IncomingFromDepartment != null ? t.IncomingFromDepartment.Name : t.IncomingFrom)
                    : (t.IncomingFromParty != null ? t.IncomingFromParty.Name : t.IncomingFrom)
            })
            .GroupBy(x => x.PartyName ?? "غير محدد")
            .Select(g => new ExternalPartyReportDto { PartyName = g.Key, TransactionCount = g.Count() })
            .OrderByDescending(r => r.TransactionCount)
            .ToListAsync();
    }

    public async Task<List<CategoryDistributionDto>> GetByCategoryAsync(ReportFilterRequest? filter = null)
    {
        var query = ApplyReportFilter(_db.Transactions.AsNoTracking(), filter);
        return await query
            .GroupBy(t => new
            {
                t.CategoryId,
                Name = t.CategoryEntity != null ? t.CategoryEntity.Name : (t.Category ?? "بدون تصنيف")
            })
            .Select(g => new CategoryDistributionDto
            {
                CategoryId = g.Key.CategoryId,
                CategoryName = g.Key.Name,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ToListAsync();
    }

    public Task<ReportSectionCountsDto> GetPageSummaryAsync(ReportFilterRequest? filter = null) =>
        GetSectionCountsAsync(filter, DateTime.UtcNow);

    private async Task<ReportSectionCountsDto> GetSectionCountsAsync(ReportFilterRequest? filter, DateTime now)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = ApplyReportFilter(db.Transactions.AsNoTracking(), filter);
        var counts = await query
            .GroupBy(_ => 1)
            .Select(g => new ReportSectionCountsDto
            {
                ResponseRequired = g.Count(t => t.RequiresResponse && !t.ResponseCompleted),
                OverdueResponses = g.Count(t => t.RequiresResponse && !t.ResponseCompleted
                    && t.ResponseDueDate.HasValue && t.ResponseDueDate.Value < now),
                OpenAssignments = g.Count(t => t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                    && a.Status == AssignmentStatus.Active)),
                PartialReplies = g.Count(t =>
                    t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus == ReplyStatus.Replied)
                    && t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                        && a.Status == AssignmentStatus.Active)),
                Overdue = g.Count(t =>
                    (t.RequiresResponse && !t.ResponseCompleted
                        && t.ResponseDueDate.HasValue && t.ResponseDueDate.Value < now)
                    || t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                        && a.Status == AssignmentStatus.Active && a.DueDate.HasValue && a.DueDate.Value < now)),
                WaitingReply = g.Count(t => t.Status == TransactionStatus.WaitingForReply
                    || t.Status == TransactionStatus.PartiallyReplied),
                Open = g.Count(t => t.Status != TransactionStatus.Closed
                    && t.Status != TransactionStatus.Cancelled
                    && t.Status != TransactionStatus.Archived)
            })
            .FirstOrDefaultAsync();

        return counts ?? new ReportSectionCountsDto();
    }

    private async Task<DashboardAggregateStats?> LoadDashboardAggregateStatsAsync(DateTime now)
    {
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var stats = await db.Transactions.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new DashboardAggregateStats
            {
                TotalOpen = g.Count(t => t.Status != TransactionStatus.Closed
                    && t.Status != TransactionStatus.Cancelled
                    && t.Status != TransactionStatus.Archived),
                NewCount = g.Count(t => t.Status == TransactionStatus.New),
                RequiresResponsePending = g.Count(t => t.RequiresResponse && !t.ResponseCompleted),
                ResponseOverdueCount = g.Count(t => t.RequiresResponse && !t.ResponseCompleted
                    && t.ResponseDueDate.HasValue && t.ResponseDueDate.Value < now),
                WaitingForReply = g.Count(t => t.Status == TransactionStatus.WaitingForReply),
                PartiallyReplied = g.Count(t => t.Status == TransactionStatus.PartiallyReplied),
                ReadyForResponse = g.Count(t => t.Status == TransactionStatus.ReadyForResponse),
                ResponseCompleted = g.Count(t => t.ResponseCompleted),
                ClosedCount = g.Count(t => t.Status == TransactionStatus.Closed),
                ClosedThisMonth = g.Count(t => t.Status == TransactionStatus.Closed
                    && t.ClosedAt.HasValue && t.ClosedAt.Value >= monthStart),
                ClosedWithCompletionDays = g.Count(t => t.Status == TransactionStatus.Closed && t.ClosedAt.HasValue),
                OverdueCount = g.Count(t =>
                    (t.RequiresResponse && !t.ResponseCompleted
                        && t.ResponseDueDate.HasValue && t.ResponseDueDate.Value < now)
                    || t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                        && a.Status == AssignmentStatus.Active && a.DueDate.HasValue && a.DueDate.Value < now))
            })
            .FirstOrDefaultAsync();

        if (stats == null)
            return null;

        var completionDays = await db.Transactions.AsNoTracking()
            .Where(t => t.Status == TransactionStatus.Closed && t.ClosedAt.HasValue)
            .Select(t => new { t.CreatedAt, ClosedAt = t.ClosedAt!.Value })
            .ToListAsync();

        stats.AverageCompletionDaysRaw = completionDays.Count == 0
            ? 0
            : completionDays.Average(t => Math.Max(0, (t.ClosedAt.Date - t.CreatedAt.Date).Days));

        return stats;
    }

    private sealed class DashboardAggregateStats
    {
        public static DashboardAggregateStats Empty { get; } = new();

        public int TotalOpen { get; init; }
        public int NewCount { get; init; }
        public int RequiresResponsePending { get; init; }
        public int ResponseOverdueCount { get; init; }
        public int WaitingForReply { get; init; }
        public int PartiallyReplied { get; init; }
        public int ReadyForResponse { get; init; }
        public int ResponseCompleted { get; init; }
        public int ClosedCount { get; init; }
        public int ClosedThisMonth { get; init; }
        public int ClosedWithCompletionDays { get; init; }
        public double AverageCompletionDaysRaw { get; set; }
        public int OverdueCount { get; init; }

        public double AverageCompletionDays =>
            ClosedWithCompletionDays == 0 ? 0 : Math.Round(AverageCompletionDaysRaw, 1);

        public DashboardSummaryDto ToSummaryDto() => new()
        {
            TotalOpen = TotalOpen,
            RequiresResponsePending = RequiresResponsePending,
            ResponseOverdueCount = ResponseOverdueCount,
            WaitingForReply = WaitingForReply,
            PartiallyReplied = PartiallyReplied,
            ReadyForResponse = ReadyForResponse,
            ClosedThisMonth = ClosedThisMonth,
            AverageCompletionDays = AverageCompletionDays
        };
    }

    private static IQueryable<Models.Entities.Transaction> ApplyPartialRepliesFilter(
        IQueryable<Models.Entities.Transaction> query) =>
        query.Where(t =>
            t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus == ReplyStatus.Replied)
            && t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                && a.Status == AssignmentStatus.Active));

    public async Task<List<OutgoingPartyReportDto>> GetByOutgoingPartyAsync(ReportFilterRequest? filter = null) =>
        (await GetByOutgoingDepartmentAsync(filter))
            .Select(d => new OutgoingPartyReportDto
            {
                ExternalPartyId = d.DepartmentId,
                PartyName = d.DepartmentName,
                TransactionCount = d.TransactionCount
            })
            .ToList();

    public async Task<List<OutgoingDepartmentReportDto>> GetByOutgoingDepartmentAsync(ReportFilterRequest? filter = null)
    {
        var now = DateTime.UtcNow;
        var rows = await LoadOutgoingDepartmentLinkRowsAsync(filter);
        var assignmentOverdueTx = await LoadAssignmentOverdueTransactionIdsAsync(now);

        return rows
            .GroupBy(r => new { r.DepartmentId, r.DepartmentName })
            .Select(g =>
            {
                var byTransaction = g.GroupBy(x => x.TransactionId).Select(x => x.First()).ToList();
                return new OutgoingDepartmentReportDto
                {
                    DepartmentId = g.Key.DepartmentId,
                    DepartmentName = g.Key.DepartmentName,
                    TransactionCount = byTransaction.Count,
                    OpenCount = byTransaction.Count(r => IsOpenTransactionStatus(r.Status)),
                    ClosedCount = byTransaction.Count(r => r.Status == TransactionStatus.Closed),
                    OverdueCount = byTransaction.Count(r =>
                        IsResponseOverdueRow(r, now) || assignmentOverdueTx.Contains(r.TransactionId))
                };
            })
            .OrderByDescending(x => x.TransactionCount)
            .ToList();
    }

    public async Task<List<DepartmentSummaryDto>> GetDepartmentSummaryAsync(ReportFilterRequest? filter = null)
    {
        var now = DateTime.UtcNow;
        var dateFrom = filter?.DateFrom;
        var dateTo = filter?.DateTo;

        var rows = await LoadDepartmentSummaryLinkRowsAsync(dateFrom, dateTo);
        var assignmentOverdueTx = await LoadAssignmentOverdueTransactionIdsAsync(now);

        var results = rows
            .GroupBy(r => new { r.DepartmentId, r.DepartmentName })
            .Select(g =>
            {
                var byTransaction = g.GroupBy(x => x.TransactionId).Select(x => x.First()).ToList();
                return new DepartmentSummaryDto
                {
                    DepartmentId = g.Key.DepartmentId,
                    DepartmentName = g.Key.DepartmentName,
                    TotalIncoming = byTransaction.Count,
                    OpenCount = byTransaction.Count(r => IsOpenTransactionStatus(r.Status)),
                    WaitingForReplyCount = byTransaction.Count(r => IsWaitingForReplyStatus(r.Status)),
                    OverdueCount = byTransaction.Count(r =>
                        IsResponseOverdueRow(r, now) || assignmentOverdueTx.Contains(r.TransactionId)),
                    ClosedCount = 0
                };
            })
            .ToList();

        var closedByDept = await LoadClosedCountByDepartmentAsync(dateFrom, dateTo);

        var resultMap = results.ToDictionary(r => r.DepartmentId);
        foreach (var closed in closedByDept)
        {
            if (resultMap.TryGetValue(closed.DepartmentId, out var row))
                row.ClosedCount = closed.ClosedCount;
            else
            {
                results.Add(new DepartmentSummaryDto
                {
                    DepartmentId = closed.DepartmentId,
                    DepartmentName = closed.DepartmentName,
                    ClosedCount = closed.ClosedCount
                });
            }
        }

        results = results
            .Where(x => x.TotalIncoming > 0 || x.ClosedCount > 0)
            .OrderByDescending(x => x.TotalIncoming)
            .ToList();

        foreach (var row in results)
            row.CloseRate = row.TotalIncoming > 0 ? Math.Round((double)row.ClosedCount / row.TotalIncoming * 100, 1) : 0;

        return results;
    }

    public Task<List<DepartmentSummaryDto>> GetDepartmentIncomingClosedAsync(ReportFilterRequest? filter = null) =>
        GetDepartmentSummaryAsync(filter);

    public async Task<byte[]> ExportDepartmentIncomingClosedExcelAsync(ReportFilterRequest? filter = null)
    {
        var data = await GetDepartmentSummaryAsync(filter);
        return DepartmentReportExporter.ToExcel(data, filter?.DateFrom, filter?.DateTo);
    }

    public async Task<byte[]> ExportDepartmentIncomingClosedPdfAsync(ReportFilterRequest? filter = null)
    {
        var data = await GetDepartmentSummaryAsync(filter);
        return DepartmentReportExporter.ToPdf(data, filter?.DateFrom, filter?.DateTo);
    }

    public async Task<List<MonthlyReportDto>> GetMonthlyAsync(int year)
    {
        var incoming = await _db.Transactions
            .Where(t => t.IncomingDate.Year == year)
            .GroupBy(t => t.IncomingDate.Month)
            .Select(g => new { Month = g.Key, Count = g.Count() })
            .ToListAsync();

        var outgoing = await _db.Transactions
            .Where(t => t.OutgoingDate.HasValue && t.OutgoingDate.Value.Year == year)
            .GroupBy(t => t.OutgoingDate!.Value.Month)
            .Select(g => new { Month = g.Key, Count = g.Count() })
            .ToListAsync();

        return Enumerable.Range(1, 12).Select(m => new MonthlyReportDto
        {
            Year = year,
            Month = m,
            IncomingCount = incoming.FirstOrDefault(i => i.Month == m)?.Count ?? 0,
            OutgoingCount = outgoing.FirstOrDefault(o => o.Month == m)?.Count ?? 0
        }).ToList();
    }

    private static ReportPagedFilterRequest ToPagedFilter(ReportFilterRequest? filter) => new()
    {
        DateFrom = filter?.DateFrom,
        DateTo = filter?.DateTo,
        Status = filter?.Status,
        CategoryId = filter?.CategoryId,
        DepartmentId = filter?.DepartmentId,
        IncomingPartyId = filter?.IncomingPartyId,
        OutgoingPartyId = filter?.OutgoingPartyId,
        OutgoingDepartmentId = filter?.OutgoingDepartmentId,
        IncomingSourceType = filter?.IncomingSourceType,
        Search = filter?.Search,
        Page = 1,
        PageSize = ReportPageSize.Max
    };

    private async Task<List<TransactionListDto>> GetLegacyReportListAsync(
        ReportFilterRequest? filter,
        Func<ReportPagedFilterRequest, Task<PagedResult<ReportTransactionRowDto>>> loader)
    {
        var paged = await loader(ToPagedFilter(filter));
        return MapReportRowsToList(paged.Items);
    }

    private static List<TransactionListDto> MapReportRowsToList(IEnumerable<ReportTransactionRowDto> rows) =>
        rows.Select(r => new TransactionListDto
        {
            Id = r.Id,
            InternalTrackingNumber = r.InternalTrackingNumber,
            IncomingNumber = r.IncomingNumber,
            IncomingDate = r.IncomingDate,
            Subject = r.Subject,
            IncomingFrom = r.IncomingFromDisplayName,
            OutgoingDepartmentNames = r.OutgoingDepartmentsDisplayNames,
            OutgoingPartyNames = r.OutgoingDepartmentsDisplayNames,
            Status = r.Status,
            Priority = r.Priority,
            CategoryName = r.CategoryName,
            ResponseDueDate = r.ResponseDueDate,
            DaysRemainingForResponse = r.DaysRemainingForResponse,
            DaysSinceIncoming = r.DaysSinceIncoming,
            DaysSinceLastFollowUp = r.DaysSinceLastFollowUp,
            LastFollowUpDate = r.LastFollowUpDate,
            ResponseTimingStatus = r.ResponseTimingStatus,
            ResponseTimingLabel = r.ResponseTimingLabel,
            IsOverdue = r.IsOverdue,
            IsResponseOverdue = r.IsOverdue && r.ResponseDueDate.HasValue,
            CreatedAt = r.CreatedAt
        }).ToList();

    public Task<byte[]> ExportToExcelAsync(string reportType, ReportFilterRequest? filter = null, CancellationToken cancellationToken = default) =>
        ExportReportDetailsExcelAsync(reportType, ToPagedFilter(filter), currentPageOnly: true, cancellationToken);

    private async Task<PagedResult<ReportTransactionRowDto>> GetReportDetailsPagedAsync(
        ReportPagedFilterRequest filter,
        Func<IQueryable<Models.Entities.Transaction>, IQueryable<Models.Entities.Transaction>> applyPredicate,
        bool orderByOverdue)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = ReportPageSize.Normalize(filter.PageSize);
        var now = DateTime.UtcNow;

        var query = ApplyReportFilter(_db.Transactions.AsNoTracking(), filter);
        query = applyPredicate(query);

        var totalCount = await query.CountAsync();

        query = orderByOverdue
            ? query.OrderBy(t =>
                t.RequiresResponse && !t.ResponseCompleted && t.ResponseDueDate.HasValue
                    ? t.ResponseDueDate!.Value
                    : t.Assignments
                        .Where(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                            && a.Status == AssignmentStatus.Active && a.DueDate.HasValue)
                        .Min(a => a.DueDate) ?? DateTime.MaxValue)
            : query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.IncomingDate);

        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new ReportDetailRowProjection
            {
                Id = t.Id,
                InternalTrackingNumber = t.InternalTrackingNumber,
                IncomingNumber = t.IncomingNumber,
                IncomingDate = t.IncomingDate,
                Subject = t.Subject,
                IncomingFrom = t.IncomingFrom,
                IncomingSourceType = t.IncomingSourceType,
                IncomingFromPartyName = t.IncomingFromParty != null ? t.IncomingFromParty.Name : null,
                IncomingFromDepartmentName = t.IncomingFromDepartment != null ? t.IncomingFromDepartment.Name : null,
                CategoryName = t.CategoryEntity != null ? t.CategoryEntity.Name : t.Category,
                Priority = t.Priority,
                Status = t.Status,
                ResponseType = t.ResponseType,
                ResponseDueDate = t.ResponseDueDate,
                RequiresResponse = t.RequiresResponse,
                ResponseCompleted = t.ResponseCompleted,
                ResponseDueDays = t.ResponseDueDays,
                CreatedAt = t.CreatedAt,
                OutgoingDepartmentsDisplayNames = t.OutgoingDepartments.Select(o => o.Department.Name).ToList(),
                AssignmentDueDate = t.Assignments
                    .Where(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                        && a.Status == AssignmentStatus.Active && a.DueDate.HasValue)
                    .Min(a => (DateTime?)a.DueDate),
                LastFollowUpDate = t.FollowUps.Any()
                    ? t.FollowUps.Max(f => f.CreatedAt > f.FollowUpDate ? f.CreatedAt : f.FollowUpDate)
                    : (DateTime?)null
            })
            .ToListAsync();

        var items = MapReportDetailRows(rows, now);

        return PagedResult<ReportTransactionRowDto>.Create(items, totalCount, page, pageSize);
    }

    private static List<ReportTransactionRowDto> MapReportDetailRows(
        List<ReportDetailRowProjection> rows, DateTime now) =>
        rows.Select(t =>
        {
            var incomingFrom = t.IncomingSourceType switch
            {
                IncomingSourceType.Internal => t.IncomingFromDepartmentName ?? t.IncomingFrom,
                IncomingSourceType.External => t.IncomingFromPartyName ?? t.IncomingFrom,
                _ => t.IncomingFrom
            };
            var assignmentDue = t.AssignmentDueDate;
            var isResponseOverdue = t.RequiresResponse && !t.ResponseCompleted
                && t.ResponseDueDate.HasValue && t.ResponseDueDate.Value < now;
            var isAssignmentOverdue = assignmentDue.HasValue && assignmentDue.Value < now;
            var isOverdue = isResponseOverdue || isAssignmentOverdue;
            int? daysOverdue = null;
            if (isResponseOverdue && t.ResponseDueDate.HasValue)
                daysOverdue = (int)(now.Date - t.ResponseDueDate.Value.Date).TotalDays;
            else if (isAssignmentOverdue)
                daysOverdue = (int)(now.Date - assignmentDue!.Value.Date).TotalDays;

            var timeline = TransactionTimelineHelper.Compute(
                t.IncomingDate,
                t.ResponseDueDate,
                t.ResponseDueDays,
                t.RequiresResponse,
                t.ResponseCompleted,
                t.LastFollowUpDate?.Date,
                now.Date);

            return new ReportTransactionRowDto
            {
                Id = t.Id,
                InternalTrackingNumber = t.InternalTrackingNumber,
                IncomingNumber = t.IncomingNumber,
                IncomingDate = t.IncomingDate,
                IncomingHijriDate = FormatHijriDate(t.IncomingDate),
                Subject = t.Subject,
                IncomingFromDisplayName = incomingFrom,
                OutgoingDepartmentsDisplayNames = t.OutgoingDepartmentsDisplayNames,
                CategoryName = t.CategoryName,
                Priority = t.Priority.ToString(),
                Status = t.Status.ToString(),
                ResponseType = t.ResponseType.ToString(),
                ResponseDueDate = t.ResponseDueDate,
                AssignmentDueDate = assignmentDue,
                DaysRemainingForResponse = timeline.DaysRemainingForResponse,
                DaysSinceIncoming = timeline.DaysSinceIncoming,
                DaysSinceLastFollowUp = timeline.DaysSinceLastFollowUp,
                LastFollowUpDate = timeline.LastFollowUpDate,
                ResponseTimingStatus = timeline.ResponseTimingStatus,
                ResponseTimingLabel = timeline.ResponseTimingLabel,
                DaysOverdue = daysOverdue,
                CreatedAt = t.CreatedAt,
                IsOverdue = isOverdue
            };
        }).ToList();

    private sealed class ReportDetailRowProjection
    {
        public int Id { get; set; }
        public string InternalTrackingNumber { get; set; } = string.Empty;
        public string IncomingNumber { get; set; } = string.Empty;
        public DateTime IncomingDate { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string? IncomingFrom { get; set; }
        public IncomingSourceType IncomingSourceType { get; set; }
        public string? IncomingFromPartyName { get; set; }
        public string? IncomingFromDepartmentName { get; set; }
        public string? CategoryName { get; set; }
        public Priority Priority { get; set; }
        public TransactionStatus Status { get; set; }
        public ResponseType ResponseType { get; set; }
        public DateTime? ResponseDueDate { get; set; }
        public int? ResponseDueDays { get; set; }
        public bool RequiresResponse { get; set; }
        public bool ResponseCompleted { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<string> OutgoingDepartmentsDisplayNames { get; set; } = new();
        public DateTime? AssignmentDueDate { get; set; }
        public DateTime? LastFollowUpDate { get; set; }
    }

    private async Task<List<TransactionListDto>> LoadActionRequiredAsync(DateTime now)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tx = db.Transactions.AsNoTracking();
        var open = tx.Where(t => t.Status != TransactionStatus.Closed
            && t.Status != TransactionStatus.Cancelled
            && t.Status != TransactionStatus.Archived);

        var actionRequiredIds = new List<int>();

        async Task AppendIdsAsync(IQueryable<Models.Entities.Transaction> source, int limit)
        {
            if (limit <= 0 || actionRequiredIds.Count >= 10) return;
            var ids = await source
                .Where(t => !actionRequiredIds.Contains(t.Id))
                .Select(t => t.Id)
                .Take(limit)
                .ToListAsync();
            actionRequiredIds.AddRange(ids);
        }

        await AppendIdsAsync(
            open.Where(t => t.RequiresResponse && !t.ResponseCompleted
                    && t.ResponseDueDate.HasValue && t.ResponseDueDate.Value < now)
                .OrderBy(t => t.ResponseDueDate),
            10);

        await AppendIdsAsync(
            open.Where(t => t.RequiresResponse && !t.ResponseCompleted)
                .OrderBy(t => t.ResponseDueDate ?? DateTime.MaxValue)
                .ThenByDescending(t => t.Priority),
            10 - actionRequiredIds.Count);

        await AppendIdsAsync(
            open.Where(t => t.Status == TransactionStatus.ReadyForResponse)
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.IncomingDate),
            10 - actionRequiredIds.Count);

        await AppendIdsAsync(
            open.Where(t => t.Status == TransactionStatus.WaitingForReply
                || t.Status == TransactionStatus.PartiallyReplied)
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.IncomingDate),
            10 - actionRequiredIds.Count);

        if (actionRequiredIds.Count < 10)
        {
            var overdueAssignmentIds = await db.Assignments.AsNoTracking()
                .Where(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                    && a.Status == AssignmentStatus.Active
                    && a.DueDate.HasValue && a.DueDate.Value < now)
                .OrderBy(a => a.DueDate)
                .Select(a => a.TransactionId)
                .Distinct()
                .Take(20)
                .ToListAsync();

            foreach (var id in overdueAssignmentIds)
            {
                if (actionRequiredIds.Count >= 10) break;
                if (!actionRequiredIds.Contains(id))
                    actionRequiredIds.Add(id);
            }
        }

        actionRequiredIds = actionRequiredIds.Take(10).ToList();
        if (actionRequiredIds.Count == 0)
            return new List<TransactionListDto>();

        var rows = await tx
            .Where(t => actionRequiredIds.Contains(t.Id))
            .Select(t => new TransactionListDto
            {
                Id = t.Id,
                InternalTrackingNumber = t.InternalTrackingNumber,
                IncomingNumber = t.IncomingNumber,
                IncomingDate = t.IncomingDate,
                Subject = t.Subject,
                IncomingFrom = t.IncomingSourceType == IncomingSourceType.Internal
                    ? (t.IncomingFromDepartment != null ? t.IncomingFromDepartment.Name : t.IncomingFrom)
                    : (t.IncomingFromParty != null ? t.IncomingFromParty.Name : t.IncomingFrom),
                IncomingSourceType = t.IncomingSourceType.ToString(),
                OutgoingNumber = t.OutgoingNumber,
                OutgoingDate = t.OutgoingDate,
                Status = t.Status.ToString(),
                Priority = t.Priority.ToString(),
                CategoryName = t.CategoryEntity != null ? t.CategoryEntity.Name : t.Category,
                RequiresResponse = t.RequiresResponse,
                ResponseCompleted = t.ResponseCompleted,
                ResponseDueDate = t.ResponseDueDate,
                ResponseDays = t.ResponseDueDays,
                IsArchived = t.IsArchived,
                CreatedByName = t.CreatedBy != null ? t.CreatedBy.FullName : "",
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();

        var assignmentRows = await db.Assignments.AsNoTracking()
            .Where(a => actionRequiredIds.Contains(a.TransactionId))
            .Select(a => new
            {
                a.TransactionId,
                a.RequiresReply,
                a.ReplyStatus,
                a.Status,
                a.DueDate
            })
            .ToListAsync();

        var assignmentMap = assignmentRows
            .GroupBy(a => a.TransactionId)
            .ToDictionary(
                g => g.Key,
                g => (
                    HasPending: g.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                        && a.Status == AssignmentStatus.Active),
                    HasOverdueAssignment: g.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                        && a.Status == AssignmentStatus.Active && a.DueDate.HasValue && a.DueDate.Value < now)));

        var outgoingRows = await db.TransactionOutgoingDepartments.AsNoTracking()
            .Where(x => actionRequiredIds.Contains(x.TransactionId))
            .Select(x => new { x.TransactionId, Name = x.Department.Name })
            .ToListAsync();

        var outgoingByTx = outgoingRows
            .GroupBy(x => x.TransactionId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());

        var lastFollowUpLookup = await db.FollowUps.AsNoTracking()
            .Where(f => actionRequiredIds.Contains(f.TransactionId))
            .GroupBy(f => f.TransactionId)
            .Select(g => new
            {
                TransactionId = g.Key,
                LastDate = g.Max(f => f.CreatedAt > f.FollowUpDate ? f.CreatedAt : f.FollowUpDate)
            })
            .ToDictionaryAsync(x => x.TransactionId, x => (DateTime?)x.LastDate);

        var orderMap = actionRequiredIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);

        var result = rows
            .OrderBy(r => orderMap.GetValueOrDefault(r.Id, int.MaxValue))
            .ToList();

        foreach (var item in result)
        {
            var isResponseOverdue = item.RequiresResponse && !item.ResponseCompleted
                && item.ResponseDueDate.HasValue && item.ResponseDueDate.Value < now;
            var flags = assignmentMap.GetValueOrDefault(item.Id, (HasPending: false, HasOverdueAssignment: false));
            item.HasPendingAssignments = flags.HasPending;
            item.IsResponseOverdue = isResponseOverdue;
            item.IsOverdue = isResponseOverdue || flags.HasOverdueAssignment;

            if (outgoingByTx.TryGetValue(item.Id, out var names))
            {
                item.OutgoingDepartmentNames = names;
                item.OutgoingPartyNames = names;
            }

            var lastFollowUp = lastFollowUpLookup.GetValueOrDefault(item.Id);
            TransactionTimelineHelper.ApplyTo(item, TransactionTimelineHelper.Compute(
                item.IncomingDate,
                item.ResponseDueDate,
                item.ResponseDays,
                item.RequiresResponse,
                item.ResponseCompleted,
                lastFollowUp?.Date,
                now.Date));
        }

        return result;
    }

    private async Task<List<DepartmentOverdueDto>> LoadTopOverdueDepartmentsAsync(DateTime now)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Assignments.AsNoTracking()
            .Where(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                && a.DueDate.HasValue && a.DueDate.Value < now && a.Status == AssignmentStatus.Active)
            .GroupBy(a => new { a.DepartmentId, a.Department.Name })
            .Select(g => new DepartmentOverdueDto
            {
                DepartmentId = g.Key.DepartmentId,
                DepartmentName = g.Key.Name,
                OverdueCount = g.Count()
            })
            .OrderByDescending(d => d.OverdueCount)
            .Take(5)
            .ToListAsync();
    }

    private static IQueryable<Models.Entities.Transaction> WhereActionRequired(
        IQueryable<Models.Entities.Transaction> query, DateTime now) =>
        query.Where(t =>
            t.Status != TransactionStatus.Closed
            && t.Status != TransactionStatus.Cancelled
            && t.Status != TransactionStatus.Archived
            && (
                (t.RequiresResponse && !t.ResponseCompleted)
                || t.Status == TransactionStatus.WaitingForReply
                || t.Status == TransactionStatus.PartiallyReplied
                || t.Status == TransactionStatus.Overdue
                || (t.RequiresResponse && !t.ResponseCompleted
                    && t.ResponseDueDate.HasValue && t.ResponseDueDate.Value < now)
                || t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                    && a.Status == AssignmentStatus.Active && a.DueDate.HasValue && a.DueDate.Value < now)));

    private static IQueryable<Models.Entities.TransactionOutgoingDepartment> ApplyOutgoingDepartmentLinkFilter(
        IQueryable<Models.Entities.TransactionOutgoingDepartment> query, ReportFilterRequest? filter)
    {
        if (filter?.DateFrom.HasValue == true)
            query = query.Where(l => l.Transaction.IncomingDate >= filter.DateFrom);
        if (filter?.DateTo.HasValue == true)
            query = query.Where(l => l.Transaction.IncomingDate <= filter.DateTo);
        if (filter?.OutgoingDepartmentId.HasValue == true)
            query = query.Where(l => l.DepartmentId == filter.OutgoingDepartmentId);
        return query;
    }

    private sealed class OutgoingDeptLinkRow
    {
        public int DepartmentId { get; init; }
        public string DepartmentName { get; init; } = string.Empty;
        public int TransactionId { get; init; }
        public TransactionStatus Status { get; init; }
        public bool RequiresResponse { get; init; }
        public bool ResponseCompleted { get; init; }
        public DateTime? ResponseDueDate { get; init; }
    }

    private sealed class DepartmentClosedRow
    {
        public int DepartmentId { get; init; }
        public string DepartmentName { get; init; } = string.Empty;
        public int ClosedCount { get; init; }
    }

    private async Task<List<OutgoingDeptLinkRow>> LoadOutgoingDepartmentLinkRowsAsync(ReportFilterRequest? filter)
    {
        var query = ApplyOutgoingDepartmentLinkFilter(_db.TransactionOutgoingDepartments.AsNoTracking(), filter);
        return await query
            .Select(l => new OutgoingDeptLinkRow
            {
                DepartmentId = l.DepartmentId,
                DepartmentName = l.Department.Name,
                TransactionId = l.TransactionId,
                Status = l.Transaction.Status,
                RequiresResponse = l.Transaction.RequiresResponse,
                ResponseCompleted = l.Transaction.ResponseCompleted,
                ResponseDueDate = l.Transaction.ResponseDueDate
            })
            .ToListAsync();
    }

    private async Task<List<OutgoingDeptLinkRow>> LoadDepartmentSummaryLinkRowsAsync(DateTime? dateFrom, DateTime? dateTo)
    {
        var query = _db.TransactionOutgoingDepartments.AsNoTracking();
        if (dateFrom.HasValue)
            query = query.Where(l => l.Transaction.IncomingDate >= dateFrom);
        if (dateTo.HasValue)
            query = query.Where(l => l.Transaction.IncomingDate <= dateTo);

        return await query
            .Select(l => new OutgoingDeptLinkRow
            {
                DepartmentId = l.DepartmentId,
                DepartmentName = l.Department.Name,
                TransactionId = l.TransactionId,
                Status = l.Transaction.Status,
                RequiresResponse = l.Transaction.RequiresResponse,
                ResponseCompleted = l.Transaction.ResponseCompleted,
                ResponseDueDate = l.Transaction.ResponseDueDate
            })
            .ToListAsync();
    }

    private async Task<HashSet<int>> LoadAssignmentOverdueTransactionIdsAsync(DateTime now) =>
        (await _db.Assignments.AsNoTracking()
            .Where(a => a.RequiresReply
                && a.ReplyStatus != ReplyStatus.Replied
                && a.Status == AssignmentStatus.Active
                && a.DueDate.HasValue
                && a.DueDate.Value < now)
            .Select(a => a.TransactionId)
            .Distinct()
            .ToListAsync())
        .ToHashSet();

    private async Task<List<DepartmentClosedRow>> LoadClosedCountByDepartmentAsync(DateTime? dateFrom, DateTime? dateTo)
    {
        var query = _db.TransactionOutgoingDepartments.AsNoTracking()
            .Where(l => l.Transaction.Status == TransactionStatus.Closed && l.Transaction.ClosedAt.HasValue);

        if (dateFrom.HasValue)
            query = query.Where(l => l.Transaction.ClosedAt >= dateFrom);
        if (dateTo.HasValue)
            query = query.Where(l => l.Transaction.ClosedAt <= dateTo);

        var rows = await query
            .Select(l => new { l.DepartmentId, DepartmentName = l.Department.Name, l.TransactionId })
            .ToListAsync();

        return rows
            .GroupBy(x => new { x.DepartmentId, x.DepartmentName })
            .Select(g => new DepartmentClosedRow
            {
                DepartmentId = g.Key.DepartmentId,
                DepartmentName = g.Key.DepartmentName,
                ClosedCount = g.Select(x => x.TransactionId).Distinct().Count()
            })
            .ToList();
    }

    private static bool IsOpenTransactionStatus(TransactionStatus status) =>
        status != TransactionStatus.Closed
        && status != TransactionStatus.Cancelled
        && status != TransactionStatus.Archived;

    private static bool IsWaitingForReplyStatus(TransactionStatus status) =>
        status == TransactionStatus.WaitingForReply
        || status == TransactionStatus.PartiallyReplied
        || status == TransactionStatus.Assigned;

    private static bool IsResponseOverdueRow(OutgoingDeptLinkRow row, DateTime now) =>
        row.RequiresResponse
        && !row.ResponseCompleted
        && row.ResponseDueDate.HasValue
        && row.ResponseDueDate.Value < now;

    private static IQueryable<Models.Entities.Transaction> ApplyReportFilter(
        IQueryable<Models.Entities.Transaction> query, ReportFilterRequest? filter)
    {
        if (filter == null) return query;
        if (filter.DateFrom.HasValue) query = query.Where(t => t.IncomingDate >= filter.DateFrom);
        if (filter.DateTo.HasValue) query = query.Where(t => t.IncomingDate <= filter.DateTo);
        if (!string.IsNullOrWhiteSpace(filter.Status) && Enum.TryParse<TransactionStatus>(filter.Status, out var status))
            query = query.Where(t => t.Status == status);
        if (filter.CategoryId.HasValue) query = query.Where(t => t.CategoryId == filter.CategoryId);
        if (filter.DepartmentId.HasValue) query = query.Where(t => t.Assignments.Any(a => a.DepartmentId == filter.DepartmentId));
        if (filter.IncomingPartyId.HasValue) query = query.Where(t => t.IncomingFromPartyId == filter.IncomingPartyId);
        if (filter.OutgoingPartyId.HasValue) query = query.Where(t => t.OutgoingParties.Any(o => o.ExternalPartyId == filter.OutgoingPartyId));
        if (filter.OutgoingDepartmentId.HasValue) query = query.Where(t => t.OutgoingDepartments.Any(o => o.DepartmentId == filter.OutgoingDepartmentId));
        if (!string.IsNullOrWhiteSpace(filter.IncomingSourceType)
            && Enum.TryParse<IncomingSourceType>(filter.IncomingSourceType, true, out var incomingSourceType))
            query = query.Where(t => t.IncomingSourceType == incomingSourceType);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(t =>
                t.IncomingNumber.Contains(term) ||
                t.InternalTrackingNumber.Contains(term) ||
                t.Subject.Contains(term));
        }
        return query;
    }

    private static string? FormatHijriDate(DateTime date)
    {
        try
        {
            var hijri = new UmAlQuraCalendar();
            return $"{hijri.GetDayOfMonth(date):00}/{hijri.GetMonth(date):00}/{hijri.GetYear(date)} هـ";
        }
        catch
        {
            return null;
        }
    }
}
