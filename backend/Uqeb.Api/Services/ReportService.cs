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
        var transactions = await LoadTransactionsAsync();

        var closedThisMonth = transactions.Count(t =>
            t.Status == TransactionStatus.Closed && t.ClosedAt >= monthStart);

        var completed = transactions.Where(t => t.Status == TransactionStatus.Closed && t.ClosedAt.HasValue).ToList();
        var avgDays = completed.Count > 0
            ? completed.Average(t => (t.ClosedAt!.Value - t.CreatedAt).TotalDays)
            : 0;

        var overdueDepts = await _db.Assignments
            .Include(a => a.Department)
            .Where(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied && a.DueDate < now && a.Status == AssignmentStatus.Active)
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

        var topIncoming = transactions
            .Where(t => t.IncomingFrom != null)
            .GroupBy(t => t.IncomingFrom!)
            .Select(g => new ExternalPartyReportDto { PartyName = g.Key, TransactionCount = g.Count() })
            .OrderByDescending(x => x.TransactionCount)
            .Take(5)
            .ToList();

        var byCategory = transactions
            .GroupBy(t => new { t.CategoryId, Name = t.CategoryEntity != null ? t.CategoryEntity.Name : (t.Category ?? "بدون تصنيف") })
            .Select(g => new CategoryDistributionDto
            {
                CategoryId = g.Key.CategoryId,
                CategoryName = g.Key.Name,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        var byStatus = transactions
            .GroupBy(t => t.Status)
            .Select(g => new StatusDistributionDto { Status = g.Key.ToString(), Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        var actionRequired = transactions
            .Where(t => t.Status != TransactionStatus.Closed && t.Status != TransactionStatus.Cancelled && t.Status != TransactionStatus.Archived)
            .Where(t =>
                (t.RequiresResponse && !t.ResponseCompleted) ||
                t.Status == TransactionStatus.WaitingForReply ||
                t.Status == TransactionStatus.PartiallyReplied ||
                t.Status == TransactionStatus.Overdue ||
                WorkflowHelper.IsTransactionOverdue(t, now))
            .OrderByDescending(t => WorkflowHelper.IsTransactionOverdue(t, now))
            .ThenBy(t => t.IncomingDate)
            .Take(10)
            .Select(t => MapList(t, now))
            .ToList();

        return new DashboardSummaryDto
        {
            TotalOpen = transactions.Count(t => t.Status != TransactionStatus.Closed && t.Status != TransactionStatus.Cancelled && t.Status != TransactionStatus.Archived),
            RequiresResponsePending = transactions.Count(t => t.RequiresResponse && !t.ResponseCompleted),
            ResponseOverdueCount = transactions.Count(t => WorkflowHelper.IsResponseOverdue(t, now)),
            WaitingForReply = transactions.Count(t => t.Status == TransactionStatus.WaitingForReply),
            PartiallyReplied = transactions.Count(t => t.Status == TransactionStatus.PartiallyReplied),
            ReadyForResponse = transactions.Count(t => t.Status == TransactionStatus.ReadyForResponse),
            ClosedThisMonth = closedThisMonth,
            AverageCompletionDays = Math.Round(avgDays, 1),
            TopOverdueDepartments = overdueDepts,
            TopIncomingParties = topIncoming,
            ByCategory = byCategory,
            ByStatus = byStatus,
            ActionRequired = actionRequired
        };
    }

    public async Task<DashboardDto> GetDashboardAsync()
    {
        var summary = await GetDashboardSummaryAsync();
        var now = DateTime.UtcNow;
        var transactions = await LoadTransactionsAsync();

        return new DashboardDto
        {
            TotalOpen = summary.TotalOpen,
            NewCount = transactions.Count(t => t.Status == TransactionStatus.New),
            WaitingForReply = summary.WaitingForReply + summary.PartiallyReplied,
            OverdueCount = transactions.Count(t => WorkflowHelper.IsTransactionOverdue(t, now)),
            RequiresResponse = summary.RequiresResponsePending,
            ResponseCompleted = transactions.Count(t => t.ResponseCompleted),
            ClosedCount = transactions.Count(t => t.Status == TransactionStatus.Closed),
            RequiresResponsePending = summary.RequiresResponsePending,
            ResponseOverdueCount = summary.ResponseOverdueCount,
            PartiallyReplied = summary.PartiallyReplied,
            ReadyForResponse = summary.ReadyForResponse,
            ClosedThisMonth = summary.ClosedThisMonth,
            AverageCompletionDays = summary.AverageCompletionDays,
            TopOverdueDepartments = summary.TopOverdueDepartments,
            TopIncomingParties = summary.TopIncomingParties,
            ByCategory = summary.ByCategory,
            ByStatus = summary.ByStatus,
            ActionRequired = summary.ActionRequired
        };
    }

    public Task<List<TransactionListDto>> GetOverdueAsync(ReportFilterRequest? filter = null) =>
        GetFilteredAsync(t => WorkflowHelper.IsTransactionOverdue(t, DateTime.UtcNow), filter);

    public Task<List<TransactionListDto>> GetOpenAsync(ReportFilterRequest? filter = null) =>
        GetFilteredAsync(t => t.Status != TransactionStatus.Closed && t.Status != TransactionStatus.Cancelled && t.Status != TransactionStatus.Archived, filter);

    public Task<List<TransactionListDto>> GetWaitingRepliesAsync(ReportFilterRequest? filter = null) =>
        GetFilteredAsync(t => t.Status == TransactionStatus.WaitingForReply || t.Status == TransactionStatus.PartiallyReplied, filter);

    public Task<List<TransactionListDto>> GetPendingResponseAsync(ReportFilterRequest? filter = null) =>
        GetFilteredAsync(t => t.RequiresResponse && !t.ResponseCompleted, filter);

    public Task<List<TransactionListDto>> GetResponseRequiredAsync(ReportFilterRequest? filter = null) =>
        GetPendingResponseAsync(filter);

    public Task<List<TransactionListDto>> GetOverdueResponsesAsync(ReportFilterRequest? filter = null) =>
        GetFilteredAsync(t => WorkflowHelper.IsResponseOverdue(t, DateTime.UtcNow), filter);

    public Task<List<TransactionListDto>> GetPendingAssignmentsAsync(ReportFilterRequest? filter = null) =>
        GetFilteredAsync(t => t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied && a.Status == AssignmentStatus.Active), filter);

    public Task<List<TransactionListDto>> GetPartialRepliesAsync(ReportFilterRequest? filter = null) =>
        GetFilteredAsync(t =>
            t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus == ReplyStatus.Replied)
            && t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                && a.Status == AssignmentStatus.Active), filter);

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
        var responseRequired = await CountReportAsync(filter, q =>
            q.Where(t => t.RequiresResponse && !t.ResponseCompleted));

        var overdueResponses = await CountReportAsync(filter, q => q.Where(t =>
            t.RequiresResponse && !t.ResponseCompleted
            && t.ResponseDueDate.HasValue && t.ResponseDueDate.Value < now));

        var openAssignments = await CountReportAsync(filter, q => q.Where(t =>
            t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                && a.Status == AssignmentStatus.Active)));

        var partialReplies = await CountReportAsync(filter, ApplyPartialRepliesFilter);

        var overdue = await CountReportAsync(filter, q => q.Where(t =>
            (t.RequiresResponse && !t.ResponseCompleted
                && t.ResponseDueDate.HasValue && t.ResponseDueDate.Value < now)
            || t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied
                && a.Status == AssignmentStatus.Active && a.DueDate.HasValue && a.DueDate.Value < now)));

        var waitingReply = await CountReportAsync(filter, q => q.Where(t =>
            t.Status == TransactionStatus.WaitingForReply || t.Status == TransactionStatus.PartiallyReplied));

        var open = await CountReportAsync(filter, q => q.Where(t =>
            t.Status != TransactionStatus.Closed && t.Status != TransactionStatus.Cancelled && t.Status != TransactionStatus.Archived));

        return new ReportSectionCountsDto
        {
            ResponseRequired = responseRequired,
            OverdueResponses = overdueResponses,
            OpenAssignments = openAssignments,
            PartialReplies = partialReplies,
            Overdue = overdue,
            WaitingReply = waitingReply,
            Open = open
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
        var links = await _db.TransactionOutgoingDepartments
            .Include(x => x.Department)
            .Include(x => x.Transaction)
            .ToListAsync();

        if (filter?.DateFrom.HasValue == true)
            links = links.Where(x => x.Transaction.IncomingDate >= filter.DateFrom).ToList();
        if (filter?.DateTo.HasValue == true)
            links = links.Where(x => x.Transaction.IncomingDate <= filter.DateTo).ToList();
        if (filter?.OutgoingDepartmentId.HasValue == true)
            links = links.Where(x => x.DepartmentId == filter.OutgoingDepartmentId).ToList();

        return links
            .GroupBy(x => new { x.DepartmentId, x.Department.Name })
            .Select(g =>
            {
                var txs = g.Select(x => x.Transaction).DistinctBy(t => t.Id).ToList();
                return new OutgoingDepartmentReportDto
                {
                    DepartmentId = g.Key.DepartmentId,
                    DepartmentName = g.Key.Name,
                    TransactionCount = txs.Count,
                    OpenCount = txs.Count(t => t.Status != TransactionStatus.Closed && t.Status != TransactionStatus.Cancelled && t.Status != TransactionStatus.Archived),
                    ClosedCount = txs.Count(t => t.Status == TransactionStatus.Closed),
                    OverdueCount = txs.Count(t => WorkflowHelper.IsTransactionOverdue(t, now))
                };
            })
            .OrderByDescending(x => x.TransactionCount)
            .ToList();
    }

    public async Task<List<DepartmentSummaryDto>> GetDepartmentSummaryAsync(ReportFilterRequest? filter = null)
    {
        var now = DateTime.UtcNow;
        var links = await _db.TransactionOutgoingDepartments
            .Include(x => x.Department)
            .Include(x => x.Transaction).ThenInclude(t => t.Assignments)
            .ToListAsync();

        var dateFrom = filter?.DateFrom;
        var dateTo = filter?.DateTo;

        return links
            .GroupBy(x => new { x.DepartmentId, x.Department.Name })
            .Select(g =>
            {
                var allTxs = g.Select(x => x.Transaction).DistinctBy(t => t.Id).ToList();
                var incomingTxs = allTxs.AsEnumerable();
                if (dateFrom.HasValue) incomingTxs = incomingTxs.Where(t => t.IncomingDate >= dateFrom);
                if (dateTo.HasValue) incomingTxs = incomingTxs.Where(t => t.IncomingDate <= dateTo);
                var incomingList = incomingTxs.ToList();

                var closedInPeriod = allTxs.Where(t =>
                    t.Status == TransactionStatus.Closed &&
                    t.ClosedAt.HasValue &&
                    (!dateFrom.HasValue || t.ClosedAt >= dateFrom) &&
                    (!dateTo.HasValue || t.ClosedAt <= dateTo)).ToList();

                var total = incomingList.Count;
                var closed = closedInPeriod.Count;
                return new DepartmentSummaryDto
                {
                    DepartmentId = g.Key.DepartmentId,
                    DepartmentName = g.Key.Name,
                    TotalIncoming = total,
                    OpenCount = incomingList.Count(t => t.Status != TransactionStatus.Closed && t.Status != TransactionStatus.Cancelled && t.Status != TransactionStatus.Archived),
                    WaitingForReplyCount = incomingList.Count(t =>
                        t.Status == TransactionStatus.WaitingForReply ||
                        t.Status == TransactionStatus.PartiallyReplied ||
                        t.Status == TransactionStatus.Assigned),
                    OverdueCount = incomingList.Count(t => WorkflowHelper.IsTransactionOverdue(t, now)),
                    ClosedCount = closed,
                    CloseRate = total > 0 ? Math.Round((double)closed / total * 100, 1) : 0
                };
            })
            .Where(x => x.TotalIncoming > 0 || x.ClosedCount > 0)
            .OrderByDescending(x => x.TotalIncoming)
            .ToList();
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

    public async Task<byte[]> ExportToExcelAsync(string reportType, ReportFilterRequest? filter = null)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("التقرير");

        List<TransactionListDto> data = reportType.ToLower() switch
        {
            "overdue" => await GetOverdueAsync(filter),
            "open" => await GetOpenAsync(filter),
            "waiting" => await GetWaitingRepliesAsync(filter),
            "pending-response" or "response-required" => await GetPendingResponseAsync(filter),
            "overdue-responses" => await GetOverdueResponsesAsync(filter),
            "pending-assignments" => await GetPendingAssignmentsAsync(filter),
            "partial-replies" => await GetPartialRepliesAsync(filter),
            _ => await GetOpenAsync(filter)
        };

        ws.Cell(1, 1).Value = "رقم التتبع";
        ws.Cell(1, 2).Value = "رقم الوارد";
        ws.Cell(1, 3).Value = "تاريخ الوارد";
        ws.Cell(1, 4).Value = "الموضوع";
        ws.Cell(1, 5).Value = "الإدارة";
        ws.Cell(1, 6).Value = "الجهة الوارد منها";
        ws.Cell(1, 7).Value = "الحالة";
        ws.Cell(1, 8).Value = "التصنيف";
        ws.Cell(1, 9).Value = "الأولوية";

        for (var i = 0; i < data.Count; i++)
        {
            var row = i + 2;
            var t = data[i];
            ws.Cell(row, 1).Value = t.InternalTrackingNumber;
            ws.Cell(row, 2).Value = t.IncomingNumber;
            ws.Cell(row, 3).Value = t.IncomingDate.ToString("yyyy-MM-dd");
            ws.Cell(row, 4).Value = t.Subject;
            ws.Cell(row, 5).Value = t.OutgoingDepartmentNames.Count > 0
                ? string.Join("، ", t.OutgoingDepartmentNames) : "-";
            ws.Cell(row, 6).Value = t.IncomingFrom ?? "";
            ws.Cell(row, 7).Value = t.Status;
            ws.Cell(row, 8).Value = t.CategoryName ?? "";
            ws.Cell(row, 9).Value = t.Priority;
        }

        ws.RightToLeft = true;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private async Task<List<Models.Entities.Transaction>> LoadTransactionsAsync() =>
        await _db.Transactions
            .Include(t => t.CategoryEntity)
            .Include(t => t.OutgoingDepartments).ThenInclude(o => o.Department)
            .Include(t => t.Assignments).ThenInclude(a => a.Department)
            .Include(t => t.CreatedBy)
            .ToListAsync();

    private async Task<List<TransactionListDto>> GetFilteredAsync(
        Func<Models.Entities.Transaction, bool> predicate, ReportFilterRequest? filter)
    {
        var now = DateTime.UtcNow;
        var query = ApplyReportFilter(
            _db.Transactions.Include(t => t.CreatedBy).Include(t => t.CategoryEntity)
                .Include(t => t.OutgoingDepartments).ThenInclude(o => o.Department)
                .Include(t => t.Assignments),
            filter);
        var list = await query.OrderByDescending(t => t.IncomingDate).ToListAsync();
        return list.Where(predicate).Select(t => MapList(t, now)).ToList();
    }

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

    private static string? ResolveIncomingFromName(Models.Entities.Transaction t) =>
        t.IncomingSourceType switch
        {
            IncomingSourceType.Internal => t.IncomingFromDepartment?.Name ?? t.IncomingFrom,
            IncomingSourceType.External => t.IncomingFromParty?.Name ?? t.IncomingFrom,
            _ => t.IncomingFrom
        };

    private static TransactionListDto MapList(Models.Entities.Transaction t, DateTime now) => new()
    {
        Id = t.Id,
        InternalTrackingNumber = t.InternalTrackingNumber,
        IncomingNumber = t.IncomingNumber,
        IncomingDate = t.IncomingDate,
        Subject = t.Subject,
        IncomingFrom = ResolveIncomingFromName(t),
        IncomingSourceType = t.IncomingSourceType.ToString(),
        OutgoingNumber = t.OutgoingNumber,
        OutgoingDate = t.OutgoingDate,
        OutgoingDepartmentNames = t.OutgoingDepartments.Select(o => o.Department.Name).ToList(),
        OutgoingPartyNames = t.OutgoingDepartments.Select(o => o.Department.Name).ToList(),
        Status = t.Status.ToString(),
        Priority = t.Priority.ToString(),
        CategoryName = t.CategoryEntity?.Name ?? t.Category,
        RequiresResponse = t.RequiresResponse,
        ResponseCompleted = t.ResponseCompleted,
        ResponseDueDate = t.ResponseDueDate,
        IsResponseOverdue = WorkflowHelper.IsResponseOverdue(t, now),
        HasPendingAssignments = t.Assignments.Any(a => a.RequiresReply && a.ReplyStatus != ReplyStatus.Replied && a.Status == AssignmentStatus.Active),
        IsOverdue = WorkflowHelper.IsTransactionOverdue(t, now),
        IsArchived = t.IsArchived,
        CreatedByName = t.CreatedBy?.FullName ?? "",
        CreatedAt = t.CreatedAt
    };
}
