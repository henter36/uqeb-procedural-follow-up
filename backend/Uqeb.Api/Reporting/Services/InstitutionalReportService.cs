using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Reports;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Helpers;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Rendering;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Services;

namespace Uqeb.Api.Reporting.Services;

public interface IInstitutionalReportService
{
    Task<InstitutionalReportModel> BuildReportModelAsync(ReportBuildRequestDto request, CancellationToken ct = default);
    Task<RenderedReportManifestDto> RenderPreviewAsync(ReportBuildRequestDto request, CancellationToken ct = default);
    Task<ReportExportResultDto> ExportAsync(ReportExportRequestDto request, CancellationToken ct = default);
    Task<List<ReportTemplateDto>> GetTemplatesAsync(CancellationToken ct = default);
    Task<ReportTemplateDto> SaveTemplateAsync(SaveReportTemplateRequestDto request, CancellationToken ct = default);
    Task DeleteTemplateAsync(int id, CancellationToken ct = default);
}

public sealed class InstitutionalReportService : IInstitutionalReportService, IInstitutionalReportBuildSupport
{
    private const string IsoDateFormat = "yyyy-MM-dd";

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ICurrentUserService _currentUser;
    private readonly IInstitutionalReportNumberAllocator _reportNumberAllocator;
    private readonly ReportingOptions _reportingOptions;
    private readonly DepartmentRatingCriteria _ratingCriteria = new();
    private readonly InstitutionalReportRenderer _renderer = new();
    private readonly ILogger<InstitutionalReportService> _logger;
    private readonly IReportingCorrelationIdProvider _correlationIdProvider;
    private readonly IReportingAnalysisInstrumentation _analysisInstrumentation;
    private readonly Func<IInstitutionalReportExportService> _exportServiceFactory;

    public InstitutionalReportService(
        IDbContextFactory<AppDbContext> dbFactory,
        ICurrentUserService currentUser,
        IInstitutionalReportNumberAllocator reportNumberAllocator,
        IOptions<ReportingOptions> reportingOptions,
        ILogger<InstitutionalReportService> logger,
        IReportingCorrelationIdProvider correlationIdProvider,
        IReportingAnalysisInstrumentation analysisInstrumentation,
        Func<IInstitutionalReportExportService> exportServiceFactory)
    {
        _dbFactory = dbFactory;
        _currentUser = currentUser;
        _reportNumberAllocator = reportNumberAllocator;
        _reportingOptions = reportingOptions.Value;
        _logger = logger;
        _correlationIdProvider = correlationIdProvider;
        _analysisInstrumentation = analysisInstrumentation;
        _exportServiceFactory = exportServiceFactory;
    }

    public Task<InstitutionalReportModel> BuildReportModelAsync(ReportBuildRequestDto request, CancellationToken ct = default) =>
        BuildForApiAsync(request, ct);

    private async Task<InstitutionalReportModel> BuildForApiAsync(ReportBuildRequestDto request, CancellationToken ct)
    {
        InstitutionalReportRequestValidator.ValidateBuildRequest(request, _reportingOptions);
        var detailLimit = _reportingOptions.MaxPreviewDetailRows;
        var totalMatching = await CountMatchingTransactionsAsync(request, ct);
        return await BuildInternalAsync(request, ct, ReportAssemblyOptions.ForPreview(totalMatching, detailLimit));
    }

    public async Task<RenderedReportManifestDto> RenderPreviewAsync(ReportBuildRequestDto request, CancellationToken ct = default)
    {
        InstitutionalReportRequestValidator.ValidateBuildRequest(request, _reportingOptions);
        var correlationId = _correlationIdProvider.GetCorrelationId();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Institutional report preview started. ReportType={ReportType} CorrelationId={CorrelationId}",
            request.ReportType,
            correlationId);

        var detailLimit = _reportingOptions.MaxPreviewDetailRows;

        _logger.LogDebug(
            "Institutional report preview count_matching started. PreviewStage=count_matching CorrelationId={CorrelationId}",
            correlationId);
        var totalMatching = await CountMatchingTransactionsAsync(request, ct);

        _logger.LogDebug(
            "Institutional report preview build_model started. PreviewStage=build_model CorrelationId={CorrelationId}",
            correlationId);
        var model = await BuildInternalAsync(
            request,
            ct,
            ReportAssemblyOptions.ForPreview(totalMatching, detailLimit));

        var sections = InstitutionalReportRequestValidator.ResolveSections(request);
        _logger.LogDebug(
            "Institutional report preview render_manifest started. PreviewStage=render_manifest CorrelationId={CorrelationId}",
            correlationId);
        var manifest = _renderer.RenderManifest(model, sections);

        stopwatch.Stop();
        var enriched = InstitutionalReportManifestEnricher.Enrich(manifest, model, isSummaryOnly: false, overflowAction: null);
        _logger.LogInformation(
            "Institutional report preview completed. ReportType={ReportType} MatchedRows={MatchedRows} PageCount={PageCount} DurationMs={DurationMs} CorrelationId={CorrelationId}",
            request.ReportType,
            totalMatching,
            manifest.Pages.Count,
            stopwatch.Elapsed.TotalMilliseconds,
            correlationId);

        return enriched;
    }

    public Task<ReportExportResultDto> ExportAsync(ReportExportRequestDto request, CancellationToken ct = default) =>
        _exportServiceFactory().ExportAsync(request, ct);

    public async Task<List<ReportTemplateDto>> GetTemplatesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var templates = await db.ReportExportTemplates.AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        return templates.Select(MapTemplate).ToList();
    }

    public async Task<ReportTemplateDto> SaveTemplateAsync(SaveReportTemplateRequestDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["name"] = "اسم القالب مطلوب."
            });
        }

        if (request.ReportType is null || !Enum.IsDefined(request.ReportType.Value))
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["reportType"] = "نوع التقرير غير صالح."
            });
        }

        if (request.DefaultFormat.HasValue && !Enum.IsDefined(request.DefaultFormat.Value))
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["defaultFormat"] = "صيغة التصدير الافتراضية غير صالحة."
            });
        }

        request.DefaultFilters ??= new ReportFiltersDto();
        InstitutionalReportRequestValidator.NormalizeFilterLists(request.DefaultFilters);
        InstitutionalReportRequestValidator.ValidateDepartmentTransactionsRequiresDepartments(
            request.ReportType.Value, request.DefaultFilters, "defaultFilters.departmentIds");
        InstitutionalReportRequestValidator.ValidateDepartmentTransactionsExcludedDepartments(
            request.ReportType.Value, request.DefaultFilters, "defaultFilters.excludedDepartmentIds");
        InstitutionalReportRequestValidator.ValidateDepartmentTransactionScope(
            request.DefaultFilters, "defaultFilters.departmentTransactionScope");
        InstitutionalReportRequestValidator.ValidateDetailSortBy(request.DetailSortBy);

        var templateOptions = InstitutionalReportExportOptionsResolver.Resolve(request);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var userExists = await db.Users.AsNoTracking().AnyAsync(u => u.Id == _currentUser.UserId, ct);
        if (!userExists)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["createdById"] = "المستخدم الحالي غير موجود."
            });
        }

        var entity = new ReportExportTemplate
        {
            Name = request.Name.Trim(),
            ReportType = templateOptions.ReportType,
            SectionIdsJson = System.Text.Json.JsonSerializer.Serialize(request.SectionIds),
            DefaultFiltersJson = System.Text.Json.JsonSerializer.Serialize(request.DefaultFilters),
            DefaultFormat = templateOptions.DefaultFormat,
            PageNumberingMode = templateOptions.PageNumberingMode,
            IncludePartialCover = templateOptions.IncludePartialCover,
            IncludePartialManifest = templateOptions.IncludePartialManifest,
            DetailSortBy = templateOptions.DetailSortBy,
            GroupDetailsByDepartment = templateOptions.GroupDetailsByDepartment,
            CreatedById = _currentUser.UserId,
            CreatedAt = DateTime.UtcNow
        };
        db.ReportExportTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        return MapTemplate(entity);
    }

    public async Task DeleteTemplateAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.ReportExportTemplates.FindAsync([id], ct)
            ?? throw new KeyNotFoundException("القالب غير موجود.");
        db.ReportExportTemplates.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    private async Task<InstitutionalReportModel> BuildInternalAsync(
        ReportBuildRequestDto request,
        CancellationToken ct,
        ReportAssemblyOptions options)
    {
        var detailLimit = options.DetailRowLimit > 0 ? options.DetailRowLimit : _reportingOptions.MaxPreviewDetailRows;
        ReportingOptions.ValidateDetailLimit(detailLimit);
        var generatedAt = DateTime.UtcNow;
        var referenceDate = ReportingTemporalCalculator.RiyadhBusinessDate();

        // The overdue report evaluates "is this overdue" as of the requested period end
        // (DateTo), not as of today, so old-but-still-overdue transactions from earlier
        // periods aren't dropped just because a later period was requested. Every other
        // report type keeps evaluating against today (same as referenceDate).
        var overdueEvaluationDate = InstitutionalReportSnapshotQuery.ResolveOverdueEvaluationDate(
            request.ReportType, request.Filters.DateTo);

        var metricSnapshots = await LoadSnapshotsAsync(request, ct, takeLimit: null, overdueEvaluationDate);
        metricSnapshots = ApplyOverdueScope(metricSnapshots, request);
        var totalMatched = metricSnapshots.Count;
        var metrics = InstitutionalReportMetricsCalculator.Calculate(metricSnapshots, overdueEvaluationDate);
        // The executive summary cards/narrative and the open/closed chart must reflect the selected
        // departments' own DepartmentTransactionPerformanceState, not the overall transaction status —
        // a transaction can be Completed for department A while it (and department B) remain Open
        // overall. `metrics` above stays transaction-level and keeps driving DepartmentPerformance,
        // Risks/Recommendations/RiskCounters and ValidateIntegrity unchanged, since those already
        // read per-department state internally where it matters or intentionally describe the whole
        // matched population.
        var isDepartmentTransactions = request.ReportType == InstitutionalReportType.DepartmentTransactions;
        var summaryMetrics = isDepartmentTransactions
            ? InstitutionalReportMetricsCalculator.CalculateForDepartmentTransactions(metricSnapshots, request.Filters.DepartmentIds)
            : metrics;
        var comparisonRequest = InstitutionalReportAnalysisService.CreateComparisonRequest(request, out var comparisonUnavailableReason);
        // CreateComparisonRequest can shift Filters.DateTo to a prior period (previous-period /
        // year-over-year comparisons), so the comparison snapshots must be evaluated for
        // overdue-ness against their own period end, not the current request's.
        var comparisonOverdueEvaluationDate = comparisonRequest is null
            ? referenceDate
            : InstitutionalReportSnapshotQuery.ResolveOverdueEvaluationDate(
                comparisonRequest.ReportType, comparisonRequest.Filters.DateTo);
        var comparisonSnapshots = comparisonRequest is null
            ? []
            : await LoadSnapshotsAsync(comparisonRequest, ct, takeLimit: null, comparisonOverdueEvaluationDate);
        if (comparisonRequest is not null)
            comparisonSnapshots = ApplyOverdueScope(comparisonSnapshots, comparisonRequest);
        var comparisonMetrics = comparisonRequest is null
            ? null
            : InstitutionalReportMetricsCalculator.Calculate(comparisonSnapshots, comparisonOverdueEvaluationDate);

        var (detailSnapshots, exportedDetailRows) = ResolveDetailRows(
            options, totalMatched, metricSnapshots);

        // Sorting/grouping is a pure post-processing step on the already-loaded, already-truncated
        // detail rows: it never changes which rows survive truncation (still governed by
        // LoadSnapshotsAsync's DB-level order) or the metricSnapshots-driven aggregates above.
        var (sortedDetailSnapshots, effectiveSort) = ApplyDetailSort(detailSnapshots, request);
        var (detailRows, groupedByDepartment) = BuildTransactionDetails(sortedDetailSnapshots, request);
        // Grouping re-sorts rows by department regardless of the requested/default sort (see
        // BuildTransactionDetails), so the reported effective sort must reflect that final order -
        // otherwise exported metadata would claim e.g. "DueDate" while rows are actually grouped by department.
        var finalEffectiveSort = groupedByDepartment ? ReportDetailSortBy.Department : effectiveSort;

        var detailRowsTruncated = options.DetailRowsTruncated || totalMatched > exportedDetailRows || options.DetailPartsCount > 1;
        string reportNumber;
        if (options.Purpose == ReportBuildPurpose.Preview)
        {
            reportNumber = $"PREVIEW-{DateTime.UtcNow:yyyyMMddHHmmss}";
        }
        else
        {
            reportNumber = await _reportNumberAllocator.AllocateAsync(ct);
        }

        var verificationId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

        var departmentPerformance = BuildDepartmentPerformance(metrics.Snapshots, referenceDate);

        var model = new InstitutionalReportModel
        {
            TotalMatchedRows = totalMatched,
            ExportedDetailRows = exportedDetailRows,
            DetailRowsTruncated = detailRowsTruncated,
            DetailPartsCount = options.DetailPartsCount,
            Metadata = new ReportMetadataDto
            {
                ReportNumber = reportNumber,
                ReportType = request.ReportType,
                ReportTypeName = ReportTypeLabel(request.ReportType),
                IssueDate = referenceDate,
                PeriodFrom = ResolvePeriodStart(request.Filters.DateFrom, metricSnapshots, s => s.IncomingDate, referenceDate),
                PeriodTo = ResolvePeriodEnd(request.Filters.DateTo, metricSnapshots, s => s.IncomingDate, referenceDate),
                Title = string.IsNullOrWhiteSpace(request.Title)
                    ? "تقرير المتابعة الإجرائية للمعاملات"
                    : request.Title.Trim(),
                Introduction = request.Introduction,
                VerificationId = verificationId,
                GeneratedAt = generatedAt,
                TotalMatchingTransactions = totalMatched,
                IncludedTransactionCount = detailSnapshots.Count,
                DetailRowLimit = detailLimit,
            },
            Filters = request.Filters,
            Summary = BuildExecutiveSummary(
                summaryMetrics, metricSnapshots, isDepartmentTransactions ? request.Filters.DepartmentIds : []),
            Charts = BuildCharts(
                summaryMetrics, metricSnapshots, isDepartmentTransactions ? request.Filters.DepartmentIds : []),
            DepartmentPerformance = departmentPerformance,
            Risks = BuildRisks(metrics.Snapshots),
            Recommendations = BuildRecommendations(metrics.Snapshots, referenceDate, _reportingOptions.Analysis),
            RiskCounters = BuildRiskCounters(metrics.Snapshots, referenceDate, _reportingOptions.Analysis),
            Transactions = detailRows,
            IntegrityWarnings = ValidateIntegrity(metrics, departmentPerformance),
            DetailSortByEffective = finalEffectiveSort,
            GroupDetailsByDepartmentEffective = groupedByDepartment,
            DetailRowsAreAdditive = !groupedByDepartment,
            ComparisonUnavailableReason = comparisonUnavailableReason,
        };

        model.Analysis = InstitutionalReportAnalysisService.Build(
            new InstitutionalReportAnalysisService.InstitutionalReportAnalysisInput
            {
                Request = request,
                Metadata = model.Metadata,
                Filters = model.Filters,
                CurrentMetrics = metrics,
                CurrentSnapshots = metricSnapshots,
                PreviousMetrics = comparisonMetrics,
                PreviousSnapshots = comparisonSnapshots,
                Options = _reportingOptions.Analysis,
                DetailLimit = detailLimit,
                DetailRowsTruncated = detailRowsTruncated
            },
            _analysisInstrumentation);

        if (detailRowsTruncated && totalMatched > detailSnapshots.Count)
        {
            model.IntegrityWarnings.Add(new IntegrityWarningDto
            {
                Code = "DETAIL_ROWS_TRUNCATED",
                Message = $"جدول التفاصيل يعرض {exportedDetailRows:N0} صفًا من إجمالي {totalMatched:N0} معاملة مطابقة للفلاتر.",
                Severity = "warning"
            });
        }

        return model;
    }

    private static (List<TransactionReportSnapshot> Snapshots, int ExportedCount) ResolveDetailRows(
        ReportAssemblyOptions options,
        int totalMatched,
        List<TransactionReportSnapshot> metricSnapshots)
    {
        if (options.OmitDetailRows)
            return ([], options.ExportedDetailRowsOverride ?? 0);

        if (options.DetailRowsToLoad.HasValue)
        {
            var exportedCount = options.ExportedDetailRowsOverride ?? Math.Min(totalMatched, options.DetailRowsToLoad.Value);
            var snapshots = exportedCount >= metricSnapshots.Count
                ? metricSnapshots
                : metricSnapshots.Take(exportedCount).ToList();
            return (snapshots, exportedCount);
        }

        return (metricSnapshots, options.ExportedDetailRowsOverride ?? totalMatched);
    }

    async Task<int> IInstitutionalReportBuildSupport.CountMatchingTransactionsAsync(
        ReportBuildRequestDto request,
        CancellationToken ct) =>
        await CountMatchingTransactionsAsync(request, ct);

    async Task<InstitutionalReportModel> IInstitutionalReportBuildSupport.BuildInternalAsync(
        ReportBuildRequestDto request,
        CancellationToken ct,
        ReportAssemblyOptions options) =>
        await BuildInternalAsync(request, ct, options);

    private async Task<int> CountMatchingTransactionsAsync(ReportBuildRequestDto request, CancellationToken ct)
    {
        var referenceDate = InstitutionalReportSnapshotQuery.ResolveOverdueEvaluationDate(
            request.ReportType,
            request.Filters.DateTo);
        var snapshots = await LoadSnapshotsAsync(request, ct, takeLimit: null, referenceDate);
        snapshots = ApplyOverdueScope(snapshots, request);
        return snapshots.Count;
    }

    private static List<TransactionReportSnapshot> ApplyOverdueScope(
        List<TransactionReportSnapshot> snapshots,
        ReportBuildRequestDto request)
    {
        if (!request.Filters.IncludeOverdue)
            return snapshots;

        if (request.ReportType != InstitutionalReportType.DepartmentTransactions)
            return snapshots.Where(snapshot => snapshot.IsOverdue).ToList();

        var selectedDepartmentIds = request.Filters.DepartmentIds.ToHashSet();
        return snapshots.Where(snapshot => snapshot.DepartmentPerformanceStates.Any(state =>
            selectedDepartmentIds.Contains(state.DepartmentId)
            && state.IsOverdueForDepartment)).ToList();
    }

    private async Task<List<TransactionReportSnapshot>> LoadSnapshotsAsync(
        ReportBuildRequestDto request,
        CancellationToken ct,
        int? takeLimit = null,
        DateTime? referenceDate = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = InstitutionalReportQueryBuilder.BuildFilteredQuery(
            db,
            request,
            _currentUser.UserId,
            _currentUser.Role,
            _currentUser.DepartmentId);

        query = query
            .OrderByDescending(t => t.IncomingDate)
            .ThenByDescending(t => t.Id);

        var requiresDepartmentOpenScope = request.ReportType == InstitutionalReportType.DepartmentTransactions
            && request.Filters.DepartmentTransactionScope == DepartmentTransactionScope.OpenOnly;

        // Department open/complete state is derived after projection from assignment observations.
        // Apply the preview/export row cap after that state filter so completed rows cannot consume
        // the cap and hide later rows that are genuinely open for a selected department.
        if (takeLimit.HasValue && !requiresDepartmentOpenScope)
            query = query.Take(takeLimit.Value);

        var rows = await query
            .Select(InstitutionalReportSnapshotQuery.SnapshotRowProjection)
            .ToListAsync(ct);

        var evaluationDate = referenceDate?.Date ?? DateTime.UtcNow.Date;
        var snapshots = rows.Select(row => InstitutionalReportSnapshotQuery.MapRowToSnapshot(row, evaluationDate)).ToList();
        ApplyPeriodScope(snapshots, request.Filters.DateFrom, evaluationDate);
        var scopedSnapshots = snapshots.Where(IsInFinalReportScope);
        if (requiresDepartmentOpenScope)
        {
            var selectedDepartmentIds = request.Filters.DepartmentIds.ToHashSet();
            scopedSnapshots = scopedSnapshots.Where(snapshot => snapshot.DepartmentPerformanceStates.Any(state =>
                selectedDepartmentIds.Contains(state.DepartmentId)
                && state.IsOpenForDepartment));
        }
        if (takeLimit.HasValue && requiresDepartmentOpenScope)
            scopedSnapshots = scopedSnapshots.Take(takeLimit.Value);
        return scopedSnapshots.ToList();
    }

    private static bool IsInFinalReportScope(TransactionReportSnapshot snapshot) =>
        snapshot.IsPeriodIncoming || snapshot.IsCarriedOpenBalance;

    private static void ApplyPeriodScope(
        IEnumerable<TransactionReportSnapshot> snapshots,
        DateTime? dateFrom,
        DateTime evaluationDate)
    {
        var from = dateFrom?.Date ?? DateTime.MinValue.Date;
        var to = evaluationDate.Date;
        foreach (var snapshot in snapshots)
        {
            snapshot.IsPeriodIncoming = snapshot.IncomingDate.Date >= from && snapshot.IncomingDate.Date <= to;
            snapshot.IsCarriedOpenBalance = dateFrom.HasValue
                && snapshot.IncomingDate.Date < from
                && snapshot.IsOpen;
        }
    }

    /// <summary>
    /// For DepartmentTransactions, <paramref name="metrics"/> must already be the department-scoped
    /// result of <see cref="InstitutionalReportMetricsCalculator.CalculateForDepartmentTransactions"/>
    /// (one observation per matched Transaction+selected-Department relationship), and
    /// <paramref name="selectedDepartmentIds"/> non-empty so the "top overdue department" narrative
    /// line only considers the selected departments too. For every other report type, callers pass
    /// the unchanged transaction-level <see cref="InstitutionalReportMetricsCalculator.Calculate"/>
    /// result and an empty department list, leaving this function's behavior exactly as before.
    /// </summary>
    private static ExecutiveSummaryDto BuildExecutiveSummary(
        InstitutionalMetricsResult metrics,
        IReadOnlyList<TransactionReportSnapshot> snapshots,
        IReadOnlyList<int> selectedDepartmentIds)
    {
        var cards = new List<KpiCardDto>
        {
            new() { Key = "periodIncoming", Title = "وارد الفترة", Value = metrics.PeriodIncomingCount.ToString("N0"), Footnote = "معاملات وردت داخل الفترة فقط." },
            new() { Key = "carriedOpenBalance", Title = "المعاملات مرحلة لفترة سابقة", Value = metrics.CarriedOpenBalanceCount.ToString("N0"), Footnote = "معاملات أقدم من الفترة وما زالت مفتوحة حتى نهايتها." },
            new() { Key = "activeBurden", Title = "إجمالي المعاملات القائمة", Value = metrics.TotalActiveBurdenCount.ToString("N0"), Footnote = "وارد الفترة المفتوح + المعاملات المرحلة من فترة سابقة." },
            new() { Key = "total", Title = "إجمالي نطاق التقرير", Value = metrics.TotalTransactions.ToString("N0") },
            new() { Key = "closed", Title = "المعاملات المغلقة", Value = metrics.ClosedCount.ToString("N0") },
            new() { Key = "open", Title = "المعاملات المفتوحة", Value = metrics.OpenCount.ToString("N0") },
            new() { Key = "overdue", Title = "إجمالي المتأخرات", Value = metrics.OverdueCount.ToString("N0"), Footnote = "يشمل المفتوحة المتأخرة والمنجزة/المغلقة بعد الاستحقاق." },
            new() { Key = "openOverdue", Title = "متأخرة مفتوحة", Value = metrics.OpenOverdueCount.ToString("N0") },
            new() { Key = "completedLate", Title = "منجزة متأخرة", Value = metrics.CompletedLateCount.ToString("N0") },
            new() { Key = "joint", Title = "معاملات الإدارات المشتركة", Value = metrics.JointDepartmentCount.ToString("N0") },
            new() { Key = "partial", Title = "الردود الجزئية", Value = metrics.PartialResponseCount.ToString("N0") },
            new() { Key = "avgDays", Title = "متوسط مدة الإنجاز", Value = $"{metrics.AverageCompletionDays:N1} يوم" },
            new() { Key = "onTime", Title = "نسبة الإنجاز ضمن المهلة", Value = $"{metrics.OnTimeCompletionRate:N1}%" }
        };

        var departmentObservationsForNarrative = DepartmentTransactionPerformanceObservationResolver.Expand(snapshots);
        if (selectedDepartmentIds.Count > 0)
        {
            var selected = selectedDepartmentIds.ToHashSet();
            departmentObservationsForNarrative = departmentObservationsForNarrative
                .Where(observation => selected.Contains(observation.State.DepartmentId))
                .ToList();
        }

        var topOverdueDept = departmentObservationsForNarrative
            .Where(observation => observation.State.IsOpenOverdueForDepartment)
            .GroupBy(observation => observation.State.DepartmentId)
            .OrderByDescending(g => g.Count())
            .Select(group => ReportDepartmentNameNormalizer.Normalize(group.First().State.DepartmentName))
            .FirstOrDefault();

        // For DepartmentTransactions, `metrics` counts Transaction+selected-Department relationships,
        // not unique transactions — a transaction shared by two selected departments is counted twice.
        // "فريدة" (unique) would misstate that, so the wording is scoped accordingly.
        var scopeNoun = selectedDepartmentIds.Count > 0 ? "علاقة معاملة/إدارة" : "معاملة فريدة";
        var openClosedVerbs = selectedDepartmentIds.Count > 0
            ? $"منها {metrics.ClosedCount:N0} منجزة لدى الإدارة و{metrics.OpenCount:N0} مفتوحة لدى الإدارة. "
            : $"منها {metrics.ClosedCount:N0} مغلقة و{metrics.OpenCount:N0} مفتوحة. ";

        var narrative = $"بلغ وارد الفترة {metrics.PeriodIncomingCount:N0} معاملة وردت داخل الفترة فقط، " +
                        $"والمعاملات المرحلة من فترة سابقة {metrics.CarriedOpenBalanceCount:N0} معاملة أقدم من الفترة وما زالت مفتوحة حتى نهايتها. " +
                        $"إجمالي المعاملات القائمة بلغ {metrics.TotalActiveBurdenCount:N0} معاملة، " +
                        $"ونطاق التقرير التشغيلي يضم {metrics.TotalTransactions:N0} {scopeNoun}، " +
                        openClosedVerbs +
                        $"تضم المعاملات المفتوحة {metrics.OpenOverdueCount:N0} معاملة متأخرة، " +
                        $"وتوجد {metrics.CompletedLateCount:N0} معاملة منجزة/مغلقة بعد الاستحقاق، " +
                        $"و{metrics.PartialResponseCount:N0} ردًا جزئيًا " +
                        $"و{metrics.JointDepartmentCount:N0} معاملة مشتركة بين الإدارات. " +
                        $"بلغ متوسط مدة الإنجاز للمعاملات المغلقة {metrics.AverageCompletionDays:N1} يومًا، " +
                        $"ونسبة الإنجاز ضمن المهلة {metrics.OnTimeCompletionRate:N1}%." +
                        (topOverdueDept != null ? $" أعلى تركز للتأخير في إدارة «{topOverdueDept}»." : string.Empty);

        return new ExecutiveSummaryDto { KpiCards = cards, ExecutiveNarrative = narrative };
    }

    /// <summary>
    /// For DepartmentTransactions, <paramref name="metrics"/> must already be the department-scoped
    /// result of <see cref="InstitutionalReportMetricsCalculator.CalculateForDepartmentTransactions"/>
    /// (drives the "open vs closed" bar), and non-empty <paramref name="selectedDepartmentIds"/> scopes
    /// the "top overdue departments" and "average completion by department" bars to the departments the
    /// report was actually built for — otherwise a co-assigned but non-selected department could show up
    /// in a report scoped to someone else. Every other report type passes the unchanged transaction-level
    /// metrics and an empty department list, so its chart output is exactly as before.
    /// </summary>
    private static List<ChartDto> BuildCharts(
        InstitutionalMetricsResult metrics,
        IReadOnlyList<TransactionReportSnapshot> snapshots,
        IReadOnlyList<int> selectedDepartmentIds)
    {
        var departmentObservations = DepartmentTransactionPerformanceObservationResolver.Expand(snapshots);
        if (selectedDepartmentIds.Count > 0)
        {
            var selected = selectedDepartmentIds.ToHashSet();
            departmentObservations = departmentObservations
                .Where(observation => selected.Contains(observation.State.DepartmentId))
                .ToList();
        }
        var monthly = snapshots.Where(s => s.IsPeriodIncoming)
            .GroupBy(s => new DateTime(s.IncomingDate.Year, s.IncomingDate.Month, 1, 0, 0, 0, DateTimeKind.Utc))
            .OrderBy(g => g.Key)
            .Select(g => new ChartSeriesPointDto { Label = g.Key.ToString("yyyy-MM"), Value = g.Count() })
            .ToList();

        var openClosed = new List<ChartSeriesPointDto>
        {
            new() { Label = "مفتوحة", Value = metrics.OpenCount },
            new() { Label = "مغلقة", Value = metrics.ClosedCount }
        };

        var stageGroups = snapshots.Where(s => s.IsOpen)
            .SelectMany(s => s.FollowUpStages)
            .GroupBy(s => s)
            .Select(g => new ChartSeriesPointDto
            {
                Label = InstitutionalReportMetricsCalculator.FollowUpStageLabel(g.Key),
                Value = g.Count()
            }).ToList();

        var priorityGroups = snapshots
            .GroupBy(s => s.Priority)
            .Select(g => new ChartSeriesPointDto { Label = PriorityLabel(g.Key), Value = g.Count() })
            .ToList();

        var topOverdueDepts = departmentObservations
            .Where(observation => observation.State.IsOverdueForDepartment)
            .GroupBy(observation => observation.State.DepartmentId)
            .OrderByDescending(g => g.Count())
            .Take(8)
            .Select(group => new ChartSeriesPointDto
            {
                Label = ReportDepartmentNameNormalizer.Normalize(group.First().State.DepartmentName),
                Value = group.Count()
            })
            .ToList();

        var avgByDept = departmentObservations
            .GroupBy(observation => observation.State.DepartmentId)
            .Select(group => new
            {
                group.Key,
                DepartmentName = ReportDepartmentNameNormalizer.Normalize(group.First().State.DepartmentName),
                CompletionDays = DepartmentTransactionPerformanceObservationResolver.CompletionDays(group)
            })
            .Where(g => g.CompletionDays.Count > 0)
            .Select(g => new ChartSeriesPointDto
            {
                Label = g.DepartmentName,
                Value = (decimal)Math.Round(g.CompletionDays.Average(), 1)
            })
            .OrderByDescending(x => x.Value)
            .Take(8)
            .ToList();

        return
        [
            new ChartDto { Key = "trend", Title = "الاتجاه العام للمعاملات خلال الفترة", ChartType = "line", Series = monthly },
            new ChartDto { Key = "openClosed", Title = "المعاملات المفتوحة مقابل المغلقة", ChartType = "bar", Series = openClosed },
            new ChartDto
            {
                Key = "stages",
                Title = "توزيع المعاملات المفتوحة حسب مرحلة المتابعة",
                ChartType = "donut",
                Series = stageGroups,
                Footnote = "قد تظهر المعاملة في أكثر من تصنيف متابعة فرعي."
            },
            new ChartDto { Key = "priority", Title = "توزيع المعاملات حسب الأولوية", ChartType = "donut", Series = priorityGroups },
            new ChartDto { Key = "topOverdueDepts", Title = "أعلى الإدارات في عدد المعاملات المتأخرة", ChartType = "bar", Series = topOverdueDepts },
            new ChartDto { Key = "avgByDept", Title = "متوسط مدة الإنجاز حسب الإدارة", ChartType = "bar", Series = avgByDept }
        ];
    }

    private List<DepartmentPerformanceRowDto> BuildDepartmentPerformance(IReadOnlyList<TransactionReportSnapshot> snapshots, DateTime today)
    {
        var staleThreshold = today.AddDays(-_ratingCriteria.CriticalStaleUpdateDaysThreshold);
        var departmentItems = snapshots
            .SelectMany(snapshot => snapshot.DepartmentPerformanceStates.Select(state => (Snapshot: snapshot, State: state)))
            .GroupBy(item => (item.State.DepartmentId, item.State.TransactionId))
            .Select(group => group.First())
            .ToList();

        return departmentItems
            .Where(item => !ReportDepartmentNameNormalizer.IsUndefined(item.State.DepartmentName))
            .GroupBy(item => item.State.DepartmentId)
            .Select(g =>
            {
                var items = g.ToList();
                var closed = items.Where(item => item.State.IsCompletedForDepartment).ToList();
                var open = items.Where(item => item.State.IsOpenForDepartment).ToList();
                var completionDays = closed
                    .Where(item => item.State.DepartmentCompletionDate.HasValue)
                    .Select(item => (item.State.DepartmentCompletionDate!.Value.Date - item.Snapshot.IncomingDate.Date).Days)
                    .Where(days => days >= 0)
                    .ToList();
                var timelinessEligible = items.Where(item => item.State.IsTimelinessEligible).ToList();
                var overdueRate = timelinessEligible.Count == 0
                    ? (double?)null
                    : Math.Round(timelinessEligible.Count(item => item.State.IsOverdueForDepartment) * 100.0 / timelinessEligible.Count, 1);
                var onTimeRate = timelinessEligible.Count == 0
                    ? 0
                    : Math.Round(timelinessEligible.Count(item => item.State.IsOnTimeForDepartment) * 100.0 / timelinessEligible.Count, 1);
                var ratingMetrics = new InstitutionalReportMetricsCalculator.DepartmentPerformanceMetrics
                {
                    OnTimeCompletionRate = onTimeRate,
                    OverdueCount = items.Count(item => item.State.IsOverdueForDepartment),
                    OldestOpenDays = open.Count == 0 ? 0 : open.Max(item => item.Snapshot.ElapsedDays),
                    PartialResponses = open.Count(item => item.State.IsPartialReplyForDepartment),
                    StaleUpdates = open.Count(item => (item.Snapshot.UpdatedAt ?? item.Snapshot.CreatedAt) < staleThreshold)
                };
                var rating = InstitutionalReportMetricsCalculator.RateDepartment(ratingMetrics, _ratingCriteria);
                return new DepartmentPerformanceRowDto
                {
                    DepartmentId = g.Key,
                    DepartmentName = ReportDepartmentNameNormalizer.Normalize(items[0].State.DepartmentName),
                    TotalTransactions = items.Count,
                    ClosedCount = closed.Count,
                    OpenCount = open.Count,
                    WaitingForStatementCount = open.Count(item => item.State.PendingReplyAssignmentCount > 0),
                    OverdueCount = items.Count(item => item.State.IsOverdueForDepartment),
                    JointDepartmentCount = items.Count(item => item.Snapshot.DepartmentPerformanceStates.Count > 1),
                    AverageCompletionDays = completionDays.Count == 0 ? 0 : Math.Round(completionDays.Average(), 1),
                    OnTimeCompletionRate = onTimeRate,
                    OverdueRate = overdueRate,
                    Rating = rating,
                    RatingLabel = InstitutionalReportMetricsCalculator.RatingLabel(rating)
                };
            })
            .OrderByDescending(r => r.TotalTransactions)
            .ToList();
    }

    private static List<RiskAlertRowDto> BuildRisks(IReadOnlyList<TransactionReportSnapshot> snapshots)
    {
        var seq = 1;
        var risks = new List<RiskAlertRowDto>();
        foreach (var s in snapshots.Where(s => s.IsOpen).OrderByDescending(s => s.ElapsedDays).Take(25))
        {
            var departmentStates = s.DepartmentPerformanceStates;
            foreach (var state in departmentStates.Where(state => state.IsOpenOverdueForDepartment))
                risks.Add(new RiskAlertRowDto
                {
                    Sequence = seq++,
                    Alert = $"معاملة متأخرة: {s.Subject}",
                    DepartmentName = state.DepartmentName,
                    Severity = RiskSeverity.High,
                    SeverityLabel = "مرتفع",
                    ElapsedDays = s.ElapsedDays,
                    SuggestedAction = "متابعة فورية وتحديد مسؤول الإنجاز"
                });
            if (s.IsPartialReply)
            {
                foreach (var state in departmentStates.Where(state => state.IsOpenForDepartment && state.PendingReplyAssignmentCount > 0))
                {
                    risks.Add(new RiskAlertRowDto
                    {
                        Sequence = seq++,
                        Alert = $"رد جزئي متوقف: {s.Subject}",
                        DepartmentName = state.DepartmentName,
                        Severity = RiskSeverity.Elevated,
                        SeverityLabel = "يحتاج متابعة",
                        ElapsedDays = s.ElapsedDays,
                        SuggestedAction = "استكمال ردود الإدارات المتبقية"
                    });
                }
            }
        }
        return risks.Take(20).Select((r, i) => { r.Sequence = i + 1; return r; }).ToList();
    }

    private static List<RecommendationRowDto> BuildRecommendations(
        IReadOnlyList<TransactionReportSnapshot> snapshots,
        DateTime today,
        ReportingAnalysisOptions analysisOptions)
    {
        var recs = new List<RecommendationRowDto>();
        var overdueDept = DepartmentTransactionPerformanceObservationResolver.Expand(snapshots)
            .Where(observation => observation.State.IsOverdueForDepartment)
            .GroupBy(observation => observation.State.DepartmentId)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        if (overdueDept != null)
        {
            var departmentName = ReportDepartmentNameNormalizer.Normalize(overdueDept.First().State.DepartmentName);
            recs.Add(new RecommendationRowDto
            {
                Observation = $"تراكم {overdueDept.Count()} معاملة متأخرة في {departmentName}",
                RequiredAction = "عقد اجتماع متابعة أسبوعي وتحديد خطة إغلاق",
                ResponsibleDepartment = departmentName,
                Priority = "عالية",
                TargetDate = today.AddDays(analysisOptions.RecommendationTargetDays).ToString(IsoDateFormat, CultureInfo.InvariantCulture),
                Source = RecommendationSource.Automated,
                SourceLabel = "مولّد آليًا"
            });
        }
        return recs;
    }

    private static RiskSummaryCountersDto BuildRiskCounters(
        IReadOnlyList<TransactionReportSnapshot> snapshots,
        DateTime today,
        ReportingAnalysisOptions analysisOptions)
    {
        var staleThreshold = today.AddDays(-analysisOptions.StaleRiskWindowDays);
        return new RiskSummaryCountersDto
        {
            DepartmentsNeedingFollowUp = DepartmentTransactionPerformanceObservationResolver.Expand(snapshots)
                .Where(observation => observation.State.IsOpenOverdueForDepartment)
                .Select(observation => observation.State.DepartmentId)
                .Distinct()
                .Count(),
            OpenJointDepartmentTransactions = snapshots.Count(s => s.IsOpen && s.IsJointDepartment),
            PartialResponses = snapshots.Count(s => s.IsOpen && s.IsPartialReply),
            TransactionsWithoutRecentUpdate = snapshots.Count(s => s.IsOpen && (s.UpdatedAt ?? s.CreatedAt) < staleThreshold),
            DataIntegrityIssues = 0
        };
    }

    /// <summary>
    /// Post-processing re-sort of the already-loaded, already-truncated detail snapshots. Never
    /// changes which rows survive truncation (that's still LoadSnapshotsAsync's DB-level order) or
    /// the metricSnapshots-driven aggregates computed earlier in BuildInternalAsync — this only
    /// reorders the final TransactionDetails table/export rows.
    /// </summary>
    private static (List<TransactionReportSnapshot> Snapshots, ReportDetailSortBy EffectiveSort) ApplyDetailSort(
        List<TransactionReportSnapshot> snapshots, ReportBuildRequestDto request)
    {
        var requested = request.DetailSortBy ?? ReportDetailSortBy.Default;

        if (requested == ReportDetailSortBy.Default)
        {
            if (request.ReportType == InstitutionalReportType.DepartmentTransactions)
            {
                var departmentSorted = snapshots
                    .OrderBy(s => ResolveDepartmentSortKey(s, request), StringComparer.Ordinal)
                    .ThenByDescending(s => s.IncomingDate)
                    .ThenByDescending(s => s.TransactionId)
                    .ToList();
                return (departmentSorted, ReportDetailSortBy.Department);
            }

            // Unchanged for every other report type: LoadSnapshotsAsync already applied
            // OrderByDescending(IncomingDate).ThenByDescending(Id) at the DB level — zero
            // regression when DetailSortBy is omitted.
            return (snapshots, ReportDetailSortBy.IncomingDateDesc);
        }

        List<TransactionReportSnapshot> sorted = requested switch
        {
            ReportDetailSortBy.IncomingDateDesc => snapshots
                .OrderByDescending(s => s.IncomingDate).ThenByDescending(s => s.TransactionId).ToList(),
            ReportDetailSortBy.Department => snapshots
                .OrderBy(s => ResolveDepartmentSortKey(s, request), StringComparer.Ordinal).ThenByDescending(s => s.IncomingDate).ToList(),
            ReportDetailSortBy.Status when request.ReportType == InstitutionalReportType.DepartmentTransactions => snapshots
                .OrderBy(s => ResolveDepartmentStatusSortKey(s, request))
                .ThenByDescending(s => s.IncomingDate)
                .ThenByDescending(s => s.TransactionId)
                .ToList(),
            ReportDetailSortBy.Status => snapshots
                .OrderBy(s => s.Status).ThenByDescending(s => s.IncomingDate).ToList(),
            ReportDetailSortBy.Priority => snapshots
                .OrderByDescending(s => s.Priority).ThenByDescending(s => s.IncomingDate).ToList(),
            ReportDetailSortBy.DueDate when request.ReportType == InstitutionalReportType.DepartmentTransactions => snapshots
                .OrderBy(s => ResolveDepartmentDueDateSortKey(s, request))
                .ThenByDescending(s => s.IncomingDate)
                .ThenByDescending(s => s.TransactionId)
                .ToList(),
            ReportDetailSortBy.DueDate => snapshots
                .OrderBy(s => s.ResponseDueDate ?? DateTime.MaxValue).ThenByDescending(s => s.IncomingDate).ToList(),
            _ => snapshots
        };
        return (sorted, requested);
    }

    /// <summary>
    /// For DepartmentTransactions, "Department" sort must reflect the report's own selected-department
    /// scope (assignment OR outgoing), not the generic ResponsibleDepartment field — which only reflects
    /// the first ASSIGNMENT department and is empty ("—") for transactions matched purely via an
    /// OutgoingDepartment relation, which would otherwise sort those rows incorrectly. For every other
    /// report type, ResponsibleDepartment (the existing, unaffected meaning) is used unchanged.
    /// </summary>
    private static string ResolveDepartmentSortKey(TransactionReportSnapshot s, ReportBuildRequestDto request)
    {
        if (request.ReportType != InstitutionalReportType.DepartmentTransactions)
            return s.ResponsibleDepartment;

        var assignmentIds = s.AssignmentDepartmentIds ?? [];
        var assignmentNames = s.AssignmentDepartmentNames ?? [];
        var outgoingIds = s.OutgoingDepartmentIds ?? [];
        var outgoingNames = s.OutgoingDepartmentNames ?? [];

        foreach (var deptId in request.Filters?.DepartmentIds ?? [])
        {
            if (assignmentIds.Contains(deptId))
                return ResolveDepartmentNameById(deptId, assignmentIds, assignmentNames, s.ResponsibleDepartment);
            if (outgoingIds.Contains(deptId))
                return ResolveDepartmentNameById(deptId, outgoingIds, outgoingNames, s.ResponsibleDepartment);
        }
        return s.ResponsibleDepartment;
    }

    /// <summary>
    /// DepartmentTransactions status order is urgency-first and considers only selected departments:
    /// open-overdue, open, completed-late, completed-on-time, completed without measurable timing,
    /// then OutgoingDepartment-only relations with no assignment performance evidence.
    /// For a multi-department row the most urgent matching state determines its position.
    /// </summary>
    private static int ResolveDepartmentStatusSortKey(TransactionReportSnapshot snapshot, ReportBuildRequestDto request)
    {
        var states = ResolveSelectedDepartmentStates(snapshot, request.Filters?.DepartmentIds ?? []);
        return states.Count == 0 ? 5 : states.Min(DepartmentStatusSortKey);
    }

    private static int DepartmentStatusSortKey(DepartmentTransactionPerformanceState state)
    {
        if (state.IsOpenOverdueForDepartment)
            return 0;
        if (state.IsOpenForDepartment)
            return 1;
        if (state.IsCompletedLateForDepartment)
            return 2;
        if (state.IsOnTimeForDepartment)
            return 3;
        return 4;
    }

    /// <summary>
    /// A multi-department row sorts by the earliest measurable due date among matching selected
    /// assignment states. OutgoingDepartment-only matches have no invented deadline and sort last.
    /// </summary>
    private static DateTime ResolveDepartmentDueDateSortKey(
        TransactionReportSnapshot snapshot,
        ReportBuildRequestDto request) =>
        ResolveSelectedDepartmentStates(snapshot, request.Filters?.DepartmentIds ?? [])
            .Where(state => state.DepartmentDueDate.HasValue)
            .Select(state => state.DepartmentDueDate!.Value.Date)
            .DefaultIfEmpty(DateTime.MaxValue)
            .Min();

    private static List<DepartmentTransactionPerformanceState> ResolveSelectedDepartmentStates(
        TransactionReportSnapshot snapshot,
        IReadOnlyList<int> selectedDepartmentIds)
    {
        var selected = selectedDepartmentIds.ToHashSet();
        var selectedOrder = selectedDepartmentIds.ToList();
        return snapshot.DepartmentPerformanceStates
            .Where(state => selected.Contains(state.DepartmentId))
            .OrderBy(state => selectedOrder.IndexOf(state.DepartmentId))
            .ThenBy(state => state.DepartmentId)
            .ToList();
    }

    /// <summary>
    /// Looks up the name at the same index as <paramref name="departmentId"/> in <paramref name="ids"/>,
    /// without assuming <paramref name="ids"/> and <paramref name="names"/> are the same length —
    /// returns <paramref name="fallback"/> instead of throwing if the id is missing or the lists are
    /// mismatched.
    /// </summary>
    internal static string ResolveDepartmentNameById(
        int departmentId, IReadOnlyList<int> ids, IReadOnlyList<string> names, string fallback)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] == departmentId && i < names.Count)
                return names[i];
        }
        return fallback;
    }

    private static (List<TransactionDetailRowDto> Rows, bool GroupedByDepartment) BuildTransactionDetails(
        IReadOnlyList<TransactionReportSnapshot> snapshots, ReportBuildRequestDto request)
    {
        var isDepartmentTransactions = request.ReportType == InstitutionalReportType.DepartmentTransactions;
        if (!isDepartmentTransactions)
        {
            var plainRows = snapshots.Select((s, index) => BuildDetailRow(s, index + 1, [])).ToList();
            return (plainRows, false);
        }

        var selectedDepartmentIds = request.Filters?.DepartmentIds ?? [];
        var groupByDepartment = request.GroupDetailsByDepartment == true && selectedDepartmentIds.Count > 1;

        if (!groupByDepartment)
        {
            var rows = snapshots.Select((s, index) =>
                BuildDetailRow(
                    s,
                    index + 1,
                    ComputeMatchedDepartments(s, selectedDepartmentIds),
                    includeAuditDepartmentLists: true)).ToList();
            return (rows, false);
        }

        // Grouped: expand each snapshot into one row per matched selected department, ordered
        // department-first. A shared transaction may appear under more than one department here —
        // intentional and documented via InstitutionalReportModel.DetailRowsAreAdditive = false.
        var expanded = new List<(TransactionReportSnapshot Snapshot, TransactionDetailDepartmentRelationDto Match)>();
        foreach (var s in snapshots)
        {
            foreach (var match in ComputeMatchedDepartments(s, selectedDepartmentIds))
                expanded.Add((s, match));
        }

        var orderedExpanded = expanded
            .OrderBy(e => e.Match.DepartmentName, StringComparer.Ordinal)
            .ThenByDescending(e => e.Snapshot.IncomingDate)
            .ThenByDescending(e => e.Snapshot.TransactionId)
            .ToList();

        var groupedRows = orderedExpanded.Select((e, index) =>
        {
            var row = BuildDetailRow(
                e.Snapshot,
                index + 1,
                [e.Match],
                includeAuditDepartmentLists: true,
                focusDepartmentId: e.Match.DepartmentId);
            row.DepartmentGroupDepartmentId = e.Match.DepartmentId;
            row.DepartmentGroupDepartmentName = e.Match.DepartmentName;
            return row;
        }).ToList();

        return (groupedRows, true);
    }

    private static TransactionDetailRowDto BuildDetailRow(
        TransactionReportSnapshot s,
        int sequence,
        List<TransactionDetailDepartmentRelationDto> matchedDepartments,
        bool includeAuditDepartmentLists = false,
        int? focusDepartmentId = null)
    {
        var performanceMatches = focusDepartmentId.HasValue
            ? matchedDepartments.Where(match => match.DepartmentId == focusDepartmentId.Value).ToList()
            : matchedDepartments;
        var measurableMatches = performanceMatches.Where(match => match.HasPerformanceState).ToList();
        var departmentStatus = performanceMatches.Count == 0
            ? string.Empty
            : string.Join("؛ ", performanceMatches.Select(match =>
                focusDepartmentId.HasValue || performanceMatches.Count == 1
                    ? match.DepartmentStatus
                    : $"{match.DepartmentName}: {match.DepartmentStatus}"));
        var departmentResponseState = performanceMatches.Count == 0
            ? string.Empty
            : string.Join("؛ ", performanceMatches.Select(match =>
                focusDepartmentId.HasValue || performanceMatches.Count == 1
                    ? DepartmentResponseStateLabel(match)
                    : $"{match.DepartmentName}: {DepartmentResponseStateLabel(match)}"));
        var departmentDueDate = measurableMatches
            .Select(match => ParseIsoDate(match.DepartmentDueDate))
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .DefaultIfEmpty()
            .Min();
        var departmentCompletionDate = measurableMatches
            .Select(match => ParseIsoDate(match.DepartmentCompletionDate))
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .DefaultIfEmpty()
            .Max();

        return new TransactionDetailRowDto
        {
            Sequence = sequence,
            TransactionId = s.TransactionId,
            TrackingNumber = s.TrackingNumber,
            IncomingNumber = s.IncomingNumber,
            IncomingDate = s.IncomingDate,
            Subject = s.Subject,
            IncomingParty = s.IncomingParty,
            ResponsibleDepartment = s.ResponsibleDepartment,
            JointDepartments = string.Join("، ", (s.AssignmentDepartmentNames ?? []).Concat(s.OutgoingDepartmentNames ?? []).Distinct()),
            Priority = PriorityLabel(s.Priority),
            Status = matchedDepartments.Count > 0 ? TransactionReportingStateLabel(s) : StatusLabel(s.Status),
            DepartmentStatus = departmentStatus,
            FollowUpStage = string.Join("، ", s.FollowUpStages.Select(InstitutionalReportMetricsCalculator.FollowUpStageLabel)),
            ElapsedDays = s.ElapsedDays,
            DueDate = s.ResponseDueDate?.ToString(IsoDateFormat, CultureInfo.InvariantCulture),
            DepartmentDueDate = departmentDueDate == default
                ? null
                : departmentDueDate.ToString(IsoDateFormat, CultureInfo.InvariantCulture),
            DepartmentCompletionDate = departmentCompletionDate == default
                ? null
                : departmentCompletionDate.ToString(IsoDateFormat, CultureInfo.InvariantCulture),
            LastActionDate = (s.UpdatedAt ?? s.LastFollowUpDate)?.ToString(IsoDateFormat, CultureInfo.InvariantCulture),
            ResponseState = ResolveResponseState(s),
            DepartmentResponseState = departmentResponseState,
            OutgoingNumber = s.OutgoingNumber,
            OutgoingDate = s.OutgoingDate?.ToString(IsoDateFormat, CultureInfo.InvariantCulture),
            MatchedDepartments = matchedDepartments,
            AllAssignmentDepartments = includeAuditDepartmentLists ? (s.AssignmentDepartmentNames ?? []).ToList() : [],
            AllOutgoingDepartments = includeAuditDepartmentLists ? (s.OutgoingDepartmentNames ?? []).ToList() : [],
        };
    }

    private static DateTime? ParseIsoDate(string? value) =>
        DateTime.TryParseExact(value, IsoDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// DepartmentTransactions only: for each selected department id, checks whether this transaction
    /// matches it via Assignment ("إحالة"), OutgoingDepartment ("صادر لها"), or both — using the
    /// already-loaded, already-deduped AssignmentDepartmentIds/OutgoingDepartmentIds on the snapshot
    /// (no new query/join). Mirrors PR #108's dedup-by-department-id idea without importing its code.
    /// </summary>
    private static List<TransactionDetailDepartmentRelationDto> ComputeMatchedDepartments(
        TransactionReportSnapshot s, IReadOnlyList<int> selectedDepartmentIds)
    {
        var assignmentIds = s.AssignmentDepartmentIds ?? [];
        var assignmentNames = s.AssignmentDepartmentNames ?? [];
        var outgoingIds = s.OutgoingDepartmentIds ?? [];
        var outgoingNames = s.OutgoingDepartmentNames ?? [];

        var result = new List<TransactionDetailDepartmentRelationDto>();
        foreach (var deptId in selectedDepartmentIds.Distinct())
        {
            var viaAssignment = assignmentIds.Contains(deptId);
            var viaOutgoing = outgoingIds.Contains(deptId);
            if (!viaAssignment && !viaOutgoing)
                continue;

            var name = viaAssignment
                ? ResolveDepartmentNameById(deptId, assignmentIds, assignmentNames, fallback: "—")
                : ResolveDepartmentNameById(deptId, outgoingIds, outgoingNames, fallback: "—");

            var performanceState = s.DepartmentPerformanceStates.SingleOrDefault(state => state.DepartmentId == deptId);
            result.Add(new TransactionDetailDepartmentRelationDto
            {
                DepartmentId = deptId,
                DepartmentName = name,
                Relation = DepartmentRelationLabel(viaAssignment, viaOutgoing),
                HasPerformanceState = performanceState != null,
                DepartmentStatus = performanceState == null
                    ? "غير قابلة للقياس — صادر لها دون إحالة"
                    : DepartmentTransactionPerformanceObservationResolver.StatusLabel(performanceState),
                DepartmentDueDate = performanceState?.DepartmentDueDate?.ToString(IsoDateFormat, CultureInfo.InvariantCulture),
                DepartmentCompletionDate = performanceState?.DepartmentCompletionDate?.ToString(IsoDateFormat, CultureInfo.InvariantCulture),
                IsOpenForDepartment = performanceState?.IsOpenForDepartment,
                IsCompletedForDepartment = performanceState?.IsCompletedForDepartment,
                IsOverdueForDepartment = performanceState?.IsOverdueForDepartment,
                IsCompletedLateForDepartment = performanceState?.IsCompletedLateForDepartment,
                IsOnTimeForDepartment = performanceState?.IsOnTimeForDepartment,
            });
        }
        return result;
    }

    private static string DepartmentRelationLabel(bool viaAssignment, bool viaOutgoing)
    {
        if (viaAssignment && viaOutgoing)
            return "إحالة وصادر لها";

        if (viaAssignment)
            return "إحالة";

        return "صادر لها";
    }

    private static string DepartmentResponseStateLabel(TransactionDetailDepartmentRelationDto match)
    {
        if (!match.HasPerformanceState)
            return "غير متاح";
        return match.IsCompletedForDepartment == true ? "مكتمل" : "بانتظار";
    }

    private static string ResolveResponseState(TransactionReportSnapshot snapshot)
    {
        if (snapshot.IsCompletedLate)
            return "مكتمل متأخر";

        if (snapshot.ResponseCompleted)
            return "مكتمل";

        if (snapshot.RequiresResponse)
            return "بانتظار";

        return "—";
    }

    private static string TransactionReportingStateLabel(TransactionReportSnapshot snapshot)
    {
        if (snapshot.IsOpen)
            return "مفتوحة";
        if (snapshot.IsClosed)
            return "مغلقة";
        return StatusLabel(snapshot.Status);
    }

    private List<IntegrityWarningDto> ValidateIntegrity(
        InstitutionalMetricsResult metrics,
        IReadOnlyList<DepartmentPerformanceRowDto> departmentPerformance)
    {
        var warnings = new List<IntegrityWarningDto>();
        var bucketTotal = metrics.OpenCount + metrics.ClosedCount + metrics.CancelledCount + metrics.ArchivedCount;
        if (bucketTotal != metrics.TotalTransactions)
        {
            warnings.Add(new IntegrityWarningDto
            {
                Code = "TOTAL_MISMATCH",
                Message = "مجموع المفتوحة والمغلقة والملغاة والمؤرشفة لا يساوي إجمالي المعاملات الفريدة.",
                Severity = "critical"
            });
        }
        var departmentTotal = departmentPerformance.Sum(row => row.TotalTransactions);
        var unattributedSnapshots = metrics.Snapshots
            .Where(snapshot => !snapshot.DepartmentPerformanceStates.Any(state =>
                !ReportDepartmentNameNormalizer.IsUndefined(state.DepartmentName)))
            .ToList();
        if (unattributedSnapshots.Count > 0)
        {
            _logger.LogWarning(
                "Institutional report department attribution mismatch. TotalTransactions={TotalTransactions} DepartmentTotal={DepartmentTotal} UnattributedCount={UnattributedCount}",
                metrics.TotalTransactions,
                departmentTotal,
                unattributedSnapshots.Count);

            warnings.Add(new IntegrityWarningDto
            {
                Code = "DEPARTMENT_ATTRIBUTION_MISMATCH",
                Message = $"إجمالي نطاق التقرير ({metrics.TotalTransactions:N0}) لا يساوي إجمالي المعاملات المنسوبة للإدارات ({departmentTotal:N0}). عدد المعاملات غير المنسوبة: {unattributedSnapshots.Count:N0}.",
                Severity = "warning"
            });
        }
        if (metrics.OpenOverdueCount > metrics.OpenCount)
        {
            warnings.Add(new IntegrityWarningDto
            {
                Code = "OVERDUE_OUTSIDE_OPEN",
                Message = "عدد المتأخرة المفتوحة يتجاوز عدد المفتوحة.",
                Severity = "critical"
            });
        }
        return warnings;
    }

    private static DateTime ResolvePeriodStart<T>(
        DateTime? requested,
        IReadOnlyList<T> snapshotsDescendingByIncomingDate,
        Func<T, DateTime> selector,
        DateTime fallback)
    {
        if (requested.HasValue)
            return requested.Value;

        return snapshotsDescendingByIncomingDate.Count == 0
            ? fallback
            : selector(snapshotsDescendingByIncomingDate[^1]);
    }

    private static DateTime ResolvePeriodEnd<T>(
        DateTime? requested,
        IReadOnlyList<T> snapshotsDescendingByIncomingDate,
        Func<T, DateTime> selector,
        DateTime fallback)
    {
        if (requested.HasValue)
            return requested.Value;

        return snapshotsDescendingByIncomingDate.Count == 0
            ? fallback
            : selector(snapshotsDescendingByIncomingDate[0]);
    }

    private static string ReportTypeLabel(InstitutionalReportType type) => type switch
    {
        InstitutionalReportType.OverdueTransactions => "تقرير المعاملات المتأخرة",
        InstitutionalReportType.JointDepartmentTransactions => "تقرير معاملات الإدارات المشتركة",
        InstitutionalReportType.PartialResponses => "تقرير الإفادات والردود الجزئية",
        InstitutionalReportType.SingleTransaction => "تقرير معاملة واحدة",
        InstitutionalReportType.DepartmentTransactions => "تقرير معاملات إدارة",
        _ => "التقرير التنفيذي الشامل"
    };

    private static string PriorityLabel(Priority priority) => priority switch
    {
        Priority.Urgent => "عاجل",
        Priority.VeryUrgent => "عاجل جدًا",
        _ => "عادي"
    };

    private static string StatusLabel(TransactionStatus status) => status switch
    {
        TransactionStatus.Closed => "مغلقة",
        TransactionStatus.WaitingForReply => "بانتظار رد",
        TransactionStatus.PartiallyReplied => "رد جزئي",
        TransactionStatus.Overdue => "متأخرة",
        TransactionStatus.ReadyForResponse => "جاهزة للإفادة",
        _ => "مفتوحة"
    };

    private static ReportTemplateDto MapTemplate(ReportExportTemplate entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        ReportType = entity.ReportType,
        SectionIds = System.Text.Json.JsonSerializer.Deserialize<List<ReportSectionId>>(entity.SectionIdsJson) ?? [],
        DefaultFilters = System.Text.Json.JsonSerializer.Deserialize<ReportFiltersDto>(entity.DefaultFiltersJson) ?? new(),
        DefaultFormat = entity.DefaultFormat,
        PageNumberingMode = entity.PageNumberingMode,
        IncludePartialCover = entity.IncludePartialCover,
        IncludePartialManifest = entity.IncludePartialManifest,
        DetailSortBy = entity.DetailSortBy,
        GroupDetailsByDepartment = entity.GroupDetailsByDepartment
    };
}
