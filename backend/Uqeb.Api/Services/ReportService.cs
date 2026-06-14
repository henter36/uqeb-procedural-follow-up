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
    Task<byte[]> ExportToExcelAsync(string reportType, ReportFilterRequest? filter = null);
    Task<PagedResult<ReportTransactionRowDto>> GetResponseRequiredDetailsAsync(ReportPagedFilterRequest filter);
    Task<PagedResult<ReportTransactionRowDto>> GetOverdueResponsesDetailsAsync(ReportPagedFilterRequest filter);
    Task<PagedResult<ReportTransactionRowDto>> GetOpenAssignmentsDetailsAsync(ReportPagedFilterRequest filter);
    Task<PagedResult<ReportTransactionRowDto>> GetPartialRepliesDetailsAsync(ReportPagedFilterRequest filter);
    Task<PagedResult<ReportTransactionRowDto>> GetOverdueDetailsAsync(ReportPagedFilterRequest filter);
    Task<PagedResult<ReportTransactionRowDto>> GetWaitingReplyDetailsAsync(ReportPagedFilterRequest filter);
    Task<PagedResult<ReportTransactionRowDto>> GetOpenDetailsAsync(ReportPagedFilterRequest filter);
    Task<byte[]> ExportReportDetailsExcelAsync(string reportType, ReportPagedFilterRequest filter, bool currentPageOnly);
    Task<ReportSectionCountsDto> GetPageSummaryAsync(ReportFilterRequest? filter = null);
}

public class ReportService : IReportService
{
    private readonly AppDbContext _db;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ReportService(AppDbContext db, IDbContextFactory<AppDbContext> dbFactory)
    {
        _db = db;
        _dbFactory = dbFactory;
    }

    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync()
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        async Task<int> CountOpenAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Transactions.AsNoTracking()
                .Where(t => t.Status != TransactionStatus.Closed
                    && t.Status != TransactionStatus.Cancelled
                    && t.Status != TransactionStatus.Archived)
                .CountAsync();
        }

        async Task<int> CountRequiresResponsePendingAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Transactions.AsNoTracking()
                .Where(t => t.RequiresResponse && !t.ResponseCompleted)
                .CountAsync();
        }

        async Task<int> CountResponseOverdueAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Transactions.AsNoTracking()
                .Where(t => t.RequiresResponse && !t.ResponseCompleted
                    && t.ResponseDueDate.HasValue && t.ResponseDueDate.Value < now)
                .CountAsync();
        }

        async Task<int> CountByStatusAsync(TransactionStatus status)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Transactions.AsNoTracking()
                .CountAsync(t => t.Status == status);
        }

        async Task<int> CountClosedThisMonthAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Transactions.AsNoTracking()
                .Where(t => t.Status == TransactionStatus.Closed
                    && t.ClosedAt.HasValue && t.ClosedAt.Value >= monthStart)
                .CountAsync();
        }

        async Task<(int Count, double AvgDays)> GetClosedAverageAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var closed = db.Transactions.AsNoTracking()
                .Where(t => t.Status == TransactionStatus.Closed && t.ClosedAt.HasValue);
            var count = await closed.CountAsync();
            if (count == 0) return (0, 0);
            var avg = await closed.AverageAsync(t =>
                (double)EF.Functions.DateDiffDay(t.CreatedAt, t.ClosedAt!.Value));
            return (count, avg);
        }

        var totalOpenTask = CountOpenAsync();
        var requiresResponsePendingTask = CountRequiresResponsePendingAsync();
        var responseOverdueCountTask = CountResponseOverdueAsync();
        var waitingForReplyTask = CountByStatusAsync(TransactionStatus.WaitingForReply);
        var partiallyRepliedTask = CountByStatusAsync(TransactionStatus.PartiallyReplied);
        var readyForResponseTask = CountByStatusAsync(TransactionStatus.ReadyForResponse);
        var closedThisMonthTask = CountClosedThisMonthAsync();
        var closedAverageTask = GetClosedAverageAsync();

        await Task.WhenAll(
            totalOpenTask,
            requiresResponsePendingTask,
            responseOverdueCountTask,
            waitingForReplyTask,
            partiallyRepliedTask,
            readyForResponseTask,
            closedThisMonthTask,
            closedAverageTask);

        var closedAverage = await closedAverageTask;

        return new DashboardSummaryDto
        {
            TotalOpen = await totalOpenTask,
            RequiresResponsePending = await requiresResponsePendingTask,
            ResponseOverdueCount = await responseOverdueCountTask,
            WaitingForReply = await waitingForReplyTask,
            PartiallyReplied = await partiallyRepliedTask,
            ReadyForResponse = await readyForResponseTask,
            ClosedThisMonth = await closedThisMonthTask,
            AverageCompletionDays = closedAverage.Count == 0 ? 0 : Math.Round(closedAverage.AvgDays, 1)
        };
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
        var summary = await GetDashboardSummaryAsync();
        var now = DateTime.UtcNow;
        var tx = _db.Transactions.AsNoTracking();
        var topOverdueTask = GetDashboardTopOverdueDepartmentsAsync();
        var topIncomingTask = GetDashboardTopIncomingPartiesAsync();
        var byCategoryTask = GetDashboardCategoryDistributionAsync();
        var byStatusTask = GetDashboardStatusDistributionAsync();
        var actionRequiredTask = GetDashboardActionRequiredAsync();

        await Task.WhenAll(topOverdueTask, topIncomingTask, byCategoryTask, byStatusTask, actionRequiredTask);

        return new DashboardDto
        {
            TotalOpen = summary.TotalOpen,
            NewCount = await tx.CountAsync(t => t.Status == TransactionStatus.New),
            WaitingForReply = summary.WaitingForReply + summary.PartiallyReplied,
            OverdueCount = await tx.CountAsync(t =>
                (t.RequiresResponse && !t.ResponseCompleted && t.ResponseDueDate.HasValue && t.ResponseDueDate.Value < now)
                || t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                    && a.Status == AssignmentStatus.Active && a.DueDate.HasValue && a.DueDate.Value < now)),
            RequiresResponse = summary.RequiresResponsePending,
            ResponseCompleted = await tx.CountAsync(t => t.ResponseCompleted),
            ClosedCount = await tx.CountAsync(t => t.Status == TransactionStatus.Closed),
            RequiresResponsePending = summary.RequiresResponsePending,
            ResponseOverdueCount = summary.ResponseOverdueCount,
            PartiallyReplied = summary.PartiallyReplied,
            ReadyForResponse = summary.ReadyForResponse,
            ClosedThisMonth = summary.ClosedThisMonth,
            AverageCompletionDays = summary.AverageCompletionDays,
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

    public async Task<byte[]> ExportReportDetailsExcelAsync(string reportType, ReportPagedFilterRequest filter, bool currentPageOnly)
    {
        List<ReportTransactionRowDto> data;
        if (currentPageOnly)
        {
            var paged = await GetDetailsByReportTypeAsync(reportType, filter);
            data = paged.Items;
        }
        else
        {
            var allFilter = new ReportPagedFilterRequest
            {
                DateFrom = filter.DateFrom,
                DateTo = filter.DateTo,
                Status = filter.Status,
                CategoryId = filter.CategoryId,
                DepartmentId = filter.DepartmentId,
                IncomingPartyId = filter.IncomingPartyId,
                OutgoingPartyId = filter.OutgoingPartyId,
                OutgoingDepartmentId = filter.OutgoingDepartmentId,
                IncomingSourceType = filter.IncomingSourceType,
                Search = filter.Search,
                Page = 1,
                PageSize = ReportPageSize.Max
            };
            var batch = new List<ReportTransactionRowDto>();
            PagedResult<ReportTransactionRowDto> page;
            do
            {
                page = await GetDetailsByReportTypeAsync(reportType, allFilter);
                batch.AddRange(page.Items);
                allFilter.Page++;
            } while (page.HasNextPage);
            data = batch;
        }

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("التقرير");
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

        for (var i = 0; i < data.Count; i++)
        {
            var row = i + 2;
            var t = data[i];
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
        }

        ws.RightToLeft = true;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

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
        var query = _db.Assignments.Include(a => a.Department).AsQueryable();
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
        var responseRequiredTask = CountReportAsync(filter, q =>
            q.Where(t => t.RequiresResponse && !t.ResponseCompleted));

        var overdueResponsesTask = CountReportAsync(filter, q => q.Where(t =>
            t.RequiresResponse && !t.ResponseCompleted
            && t.ResponseDueDate.HasValue && t.ResponseDueDate.Value < now));

        var openAssignmentsTask = CountReportAsync(filter, q => q.Where(t =>
            t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                && a.Status == AssignmentStatus.Active)));

        var partialRepliesTask = CountReportAsync(filter, ApplyPartialRepliesFilter);

        var overdueTask = CountReportAsync(filter, q => q.Where(t =>
            (t.RequiresResponse && !t.ResponseCompleted
                && t.ResponseDueDate.HasValue && t.ResponseDueDate.Value < now)
            || t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                && a.Status == AssignmentStatus.Active && a.DueDate.HasValue && a.DueDate.Value < now)));

        var waitingReplyTask = CountReportAsync(filter, q => q.Where(t =>
            t.Status == TransactionStatus.WaitingForReply || t.Status == TransactionStatus.PartiallyReplied));

        var openTask = CountReportAsync(filter, q => q.Where(t =>
            t.Status != TransactionStatus.Closed && t.Status != TransactionStatus.Cancelled && t.Status != TransactionStatus.Archived));

        await Task.WhenAll(
            responseRequiredTask,
            overdueResponsesTask,
            openAssignmentsTask,
            partialRepliesTask,
            overdueTask,
            waitingReplyTask,
            openTask);

        return new ReportSectionCountsDto
        {
            ResponseRequired = await responseRequiredTask,
            OverdueResponses = await overdueResponsesTask,
            OpenAssignments = await openAssignmentsTask,
            PartialReplies = await partialRepliesTask,
            Overdue = await overdueTask,
            WaitingReply = await waitingReplyTask,
            Open = await openTask
        };
    }

    private static IQueryable<Models.Entities.Transaction> ApplyPartialRepliesFilter(
        IQueryable<Models.Entities.Transaction> query) =>
        query.Where(t =>
            t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus == ReplyStatus.Replied)
            && t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                && a.Status == AssignmentStatus.Active));

    private async Task<int> CountReportAsync(
        ReportFilterRequest? filter,
        Func<IQueryable<Models.Entities.Transaction>, IQueryable<Models.Entities.Transaction>> applyPredicate)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = ApplyReportFilter(db.Transactions.AsNoTracking(), filter);
        return await applyPredicate(query).CountAsync();
    }

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
        var links = ApplyOutgoingDepartmentLinkFilter(_db.TransactionOutgoingDepartments.AsNoTracking(), filter);

        var candidateIds = await links.Select(l => l.TransactionId).Distinct().ToListAsync();
        var overdueTxIds = candidateIds.Count == 0
            ? new HashSet<int>()
            : await GetOverdueTransactionIdsAmongAsync(now, candidateIds);

        var summary = await links
            .GroupBy(l => new { l.DepartmentId, l.Department.Name })
            .Select(g => new OutgoingDepartmentReportDto
            {
                DepartmentId = g.Key.DepartmentId,
                DepartmentName = g.Key.Name,
                TransactionCount = g.Select(l => l.TransactionId).Distinct().Count(),
                OpenCount = g.Where(l =>
                        l.Transaction.Status != TransactionStatus.Closed
                        && l.Transaction.Status != TransactionStatus.Cancelled
                        && l.Transaction.Status != TransactionStatus.Archived)
                    .Select(l => l.TransactionId).Distinct().Count(),
                ClosedCount = g.Where(l => l.Transaction.Status == TransactionStatus.Closed)
                    .Select(l => l.TransactionId).Distinct().Count(),
                OverdueCount = 0
            })
            .OrderByDescending(x => x.TransactionCount)
            .ToListAsync();

        if (summary.Count == 0 || overdueTxIds.Count == 0)
            return summary;

        var overdueByDept = await links
            .Where(l => overdueTxIds.Contains(l.TransactionId))
            .GroupBy(l => l.DepartmentId)
            .Select(g => new { DepartmentId = g.Key, OverdueCount = g.Select(l => l.TransactionId).Distinct().Count() })
            .ToListAsync();

        var overdueMap = overdueByDept.ToDictionary(x => x.DepartmentId, x => x.OverdueCount);
        foreach (var row in summary)
            row.OverdueCount = overdueMap.GetValueOrDefault(row.DepartmentId, 0);

        return summary;
    }

    public async Task<List<DepartmentSummaryDto>> GetDepartmentSummaryAsync(ReportFilterRequest? filter = null)
    {
        var now = DateTime.UtcNow;
        var dateFrom = filter?.DateFrom;
        var dateTo = filter?.DateTo;

        IQueryable<Models.Entities.TransactionOutgoingDepartment> filteredLinks =
            _db.TransactionOutgoingDepartments.AsNoTracking();

        if (dateFrom.HasValue)
            filteredLinks = filteredLinks.Where(l => l.Transaction.IncomingDate >= dateFrom);
        if (dateTo.HasValue)
            filteredLinks = filteredLinks.Where(l => l.Transaction.IncomingDate <= dateTo);

        var results = await filteredLinks
            .GroupBy(l => new { l.DepartmentId, DepartmentName = l.Department.Name })
            .Select(g => new DepartmentSummaryDto
            {
                DepartmentId = g.Key.DepartmentId,
                DepartmentName = g.Key.DepartmentName,
                TotalIncoming = g.Select(l => l.TransactionId).Distinct().Count(),
                OpenCount = g.Where(l =>
                        l.Transaction.Status != TransactionStatus.Closed
                        && l.Transaction.Status != TransactionStatus.Cancelled
                        && l.Transaction.Status != TransactionStatus.Archived)
                    .Select(l => l.TransactionId).Distinct().Count(),
                WaitingForReplyCount = g.Where(l =>
                        l.Transaction.Status == TransactionStatus.WaitingForReply
                        || l.Transaction.Status == TransactionStatus.PartiallyReplied
                        || l.Transaction.Status == TransactionStatus.Assigned)
                    .Select(l => l.TransactionId).Distinct().Count(),
                OverdueCount = 0,
                ClosedCount = 0
            })
            .ToListAsync();

        var closedByDept = await _db.TransactionOutgoingDepartments.AsNoTracking()
            .Where(l => l.Transaction.Status == TransactionStatus.Closed
                && l.Transaction.ClosedAt.HasValue
                && (!dateFrom.HasValue || l.Transaction.ClosedAt >= dateFrom)
                && (!dateTo.HasValue || l.Transaction.ClosedAt <= dateTo))
            .GroupBy(l => new { l.DepartmentId, DepartmentName = l.Department.Name })
            .Select(g => new
            {
                g.Key.DepartmentId,
                g.Key.DepartmentName,
                ClosedCount = g.Select(l => l.TransactionId).Distinct().Count()
            })
            .ToListAsync();

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

        if (results.Count > 0)
        {
            var candidateIds = await filteredLinks
                .Select(l => l.TransactionId)
                .Distinct()
                .ToListAsync();
            var overdueTxIds = await GetOverdueTransactionIdsAmongAsync(now, candidateIds);
            if (overdueTxIds.Count > 0)
            {
                var overdueByDept = await filteredLinks
                    .Where(l => overdueTxIds.Contains(l.TransactionId))
                    .GroupBy(l => l.DepartmentId)
                    .Select(g => new { DepartmentId = g.Key, OverdueCount = g.Select(l => l.TransactionId).Distinct().Count() })
                    .ToListAsync();
                var overdueMap = overdueByDept.ToDictionary(x => x.DepartmentId, x => x.OverdueCount);
                foreach (var row in results)
                    row.OverdueCount = overdueMap.GetValueOrDefault(row.DepartmentId, 0);
            }
        }

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
            IsOverdue = r.IsOverdue,
            IsResponseOverdue = r.IsOverdue && r.ResponseDueDate.HasValue,
            CreatedAt = r.CreatedAt
        }).ToList();

    public Task<byte[]> ExportToExcelAsync(string reportType, ReportFilterRequest? filter = null) =>
        ExportReportDetailsExcelAsync(reportType, ToPagedFilter(filter), currentPageOnly: true);

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
            .Select(t => new
            {
                t.Id,
                t.InternalTrackingNumber,
                t.IncomingNumber,
                t.IncomingDate,
                t.Subject,
                t.IncomingFrom,
                t.IncomingSourceType,
                IncomingFromPartyName = t.IncomingFromParty != null ? t.IncomingFromParty.Name : null,
                IncomingFromDepartmentName = t.IncomingFromDepartment != null ? t.IncomingFromDepartment.Name : null,
                CategoryName = t.CategoryEntity != null ? t.CategoryEntity.Name : t.Category,
                t.Priority,
                t.Status,
                t.ResponseType,
                t.ResponseDueDate,
                t.RequiresResponse,
                t.ResponseCompleted,
                t.CreatedAt,
                OutgoingDepartmentsDisplayNames = t.OutgoingDepartments.Select(o => o.Department.Name).ToList(),
                AssignmentDueDate = t.Assignments
                    .Where(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                        && a.Status == AssignmentStatus.Active && a.DueDate.HasValue)
                    .Min(a => (DateTime?)a.DueDate)
            })
            .ToListAsync();

        var items = rows.Select(t =>
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
                DaysOverdue = daysOverdue,
                CreatedAt = t.CreatedAt,
                IsOverdue = isOverdue
            };
        }).ToList();

        return PagedResult<ReportTransactionRowDto>.Create(items, totalCount, page, pageSize);
    }

    private async Task<List<TransactionListDto>> LoadActionRequiredAsync(DateTime now)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tx = db.Transactions.AsNoTracking();

        var primaryIds = await tx
            .Where(t =>
                t.Status != TransactionStatus.Closed
                && t.Status != TransactionStatus.Cancelled
                && t.Status != TransactionStatus.Archived
                && (
                    (t.RequiresResponse && !t.ResponseCompleted)
                    || t.Status == TransactionStatus.WaitingForReply
                    || t.Status == TransactionStatus.PartiallyReplied
                    || t.Status == TransactionStatus.ReadyForResponse))
            .OrderBy(t => t.ResponseDueDate ?? t.IncomingDate)
            .ThenByDescending(t => t.Priority)
            .Take(10)
            .Select(t => t.Id)
            .ToListAsync();

        var overdueAssignmentIds = await db.Assignments.AsNoTracking()
            .Where(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                && a.Status == AssignmentStatus.Active
                && a.DueDate.HasValue && a.DueDate.Value < now)
            .Select(a => a.TransactionId)
            .Distinct()
            .Take(50)
            .ToListAsync();

        var actionRequiredIds = primaryIds
            .Concat(overdueAssignmentIds.Where(id => !primaryIds.Contains(id)))
            .Take(10)
            .ToList();

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

            if (item.ResponseDueDate.HasValue && !item.ResponseCompleted)
                item.DaysRemainingForResponse = (int)(item.ResponseDueDate.Value.Date - now.Date).TotalDays;
        }

        return result;
    }

    private async Task<List<DepartmentOverdueDto>> LoadTopOverdueDepartmentsAsync(DateTime now)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var overdueDeptsRaw = await db.Assignments.AsNoTracking()
            .Where(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                && a.DueDate.HasValue && a.DueDate.Value < now && a.Status == AssignmentStatus.Active)
            .GroupBy(a => a.DepartmentId)
            .Select(g => new { DepartmentId = g.Key, OverdueCount = g.Count() })
            .OrderByDescending(d => d.OverdueCount)
            .Take(5)
            .ToListAsync();

        if (overdueDeptsRaw.Count == 0)
            return new List<DepartmentOverdueDto>();

        var overdueDeptIds = overdueDeptsRaw.Select(d => d.DepartmentId).ToList();
        var overdueDeptNames = await db.Departments.AsNoTracking()
            .Where(d => overdueDeptIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name);

        return overdueDeptsRaw.Select(d => new DepartmentOverdueDto
        {
            DepartmentId = d.DepartmentId,
            DepartmentName = overdueDeptNames.GetValueOrDefault(d.DepartmentId, ""),
            OverdueCount = d.OverdueCount
        }).ToList();
    }

    private async Task<HashSet<int>> GetOverdueTransactionIdsAmongAsync(DateTime now, ICollection<int> candidateIds)
    {
        if (candidateIds.Count == 0)
            return new HashSet<int>();

        async Task<List<int>> LoadResponseOverdueAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Transactions.AsNoTracking()
                .Where(t => candidateIds.Contains(t.Id)
                    && t.RequiresResponse && !t.ResponseCompleted
                    && t.ResponseDueDate.HasValue && t.ResponseDueDate.Value < now)
                .Select(t => t.Id)
                .ToListAsync();
        }

        async Task<List<int>> LoadAssignmentOverdueAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Assignments.AsNoTracking()
                .Where(a => candidateIds.Contains(a.TransactionId)
                    && a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                    && a.Status == AssignmentStatus.Active
                    && a.DueDate.HasValue && a.DueDate.Value < now)
                .Select(a => a.TransactionId)
                .Distinct()
                .ToListAsync();
        }

        var responseTask = LoadResponseOverdueAsync();
        var assignmentTask = LoadAssignmentOverdueAsync();
        await Task.WhenAll(responseTask, assignmentTask);
        return (await responseTask).Concat(await assignmentTask).ToHashSet();
    }

    private async Task<HashSet<int>> GetOverdueTransactionIdsAsync(DateTime now)
    {
        var responseOverdueIds = await _db.Transactions.AsNoTracking()
            .Where(t => t.RequiresResponse && !t.ResponseCompleted
                && t.ResponseDueDate.HasValue && t.ResponseDueDate.Value < now)
            .Select(t => t.Id)
            .ToListAsync();

        var assignmentOverdueIds = await _db.Assignments.AsNoTracking()
            .Where(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                && a.Status == AssignmentStatus.Active
                && a.DueDate.HasValue && a.DueDate.Value < now)
            .Select(a => a.TransactionId)
            .Distinct()
            .ToListAsync();

        return responseOverdueIds.Concat(assignmentOverdueIds).ToHashSet();
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
