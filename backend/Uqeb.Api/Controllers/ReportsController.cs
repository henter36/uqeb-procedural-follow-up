using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.DTOs.Reports;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Helpers;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reports;
    private readonly IMemoryCacheCoordinator _cache;
    private readonly ICacheInvalidationService _cacheInvalidation;

    public ReportsController(
        IReportService reports,
        IMemoryCacheCoordinator cache,
        ICacheInvalidationService cacheInvalidation)
    {
        _reports = reports;
        _cache = cache;
        _cacheInvalidation = cacheInvalidation;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var result = await _cache.GetOrCreateAsync(
            _cacheInvalidation.BuildDashboardFullKey(),
            () => _reports.GetDashboardAsync(),
            _cacheInvalidation.DashboardCacheDuration);
        return Ok(result);
    }

    [HttpGet("page-summary")]
    public async Task<IActionResult> PageSummary([FromQuery] ReportFilterRequest filter)
    {
        var result = await _cache.GetOrCreateAsync(
            _cacheInvalidation.BuildReportsPageSummaryKey(filter),
            () => _reports.GetPageSummaryAsync(filter),
            _cacheInvalidation.ReportsPageSummaryCacheDuration);
        return Ok(result);
    }

    [HttpGet("overdue")]
    public async Task<IActionResult> Overdue([FromQuery] ReportFilterRequest filter) =>
        Ok(await _reports.GetOverdueAsync(filter));

    [HttpGet("open")]
    public async Task<IActionResult> Open([FromQuery] ReportFilterRequest filter) =>
        Ok(await _reports.GetOpenAsync(filter));

    [HttpGet("waiting-replies")]
    public async Task<IActionResult> WaitingReplies([FromQuery] ReportFilterRequest filter) =>
        Ok(await _reports.GetWaitingRepliesAsync(filter));

    [HttpGet("pending-response")]
    public async Task<IActionResult> PendingResponse([FromQuery] ReportFilterRequest filter) =>
        Ok(await _reports.GetPendingResponseAsync(filter));

    [HttpGet("response-required")]
    public async Task<IActionResult> ResponseRequired([FromQuery] ReportFilterRequest filter) =>
        Ok(await _reports.GetResponseRequiredAsync(filter));

    [HttpGet("overdue-responses")]
    public async Task<IActionResult> OverdueResponses([FromQuery] ReportFilterRequest filter) =>
        Ok(await _reports.GetOverdueResponsesAsync(filter));

    [HttpGet("pending-assignments")]
    public async Task<IActionResult> PendingAssignments([FromQuery] ReportFilterRequest filter) =>
        Ok(await _reports.GetPendingAssignmentsAsync(filter));

    [HttpGet("partial-replies")]
    public async Task<IActionResult> PartialReplies([FromQuery] ReportFilterRequest filter) =>
        Ok(await _reports.GetPartialRepliesAsync(filter));

    [HttpGet("by-department")]
    public async Task<IActionResult> ByDepartment([FromQuery] ReportFilterRequest filter) =>
        Ok(await _reports.GetByDepartmentAsync(filter));

    [HttpGet("by-external-party")]
    public async Task<IActionResult> ByExternalParty([FromQuery] ReportFilterRequest filter) =>
        Ok(await _reports.GetByExternalPartyAsync(filter));

    [HttpGet("by-category")]
    public async Task<IActionResult> ByCategory([FromQuery] ReportFilterRequest filter) =>
        Ok(await _reports.GetByCategoryAsync(filter));

    [HttpGet("by-incoming-party")]
    public async Task<IActionResult> ByIncomingParty([FromQuery] ReportFilterRequest filter) =>
        Ok(await _reports.GetByIncomingPartyAsync(filter));

    [HttpGet("by-outgoing-party")]
    public async Task<IActionResult> ByOutgoingParty([FromQuery] ReportFilterRequest filter) =>
        Ok(await _reports.GetByOutgoingPartyAsync(filter));

    [HttpGet("by-outgoing-department")]
    public async Task<IActionResult> ByOutgoingDepartment([FromQuery] ReportFilterRequest filter) =>
        Ok(await _reports.GetByOutgoingDepartmentAsync(filter));

    [HttpGet("department-summary")]
    public async Task<IActionResult> DepartmentSummary([FromQuery] ReportFilterRequest filter) =>
        Ok(await _reports.GetDepartmentSummaryAsync(filter));

    [HttpGet("department-incoming-closed")]
    public async Task<IActionResult> DepartmentIncomingClosed([FromQuery] ReportFilterRequest filter) =>
        Ok(await _reports.GetDepartmentIncomingClosedAsync(filter));

    [HttpGet("department-incoming-closed/export-excel")]
    public async Task<IActionResult> ExportDepartmentIncomingClosedExcel([FromQuery] ReportFilterRequest filter)
    {
        var bytes = await _reports.ExportDepartmentIncomingClosedExcelAsync(filter);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"department-incoming-closed-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpGet("department-incoming-closed/export-pdf")]
    public async Task<IActionResult> ExportDepartmentIncomingClosedPdf([FromQuery] ReportFilterRequest filter)
    {
        var bytes = await _reports.ExportDepartmentIncomingClosedPdfAsync(filter);
        return File(bytes, "application/pdf", $"department-incoming-closed-{DateTime.UtcNow:yyyyMMdd}.pdf");
    }

    [HttpGet("monthly")]
    public async Task<IActionResult> Monthly([FromQuery] int year = 0)
    {
        if (year == 0) year = DateTime.UtcNow.Year;
        return Ok(await _reports.GetMonthlyAsync(year));
    }

    [HttpGet("export/{reportType}")]
    public async Task<IActionResult> Export(string reportType, [FromQuery] ReportFilterRequest filter, [FromQuery] bool currentPageOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 5)
    {
        var pagedFilter = new ReportPagedFilterRequest
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
            Page = page,
            PageSize = ReportPageSize.Normalize(pageSize)
        };
        var bytes = await _reports.ExportReportDetailsExcelAsync(
            reportType,
            pagedFilter,
            currentPageOnly,
            HttpContext.RequestAborted);
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            LegacyReportExportHelper.BuildExcelFileName(reportType));
    }

    [HttpGet("response-required/details")]
    public async Task<IActionResult> ResponseRequiredDetails([FromQuery] ReportPagedFilterRequest filter) =>
        await PagedDetailsAsync(filter, _reports.GetResponseRequiredDetailsAsync);

    [HttpGet("overdue-responses/details")]
    public async Task<IActionResult> OverdueResponsesDetails([FromQuery] ReportPagedFilterRequest filter) =>
        await PagedDetailsAsync(filter, _reports.GetOverdueResponsesDetailsAsync);

    [HttpGet("open-assignments/details")]
    [HttpGet("pending-assignments/details")]
    public async Task<IActionResult> OpenAssignmentsDetails([FromQuery] ReportPagedFilterRequest filter) =>
        await PagedDetailsAsync(filter, _reports.GetOpenAssignmentsDetailsAsync);

    [HttpGet("partial-replies/details")]
    public async Task<IActionResult> PartialRepliesDetails([FromQuery] ReportPagedFilterRequest filter) =>
        await PagedDetailsAsync(filter, _reports.GetPartialRepliesDetailsAsync);

    [HttpGet("overdue/details")]
    public async Task<IActionResult> OverdueDetails([FromQuery] ReportPagedFilterRequest filter) =>
        await PagedDetailsAsync(filter, _reports.GetOverdueDetailsAsync);

    [HttpGet("waiting-reply/details")]
    [HttpGet("waiting-replies/details")]
    public async Task<IActionResult> WaitingReplyDetails([FromQuery] ReportPagedFilterRequest filter) =>
        await PagedDetailsAsync(filter, _reports.GetWaitingReplyDetailsAsync);

    [HttpGet("open/details")]
    public async Task<IActionResult> OpenDetails([FromQuery] ReportPagedFilterRequest filter) =>
        await PagedDetailsAsync(filter, _reports.GetOpenDetailsAsync);

    private static async Task<IActionResult> PagedDetailsAsync(
        ReportPagedFilterRequest filter,
        Func<ReportPagedFilterRequest, Task<PagedResult<ReportTransactionRowDto>>> loader)
    {
        if (!ReportPageSize.IsAllowed(filter.PageSize))
            return new BadRequestObjectResult(new { message = "حجم الصفحة يجب أن يكون 5 أو 10 أو 50" });
        filter.PageSize = ReportPageSize.Normalize(filter.PageSize);
        filter.Page = Math.Max(1, filter.Page);
        return new OkObjectResult(await loader(filter));
    }
}
