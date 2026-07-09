using System.Diagnostics;
using System.Globalization;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Rendering;

namespace Uqeb.Api.Reporting.Services;

internal static class InstitutionalReportAnalysisService
{
    private const string NotAvailable = "غير متاح من البيانات الحالية";
    private const string TransactionUnit = "معاملة";

    private static class MetricValueTypes
    {
        public const string Number = "number";
        public const string Percent = "percent";
        public const string Decimal = "decimal";
    }

    internal sealed record InstitutionalReportAnalysisInput
    {
        public required ReportBuildRequestDto Request { get; init; }
        public required ReportMetadataDto Metadata { get; init; }
        public required ReportFiltersDto Filters { get; init; }
        public required InstitutionalMetricsResult CurrentMetrics { get; init; }
        public required IReadOnlyList<TransactionReportSnapshot> CurrentSnapshots { get; init; }
        public InstitutionalMetricsResult? PreviousMetrics { get; init; }
        public required IReadOnlyList<TransactionReportSnapshot> PreviousSnapshots { get; init; }
        public required ReportingAnalysisOptions Options { get; init; }
        public required int DetailLimit { get; init; }
        public required bool DetailRowsTruncated { get; init; }
    }

    private sealed record AnalysisMetricDefinition
    {
        public required string Key { get; init; }
        public required string Title { get; init; }
        public required string Definition { get; init; }
        public required string Formula { get; init; }
        public required string FieldsUsed { get; init; }
        public required string Unit { get; init; }
        public required string Format { get; init; }
        public required int MinimumSampleSize { get; init; }
        public required KpiDirection Direction { get; init; }
    }

    internal static InstitutionalReportAnalysisResult Build(
        InstitutionalReportAnalysisInput input,
        IReportingAnalysisInstrumentation? instrumentation = null)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var request = input.Request;
        var metadata = input.Metadata;
        var filters = input.Filters;
        var currentMetrics = input.CurrentMetrics;
        var currentSnapshots = input.CurrentSnapshots;
        var previousMetrics = input.PreviousMetrics;
        var previousSnapshots = input.PreviousSnapshots;
        var options = input.Options;
        var contentLevel = request.ContentLevel ?? ReportContentLevel.Analytical;
        var comparisonRequest = CreateComparisonRequest(request);
        var comparisonMode = comparisonRequest is null
            ? ReportComparisonMode.None
            : request.ComparisonMode ?? ReportComparisonMode.PreviousEquivalentPeriod;
        var timeGrouping = request.TimeGrouping ?? ReportTimeGrouping.Monthly;
        var maxFindings = ResolvePositive(request.MaxFindings, options.MaxExecutiveFindings);
        var maxCriticalCases = ResolvePositive(request.MaxCriticalCases, options.MaxExecutiveCriticalCases);
        var maxRecommendations = ResolvePositive(request.MaxRecommendations, options.MaxRecommendations);
        var referenceDate = ReportingTemporalCalculator.ResolveReferenceDate(metadata);
        var reportType = request.ReportType.ToString();
        var snapshotCount = currentSnapshots.Count;
        var succeeded = false;

        try
        {
        var kpis = MeasureStage(
            instrumentation,
            "kpis",
            reportType,
            snapshotCount,
            () => BuildKpis(currentMetrics, currentSnapshots, previousMetrics, options, referenceDate));
        var criticalCases = request.IncludeCriticalCases == false
            ? []
            : MeasureStage(
                instrumentation,
                "critical_cases",
                reportType,
                snapshotCount,
                () => BuildCriticalCases(currentSnapshots, options, referenceDate).Take(maxCriticalCases).ToList());
        var departments = request.IncludeDepartmentPerformance == false
            ? []
            : MeasureStage(
                instrumentation,
                "departments",
                reportType,
                snapshotCount,
                () => BuildDepartments(currentSnapshots, previousSnapshots, options));
        var externalParties = request.IncludeExternalPartyAnalysis == false
            ? []
            : MeasureStage(
                instrumentation,
                "external_parties",
                reportType,
                snapshotCount,
                () => BuildExternalParties(currentSnapshots));
        var categories = request.IncludeCategoryAnalysis == false
            ? []
            : MeasureStage(
                instrumentation,
                "categories",
                reportType,
                snapshotCount,
                () => BuildCategories(currentSnapshots));
        var priorities = request.IncludeCategoryAnalysis == false
            ? []
            : MeasureStage(
                instrumentation,
                "priorities",
                reportType,
                snapshotCount,
                () => BuildPriorities(currentSnapshots));
        var bottlenecks = request.IncludeBottleneckAnalysis == false
            ? []
            : MeasureStage(
                instrumentation,
                "bottlenecks",
                reportType,
                snapshotCount,
                () => BuildBottlenecks(currentSnapshots, referenceDate, options.StaleTransactionDays));
        var dataQuality = request.IncludeDataQuality == false
            ? []
            : MeasureStage(
                instrumentation,
                "data_quality",
                reportType,
                snapshotCount,
                () => BuildDataQualityIssues(currentSnapshots));
        var includeDepartmentRecognitions =
            request.SectionIds.Contains(ReportSectionId.OutstandingAndImprovedDepartments)
            || request.IncludeDepartmentPerformance != false;
        var departmentRecognitions = !includeDepartmentRecognitions
            ? []
            : MeasureStage(
                instrumentation,
                "department_recognitions",
                reportType,
                snapshotCount,
                () => BuildDepartmentRecognitions(currentSnapshots, previousSnapshots, options));
        var completenessRate = MeasureStage(
            instrumentation,
            "completeness_rate",
            reportType,
            snapshotCount,
            () => CalculateCompletenessRate(currentSnapshots));
        var findings = MeasureStage(
            instrumentation,
            "findings",
            reportType,
            snapshotCount,
            () => BuildFindings(currentMetrics, previousMetrics, departments, externalParties, dataQuality, options)
                .Take(maxFindings)
                .ToList());
        var recommendations = request.IncludeRecommendations == false
            ? []
            : MeasureStage(
                instrumentation,
                "recommendations",
                reportType,
                snapshotCount,
                () => BuildRecommendations(findings, criticalCases, dataQuality).Take(maxRecommendations).ToList());
        var timeSeries = request.IncludeTimeTrends == false
            ? []
            : MeasureStage(
                instrumentation,
                "time_series",
                reportType,
                snapshotCount,
                () => BuildTimeSeries(currentSnapshots, timeGrouping));
        var departmentTimeSeries = request.IncludeTimeTrends == false || request.IncludeDepartmentPerformance == false
            ? []
            : MeasureStage(
                instrumentation,
                "department_time_series",
                reportType,
                snapshotCount,
                () => BuildDepartmentTimeSeries(currentSnapshots, timeGrouping));
        var insights = request.IncludeExecutiveSummary == false
            ? []
            : MeasureStage(
                instrumentation,
                "executive_insights",
                reportType,
                snapshotCount,
                () => BuildExecutiveInsights(currentMetrics, findings, criticalCases, departments, externalParties, options)
                    .Take(options.MaxExecutiveFindings)
                    .ToList());

        var result = new InstitutionalReportAnalysisResult
        {
            ContentLevel = contentLevel,
            ComparisonMode = comparisonMode,
            ComparisonFrom = comparisonRequest?.Filters.DateFrom ?? ResolveComparisonFrom(request),
            ComparisonTo = comparisonRequest?.Filters.DateTo ?? ResolveComparisonTo(request),
            Kpis = kpis,
            ExecutiveInsights = insights,
            Findings = findings,
            CriticalCases = criticalCases,
            TimeSeries = timeSeries,
            DepartmentTimeSeries = departmentTimeSeries,
            DepartmentPerformance = departments,
            DepartmentRecognitions = departmentRecognitions,
            ExternalParties = externalParties,
            Categories = categories,
            Priorities = priorities,
            Bottlenecks = bottlenecks,
            DataQualityIssues = dataQuality,
            DataCompletenessRate = completenessRate,
            Recommendations = recommendations,
            Methodology = MeasureStage(
                instrumentation,
                "methodology",
                reportType,
                snapshotCount,
                () => BuildMethodology(
                    metadata,
                    filters,
                    request,
                    comparisonRequest,
                    previousSnapshots,
                    input.DetailLimit,
                    input.DetailRowsTruncated,
                    comparisonMode))
        };

        succeeded = true;
        return result;
        }
        finally
        {
            totalStopwatch.Stop();
            instrumentation?.RecordTotal(
                totalStopwatch.Elapsed.TotalMilliseconds,
                reportType,
                snapshotCount,
                succeeded);
        }
    }

    private static T MeasureStage<T>(
        IReportingAnalysisInstrumentation? instrumentation,
        string stage,
        string reportType,
        int snapshotCount,
        Func<T> work)
    {
        if (instrumentation is null)
            return work();

        var stopwatch = Stopwatch.StartNew();
        var succeeded = false;
        try
        {
            var result = work();
            succeeded = true;
            return result;
        }
        finally
        {
            stopwatch.Stop();
            instrumentation.RecordStage(
                stage,
                stopwatch.Elapsed.TotalMilliseconds,
                reportType,
                snapshotCount,
                succeeded);
        }
    }

    internal static ReportBuildRequestDto? CreateComparisonRequest(ReportBuildRequestDto request) =>
        CreateComparisonRequest(request, out _);

    /// <summary>
    /// <paramref name="unavailableReason"/> is non-null only when comparison was actually requested
    /// (IncludeComparison=true, mode != None) but the current period was incomplete — never set when
    /// comparison simply wasn't requested (no message needed in that case).
    /// </summary>
    internal static ReportBuildRequestDto? CreateComparisonRequest(ReportBuildRequestDto request, out string? unavailableReason)
    {
        unavailableReason = null;
        const string incompletePeriodReason = "المقارنة غير متاحة لعدم تحديد فترة حالية مكتملة.";

        if (request.IncludeComparison == false)
            return null;

        var mode = request.ComparisonMode ?? ReportComparisonMode.PreviousEquivalentPeriod;
        if (mode == ReportComparisonMode.None)
            return null;

        DateTime? from;
        DateTime? to;
        if (mode == ReportComparisonMode.Custom)
        {
            from = request.ComparisonDateFrom;
            to = request.ComparisonDateTo;
        }
        else if (request.Filters.DateFrom.HasValue && request.Filters.DateTo.HasValue)
        {
            var currentFrom = request.Filters.DateFrom.Value.Date;
            var currentTo = request.Filters.DateTo.Value.Date;
            if (mode == ReportComparisonMode.YearOverYear)
            {
                from = currentFrom.AddYears(-1);
                to = currentTo.AddYears(-1);
            }
            else
            {
                var days = (currentTo - currentFrom).Days + 1;
                to = currentFrom.AddDays(-1);
                from = to.Value.AddDays(-(days - 1));
            }
        }
        else
        {
            unavailableReason = incompletePeriodReason;
            return null;
        }

        if (!from.HasValue || !to.HasValue || from.Value > to.Value)
        {
            unavailableReason = incompletePeriodReason;
            return null;
        }

        return new ReportBuildRequestDto
        {
            ReportType = request.ReportType,
            Title = request.Title,
            Introduction = request.Introduction,
            SectionIds = request.SectionIds.ToList(),
            SingleTransactionId = request.SingleTransactionId,
            ContentLevel = request.ContentLevel,
            ComparisonMode = ReportComparisonMode.None,
            IncludeComparison = false,
            TimeGrouping = request.TimeGrouping,
            IncludeExecutiveSummary = request.IncludeExecutiveSummary,
            IncludeCriticalCases = request.IncludeCriticalCases,
            IncludeTimeTrends = request.IncludeTimeTrends,
            IncludeDepartmentPerformance = request.IncludeDepartmentPerformance,
            IncludeExternalPartyAnalysis = request.IncludeExternalPartyAnalysis,
            IncludeCategoryAnalysis = request.IncludeCategoryAnalysis,
            IncludeBottleneckAnalysis = request.IncludeBottleneckAnalysis,
            IncludeDataQuality = request.IncludeDataQuality,
            IncludeRecommendations = request.IncludeRecommendations,
            IncludeMethodology = request.IncludeMethodology,
            MaxCriticalCases = request.MaxCriticalCases,
            MaxFindings = request.MaxFindings,
            MaxRecommendations = request.MaxRecommendations,
            DetailSortBy = request.DetailSortBy,
            GroupDetailsByDepartment = request.GroupDetailsByDepartment,
            Filters = new ReportFiltersDto
            {
                DateFrom = from,
                DateTo = to,
                DepartmentIds = request.Filters.DepartmentIds.ToList(),
                PartyIds = request.Filters.PartyIds.ToList(),
                CategoryIds = request.Filters.CategoryIds.ToList(),
                Priorities = request.Filters.Priorities.ToList(),
                Statuses = request.Filters.Statuses.ToList(),
                IncludeOverdue = request.Filters.IncludeOverdue,
                Search = request.Filters.Search
            }
        };
    }

    private static List<AnalyticalKpiDto> BuildKpis(
        InstitutionalMetricsResult current,
        IReadOnlyList<TransactionReportSnapshot> currentSnapshots,
        InstitutionalMetricsResult? previous,
        ReportingAnalysisOptions options,
        DateTime referenceDate)
    {
        var open = currentSnapshots.Where(s => s.IsOpen).ToList();
        var closedDurations = CompletionDays(currentSnapshots).ToList();
        var responseDurations = ResponseDays(currentSnapshots).ToList();
        var stale = open.Count(s => ReportingTemporalCalculator.IsStale(s, referenceDate, options.StaleTransactionDays));
        var oldestOpen = open.Count == 0 ? 0 : open.Max(s => s.ElapsedDays);
        var pendingAssignments = currentSnapshots.Sum(s => s.PendingReplyAssignmentCount);
        var completionMedian = InstitutionalReportStatistics.Median(closedDurations);
        var followUpAverage = current.TotalTransactions == 0
            ? 0
            : Math.Round(currentSnapshots.Count(s => s.LastFollowUpDate.HasValue) / (double)current.TotalTransactions, 2);
        var completeness = CalculateCompletenessRate(currentSnapshots);
        var previousOpen = previous?.OpenCount;
        var previousTotal = previous?.TotalTransactions;
        var closureToIncomingRatio = current.TotalTransactions == 0
            ? 0
            : Math.Round(current.ClosedCount * 100.0 / current.TotalTransactions, 1);

        // BacklogGrowthRate requires a comparison period to be meaningful.
        // When previousOpen is null there is no baseline; returning current.OpenCount would be
        // misleading (it would look like a delta but is actually an absolute count).
        var hasComparisonPeriod = previousOpen.HasValue;
        var backlogGrowth = hasComparisonPeriod ? (decimal)(current.OpenCount - previousOpen!.Value) : 0m;

        // AverageResponseDays uses ClosedAt as a proxy for ResponseCompletedAt (the snapshot model
        // does not carry a discrete ResponseCompletedAt timestamp). Only transactions where
        // ResponseCompleted == true are included. When no such transactions exist in the period,
        // the metric is genuinely unavailable — returning 0 would be misleading.
        var hasResponseData = responseDurations.Count > 0;
        var responseAverage = hasResponseData ? Math.Round(responseDurations.Average(), 1) : 0.0;

        return
        [
            Kpi(new AnalysisMetricDefinition { Key = "TotalTransactions", Title = "إجمالي المعاملات", Definition = "عدد المعاملات الفريدة المطابقة للفلاتر.", Formula = "count(distinct TransactionId)", FieldsUsed = "Transaction.Id", Unit = TransactionUnit, Format = MetricValueTypes.Number, MinimumSampleSize = 1, Direction = KpiDirection.Neutral }, current.TotalTransactions, current.TotalTransactions, previous?.TotalTransactions, options),
            Kpi(new AnalysisMetricDefinition { Key = "IncomingTransactions", Title = "المعاملات الواردة", Definition = "عدد المعاملات حسب تاريخ الوارد في الفترة.", Formula = "count by IncomingDate", FieldsUsed = "IncomingDate", Unit = TransactionUnit, Format = MetricValueTypes.Number, MinimumSampleSize = 1, Direction = KpiDirection.Neutral }, current.TotalTransactions, current.TotalTransactions, previousTotal, options),
            Kpi(new AnalysisMetricDefinition { Key = "ClosedTransactions", Title = "المعاملات المغلقة", Definition = "عدد المعاملات ذات الحالة المغلقة.", Formula = "count(IsClosed)", FieldsUsed = "Status, ClosedAt", Unit = TransactionUnit, Format = MetricValueTypes.Number, MinimumSampleSize = 1, Direction = KpiDirection.HigherIsBetter }, current.ClosedCount, current.ClosedCount, previous?.ClosedCount, options),
            Kpi(new AnalysisMetricDefinition { Key = "OpenTransactions", Title = "المعاملات المفتوحة", Definition = "عدد المعاملات التشغيلية غير المغلقة.", Formula = "count(IsOpen)", FieldsUsed = "Status", Unit = TransactionUnit, Format = MetricValueTypes.Number, MinimumSampleSize = 1, Direction = KpiDirection.LowerIsBetter }, current.OpenCount, current.OpenCount, previous?.OpenCount, options),
            Kpi(new AnalysisMetricDefinition { Key = "OnTimeCompletionRate", Title = "نسبة الإنجاز ضمن المهلة", Definition = "نسبة المعاملات المغلقة قبل أو في تاريخ المهلة.", Formula = "on-time closed / measurable closed", FieldsUsed = "ClosedAt, ResponseDueDate", Unit = "%", Format = MetricValueTypes.Percent, MinimumSampleSize = options.MinimumComparisonSampleSize, Direction = KpiDirection.HigherIsBetter }, (decimal)current.OnTimeCompletionRate, current.ClosedCount, previous is null ? null : (decimal)previous.OnTimeCompletionRate, options),
            Kpi(new AnalysisMetricDefinition { Key = "OverdueRate", Title = "نسبة التأخر", Definition = "نسبة إجمالي المعاملات المتأخرة، وتشمل المفتوحة المتأخرة والمنجزة/المغلقة بعد تاريخ الاستحقاق.", Formula = "(open overdue + completed late) / total", FieldsUsed = "ResponseDueDate, AssignmentDueDate, ResponseCompletedDate, ClosedAt, Status", Unit = "%", Format = MetricValueTypes.Percent, MinimumSampleSize = options.MinimumComparisonSampleSize, Direction = KpiDirection.LowerIsBetter }, Rate(current.OverdueCount, current.TotalTransactions), current.TotalTransactions, previous is null ? null : Rate(previous.OverdueCount, previous.TotalTransactions), options),
            Kpi(new AnalysisMetricDefinition { Key = "AverageCompletionDays", Title = "متوسط مدة الإنجاز", Definition = "متوسط الأيام بين الوارد والإغلاق للمعاملات المغلقة.", Formula = "avg(ClosedAt - IncomingDate)", FieldsUsed = "IncomingDate, ClosedAt", Unit = "يوم", Format = MetricValueTypes.Decimal, MinimumSampleSize = options.MinimumComparisonSampleSize, Direction = KpiDirection.LowerIsBetter }, (decimal)current.AverageCompletionDays, closedDurations.Count, previous is null ? null : (decimal)previous.AverageCompletionDays, options),
            Kpi(new AnalysisMetricDefinition { Key = "MedianCompletionDays", Title = "وسيط مدة الإنجاز", Definition = "وسيط أيام الإنجاز لتقليل أثر القيم الشاذة.", Formula = "median(ClosedAt - IncomingDate)", FieldsUsed = "IncomingDate, ClosedAt", Unit = "يوم", Format = MetricValueTypes.Decimal, MinimumSampleSize = options.MinimumComparisonSampleSize, Direction = KpiDirection.LowerIsBetter }, (decimal)completionMedian, closedDurations.Count, null, options),
            // AverageResponseDays: uses ClosedAt as proxy for ResponseCompletedAt (no discrete field in snapshot).
            // Only shown when ResponseCompleted transactions exist in period; otherwise unavailable.
            hasResponseData
                ? Kpi(new AnalysisMetricDefinition { Key = "AverageResponseDays", Title = "متوسط زمن الرد (تقديري)", Definition = "متوسط الأيام بين الوارد وإغلاق المعاملة للمعاملات المكتمل ردّها. يستخدم ClosedAt كبديل لتاريخ إكمال الرد.", Formula = "avg(ClosedAt - IncomingDate) where ResponseCompleted", FieldsUsed = "IncomingDate, ClosedAt, ResponseCompleted", Unit = "يوم", Format = MetricValueTypes.Decimal, MinimumSampleSize = options.MinimumComparisonSampleSize, Direction = KpiDirection.LowerIsBetter }, (decimal)responseAverage, responseDurations.Count, null, options)
                : UnavailableKpi("AverageResponseDays", "متوسط زمن الرد", "لا توجد معاملات مكتملة الرد في الفترة الحالية؛ المؤشر غير متاح."),
            Kpi(new AnalysisMetricDefinition { Key = "PendingAssignmentsCount", Title = "الإفادات المعلقة", Definition = "عدد الإفادات/التكليفات المطلوبة ولم ترد.", Formula = "sum(PendingReplyAssignmentCount)", FieldsUsed = "Assignments", Unit = "إفادة", Format = MetricValueTypes.Number, MinimumSampleSize = 1, Direction = KpiDirection.LowerIsBetter }, pendingAssignments, current.TotalTransactions, null, options),
            Kpi(new AnalysisMetricDefinition { Key = "PartialRepliesCount", Title = "الردود الجزئية", Definition = "عدد المعاملات المفتوحة ذات رد جزئي.", Formula = "count(IsPartialReply)", FieldsUsed = "Assignments, Status", Unit = TransactionUnit, Format = MetricValueTypes.Number, MinimumSampleSize = 1, Direction = KpiDirection.LowerIsBetter }, current.PartialResponseCount, current.TotalTransactions, previous?.PartialResponseCount, options),
            Kpi(new AnalysisMetricDefinition { Key = "ResponseRequiredOpenCount", Title = "مفتوحة تتطلب ردًا", Definition = "عدد المعاملات المفتوحة التي تتطلب ردًا.", Formula = "count(IsOpen && RequiresResponse)", FieldsUsed = "RequiresResponse, Status", Unit = TransactionUnit, Format = MetricValueTypes.Number, MinimumSampleSize = 1, Direction = KpiDirection.LowerIsBetter }, open.Count(s => s.RequiresResponse), open.Count, null, options),
            Kpi(new AnalysisMetricDefinition { Key = "StaleTransactionsCount", Title = "معاملات بلا تحديث حديث", Definition = $"معاملات مفتوحة بلا تحديث أو متابعة منذ {options.StaleTransactionDays} أيام فأكثر.", Formula = "last action age >= threshold", FieldsUsed = "UpdatedAt, CreatedAt, LastFollowUpDate", Unit = TransactionUnit, Format = MetricValueTypes.Number, MinimumSampleSize = 1, Direction = KpiDirection.LowerIsBetter }, stale, open.Count, null, options),
            // BacklogGrowthRate is only valid when a comparison period exists.
            // Without a baseline, returning current.OpenCount would show a delta that isn't one.
            hasComparisonPeriod
                ? Kpi(new AnalysisMetricDefinition { Key = "BacklogGrowthRate", Title = "تغير الرصيد المفتوح", Definition = "الفرق بين الرصيد المفتوح الحالي والسابق.", Formula = "current open - previous open", FieldsUsed = "Status", Unit = TransactionUnit, Format = MetricValueTypes.Number, MinimumSampleSize = options.MinimumComparisonSampleSize, Direction = KpiDirection.LowerIsBetter }, backlogGrowth, current.TotalTransactions, previousOpen, options)
                : UnavailableKpi("BacklogGrowthRate", "تغير الرصيد المفتوح", "لا توجد فترة مقارنة؛ تغير الرصيد غير قابل للحساب."),
            Kpi(new AnalysisMetricDefinition { Key = "ClosureToIncomingRatio", Title = "نسبة الإغلاق إلى الوارد", Definition = "نسبة المغلق إلى إجمالي الوارد في الفترة.", Formula = "closed / incoming", FieldsUsed = "Status, IncomingDate", Unit = "%", Format = MetricValueTypes.Percent, MinimumSampleSize = options.MinimumComparisonSampleSize, Direction = KpiDirection.HigherIsBetter }, (decimal)closureToIncomingRatio, current.TotalTransactions, previous is null ? null : Rate(previous.ClosedCount, previous.TotalTransactions), options),
            Kpi(new AnalysisMetricDefinition { Key = "OldestOpenTransactionAgeDays", Title = "عمر أقدم معاملة مفتوحة", Definition = "أكبر عمر بالأيام بين المعاملات المفتوحة.", Formula = "max(ElapsedDays where IsOpen)", FieldsUsed = "IncomingDate, Status", Unit = "يوم", Format = MetricValueTypes.Number, MinimumSampleSize = 1, Direction = KpiDirection.LowerIsBetter }, oldestOpen, open.Count, null, options),
            Kpi(new AnalysisMetricDefinition { Key = "AverageFollowUpsPerTransaction", Title = "متوسط المتابعات لكل معاملة", Definition = "مؤشر تقريبي يعتمد على وجود آخر متابعة؛ لا يحسب كل أحداث المتابعة.", Formula = "transactions with follow-up / total", FieldsUsed = "LastFollowUpDate", Unit = "متابعة", Format = MetricValueTypes.Decimal, MinimumSampleSize = 1, Direction = KpiDirection.Neutral }, (decimal)followUpAverage, current.TotalTransactions, null, options),
            Kpi(new AnalysisMetricDefinition { Key = "DataCompletenessRate", Title = "نسبة اكتمال البيانات", Definition = "نسبة اكتمال الحقول التشغيلية المطلوبة والشرطية.", Formula = "completed required fields / expected fields", FieldsUsed = "category, party, department, due date, priority", Unit = "%", Format = MetricValueTypes.Percent, MinimumSampleSize = 1, Direction = KpiDirection.HigherIsBetter }, (decimal)completeness, current.TotalTransactions, null, options),
            UnavailableKpi("AverageFirstActionHours", "متوسط ساعات أول إجراء", "لا توجد علامة زمنية موثوقة لأول إجراء في snapshot الحالي."),
            UnavailableKpi("ReopenedTransactionsRate", "نسبة إعادة الفتح", "لا توجد حالة أو حدث إعادة فتح مستقل في بيانات التقرير الحالية.")
        ];
    }

    private static AnalyticalKpiDto Kpi(
        AnalysisMetricDefinition metric,
        decimal value,
        int sampleSize,
        decimal? previousValue,
        ReportingAnalysisOptions options)
    {
        return new AnalyticalKpiDto
        {
            Key = metric.Key,
            Title = metric.Title,
            Definition = metric.Definition,
            Formula = metric.Formula,
            FieldsUsed = metric.FieldsUsed,
            NumericValue = value,
            DisplayValue = FormatValue(value, metric.Unit, metric.Format),
            Unit = metric.Unit,
            Format = metric.Format,
            SampleSize = sampleSize,
            MinimumSampleSize = metric.MinimumSampleSize,
            Direction = metric.Direction,
            Comparison = Compare(value, previousValue, metric.Direction, options)
        };
    }

    private static AnalyticalKpiDto UnavailableKpi(string key, string title, string reason) => new()
    {
        Key = key,
        Title = title,
        Definition = reason,
        Formula = NotAvailable,
        FieldsUsed = NotAvailable,
        DisplayValue = "غير متاح",
        Unit = string.Empty,
        Format = "text",
        IsAvailable = false,
        UnavailableReason = reason,
        Direction = KpiDirection.Neutral,
        Comparison = new KpiComparisonDto { TrendDirection = TrendDirection.NotComparable, TrendClassification = "not_comparable" }
    };

    private static KpiComparisonDto Compare(
        decimal current,
        decimal? previous,
        KpiDirection direction,
        ReportingAnalysisOptions options)
    {
        if (!previous.HasValue)
            return new KpiComparisonDto { CurrentValue = current, TrendDirection = TrendDirection.NotComparable, TrendClassification = "not_comparable" };

        var absolute = current - previous.Value;
        decimal? percent = previous.Value == 0
            ? null
            : Math.Round(absolute / Math.Abs(previous.Value) * 100, 1);
        var absPercent = Math.Abs(percent ?? 0);
        var classification = ClassifyChange(percent, absPercent, options);
        var trend = ResolveTrend(absolute, percent, direction, options);

        return new KpiComparisonDto
        {
            CurrentValue = current,
            PreviousValue = previous,
            AbsoluteChange = absolute,
            PercentageChange = percent,
            TrendDirection = trend,
            TrendClassification = classification,
            ComparisonLabel = percent.HasValue ? $"{absolute:+0.##;-0.##;0} ({percent:+0.#;-0.#;0}%)" : $"{absolute:+0.##;-0.##;0}"
        };
    }

    private static TrendDirection ResolveTrend(decimal absolute, decimal? percent, KpiDirection direction, ReportingAnalysisOptions options)
    {
        if (!percent.HasValue || Math.Abs(percent.Value) < options.StableChangeThresholdPercent || direction == KpiDirection.Neutral)
            return percent.HasValue ? TrendDirection.Stable : TrendDirection.NotComparable;

        var increased = absolute > 0;
        return direction switch
        {
            KpiDirection.HigherIsBetter => increased ? TrendDirection.Improved : TrendDirection.Declined,
            KpiDirection.LowerIsBetter => increased ? TrendDirection.Declined : TrendDirection.Improved,
            _ => TrendDirection.Stable
        };
    }

    private static string ClassifyChange(decimal? percent, decimal absPercent, ReportingAnalysisOptions options)
    {
        if (!percent.HasValue)
            return "not_comparable";

        if (absPercent < options.StableChangeThresholdPercent)
            return "stable";

        return absPercent >= options.SignificantChangeThresholdPercent
            ? "significant"
            : "moderate";
    }

    private static List<ExecutiveInsightDto> BuildExecutiveInsights(
        InstitutionalMetricsResult metrics,
        IReadOnlyList<SignificantFindingDto> findings,
        IReadOnlyList<CriticalCaseDto> criticalCases,
        IReadOnlyList<DepartmentAnalysisRowDto> departments,
        IReadOnlyList<ExternalPartyAnalysisRowDto> externalParties,
        ReportingAnalysisOptions options)
    {
        var insights = new List<ExecutiveInsightDto>();
        var total = metrics.TotalTransactions;
        var overdueRate = Rate(metrics.OverdueCount, Math.Max(1, total));
        insights.Add(new ExecutiveInsightDto
        {
            Code = "EXEC_TOTAL",
            Text = $"بلغ إجمالي المعاملات خلال الفترة {total:N0} معاملة، منها {metrics.ClosedCount:N0} مغلقة و{metrics.OpenCount:N0} مفتوحة.",
            Evidence = $"total={total};closed={metrics.ClosedCount};open={metrics.OpenCount}",
            Severity = AnalyticalSeverity.Low
        });

        if (metrics.ClosedCount > 0)
        {
            insights.Add(new ExecutiveInsightDto
            {
                Code = "EXEC_COMPLETION",
                Text = $"سجلت نسبة الإنجاز ضمن المهلة {metrics.OnTimeCompletionRate:N1}%، وبلغ متوسط مدة الإنجاز {metrics.AverageCompletionDays:N1} يومًا.",
                Evidence = $"onTime={metrics.OnTimeCompletionRate};avgDays={metrics.AverageCompletionDays}",
                Severity = metrics.OnTimeCompletionRate < 60 ? AnalyticalSeverity.High : AnalyticalSeverity.Medium
            });
        }

        if (metrics.OverdueCount > 0)
        {
            insights.Add(new ExecutiveInsightDto
            {
                Code = "EXEC_OVERDUE",
                Text = $"توجد {metrics.OverdueCount:N0} معاملة متأخرة تمثل {overdueRate:N1}% من نطاق التقرير، منها {metrics.OpenOverdueCount:N0} مفتوحة متأخرة و{metrics.CompletedLateCount:N0} منجزة/مغلقة بعد الاستحقاق.",
                Evidence = $"overdue={metrics.OverdueCount};openOverdue={metrics.OpenOverdueCount};completedLate={metrics.CompletedLateCount};rate={overdueRate}",
                Severity = overdueRate >= 20 ? AnalyticalSeverity.High : AnalyticalSeverity.Medium
            });
        }

        var topDepartment = departments.OrderByDescending(d => d.OverdueCount).ThenByDescending(d => d.OpenCount).FirstOrDefault();
        if (topDepartment is not null && topDepartment.OverdueCount > 0)
        {
            var departmentText = ReportDepartmentNameNormalizer.IsUndefined(topDepartment.DepartmentName)
                ? "معاملات بلا إدارة مختصة محددة"
                : topDepartment.DepartmentName;
            insights.Add(new ExecutiveInsightDto
            {
                Code = "EXEC_DEPARTMENT_CONCENTRATION",
                Text = $"تركز أعلى عدد من حالات التأخر في {departmentText} بعدد {topDepartment.OverdueCount:N0} معاملة متأخرة.",
                Evidence = $"department={departmentText};overdue={topDepartment.OverdueCount}",
                Severity = topDepartment.HasSmallSample ? AnalyticalSeverity.Medium : AnalyticalSeverity.High
            });
        }

        var topParty = externalParties.OrderByDescending(p => p.PendingResponseCount).FirstOrDefault();
        if (topParty is not null && topParty.PendingResponseCount > 0)
        {
            insights.Add(new ExecutiveInsightDto
            {
                Code = "EXEC_EXTERNAL_PENDING",
                Text = $"أعلى معاملات منتظرة من جهة خارجية كانت لدى {topParty.ExternalPartyName} بعدد {topParty.PendingResponseCount:N0} معاملة.",
                Evidence = $"party={topParty.ExternalPartyName};pending={topParty.PendingResponseCount}",
                Severity = AnalyticalSeverity.Medium
            });
        }

        insights.AddRange(findings.Take(options.MaxExecutiveFindings).Select(f => new ExecutiveInsightDto
        {
            Code = "FINDING_" + f.Code,
            Text = f.Description,
            Evidence = f.Evidence,
            Severity = f.Severity
        }));

        if (criticalCases.Count > 0)
        {
            insights.Add(new ExecutiveInsightDto
            {
                Code = "EXEC_CRITICAL_CASES",
                Text = $"توجد {criticalCases.Count:N0} حالة حرجة مختصرة تستدعي متابعة تشغيلية وفق القواعد الموثقة.",
                Evidence = $"criticalCases={criticalCases.Count}",
                Severity = AnalyticalSeverity.Critical
            });
        }

        return insights;
    }

    private static List<CriticalCaseDto> BuildCriticalCases(IReadOnlyList<TransactionReportSnapshot> snapshots, ReportingAnalysisOptions options, DateTime referenceDate) =>
        snapshots
            .Where(s => s.IsOpen)
            .SelectMany(s => CriticalRules(s, options, referenceDate))
            .OrderByDescending(c => c.Severity)
            .ThenByDescending(c => c.DaysOverdue ?? c.AgeDays)
            .ThenBy(c => c.TransactionId)
            .ToList();

    private static IEnumerable<CriticalCaseDto> CriticalRules(TransactionReportSnapshot snapshot, ReportingAnalysisOptions options, DateTime referenceDate)
    {
        if (snapshot.Priority is Priority.VeryUrgent or Priority.Urgent && snapshot.IsOpenOverdue)
            yield return CriticalCase(snapshot, "CRITICAL_PRIORITY_OVERDUE", "أولوية عالية متأخرة", "مراجعة المعاملة المتأخرة فورًا", AnalyticalSeverity.Critical, referenceDate);

        if (snapshot.IsOpenOverdue && snapshot.ElapsedDays >= options.CriticalOverdueDays)
            yield return CriticalCase(snapshot, "OPEN_OLDER_THAN_THRESHOLD", $"معاملة مفتوحة متأخرة لأكثر من {options.CriticalOverdueDays} أيام", "تصعيد المعاملة المتأخرة", AnalyticalSeverity.High, referenceDate);

        if (snapshot.PendingReplyAssignmentCount > 0 && snapshot.EarliestPendingReplyDueDate.HasValue && snapshot.EarliestPendingReplyDueDate.Value.Date < referenceDate.Date)
            yield return CriticalCase(snapshot, "PENDING_ASSIGNMENT_OVERDUE", "إفادة إدارة معلقة بعد تاريخ الاستحقاق", "طلب الإفادة الناقصة", AnalyticalSeverity.High, referenceDate);

        if (snapshot.RequiresResponse && !snapshot.ResponseCompleted && snapshot.ResponseDueDate.HasValue && snapshot.ResponseDueDate.Value.Date < referenceDate.Date)
            yield return CriticalCase(snapshot, "RESPONSE_REQUIRED_OVERDUE", "معاملة تتطلب ردًا بعد تاريخ المهلة", "استكمال الرد المطلوب", AnalyticalSeverity.High, referenceDate);

        if (ReportingTemporalCalculator.IsStale(snapshot, referenceDate, options.StaleTransactionDays))
            yield return CriticalCase(snapshot, "STALE_WITHOUT_UPDATE", $"لا يوجد تحديث حديث منذ {options.StaleTransactionDays} أيام أو أكثر", "تحديث حالة المعاملة أو تحديد إجراء تالٍ", AnalyticalSeverity.Medium, referenceDate);

        if (snapshot.IsPartialReply)
            yield return CriticalCase(snapshot, "PARTIAL_REPLY_PENDING", "ردود جزئية لم تكتمل", "استكمال الردود المتبقية", AnalyticalSeverity.Medium, referenceDate);
    }

    private static CriticalCaseDto CriticalCase(TransactionReportSnapshot snapshot, string code, string label, string action, AnalyticalSeverity severity, DateTime referenceDate) => new()
    {
        TransactionId = snapshot.TransactionId,
        IncomingNumber = snapshot.IncomingNumber,
        Subject = snapshot.Subject,
        Department = snapshot.ResponsibleDepartment,
        ExternalParty = snapshot.IncomingParty,
        Priority = PriorityLabel(snapshot.Priority),
        AgeDays = snapshot.ElapsedDays,
        DaysOverdue = ReportingTemporalCalculator.DaysOverdue(snapshot, referenceDate),
        ReasonCode = code,
        ReasonLabel = label,
        RequiredAction = action,
        SuggestedOwner = string.IsNullOrWhiteSpace(snapshot.ResponsibleDepartment) ? "إدارة المتابعة" : snapshot.ResponsibleDepartment,
        Severity = severity
    };

    private static List<DepartmentAnalysisRowDto> BuildDepartments(
        IReadOnlyList<TransactionReportSnapshot> current,
        IReadOnlyList<TransactionReportSnapshot> previous,
        ReportingAnalysisOptions options)
    {
        var previousOpen = previous
            .Where(s => s.ResponsibleDepartmentId.HasValue || !string.IsNullOrWhiteSpace(s.ResponsibleDepartment))
            .GroupBy(s => s.ResponsibleDepartmentId?.ToString(CultureInfo.InvariantCulture) ?? s.ResponsibleDepartment)
            .ToDictionary(g => g.Key, g => g.Count(s => s.IsOpen), StringComparer.Ordinal);
        var systemAverageCompletion = CompletionDays(current).DefaultIfEmpty(0).Average();

        return current
            .GroupBy(s => new { s.ResponsibleDepartmentId, Name = BlankToUnknown(s.ResponsibleDepartment) })
            .Select(g =>
            {
                var closedDays = CompletionDays(g).ToList();
                var key = g.Key.ResponsibleDepartmentId?.ToString(CultureInfo.InvariantCulture) ?? g.Key.Name;
                previousOpen.TryGetValue(key, out var previousOpenCount);
                var average = closedDays.Count == 0 ? 0 : Math.Round(closedDays.Average(), 1);
                var systemComparison = ResolveSystemComparison(average, systemAverageCompletion);
                return new DepartmentAnalysisRowDto
                {
                    DepartmentId = g.Key.ResponsibleDepartmentId,
                    DepartmentName = g.Key.Name,
                    IncomingCount = g.Count(),
                    ClosedCount = g.Count(s => s.IsClosed),
                    OpenCount = g.Count(s => s.IsOpen),
                    OverdueCount = g.Count(s => s.IsOverdue),
                    OnTimeCompletionRate = CalculateOnTimeRate(g),
                    AverageCompletionDays = average,
                    MedianCompletionDays = InstitutionalReportStatistics.Median(closedDays),
                    PendingAssignments = g.Sum(s => s.PendingReplyAssignmentCount),
                    PartialReplies = g.Count(s => s.IsPartialReply),
                    BacklogChange = g.Count(s => s.IsOpen) - previousOpenCount,
                    OldestOpenAgeDays = g.Where(s => s.IsOpen).Select(s => s.ElapsedDays).DefaultIfEmpty(0).Max(),
                    DataCompletenessRate = CalculateCompletenessRate(g.ToList()),
                    SampleSize = g.Count(),
                    HasSmallSample = g.Count() < options.MinimumRankingSampleSize,
                    SystemComparison = systemComparison
                };
            })
            .OrderByDescending(d => d.OverdueCount)
            .ThenByDescending(d => d.OpenCount)
            .ThenByDescending(d => d.SampleSize)
            .ToList();
    }

    private sealed record DepartmentRecognitionMetrics(
        string Key,
        int? DepartmentId,
        string DepartmentName,
        int TransactionCount,
        int ClosedCount,
        int OverdueCount,
        double OverdueRate,
        double OnTimeCompletionRate,
        double AverageCompletionDays,
        double DataCompletenessRate,
        double PendingAssignmentsRate);

    private sealed record DepartmentRecognitionComparison(
        DepartmentRecognitionMetrics Current,
        DepartmentRecognitionMetrics Previous);

    private static List<DepartmentRecognitionRowDto> BuildDepartmentRecognitions(
        IReadOnlyList<TransactionReportSnapshot> current,
        IReadOnlyList<TransactionReportSnapshot> previous,
        ReportingAnalysisOptions options)
    {
        var currentMetrics = BuildDepartmentRecognitionMetrics(current).ToList();
        var previousMetrics = BuildDepartmentRecognitionMetrics(previous)
            .ToDictionary(metric => metric.Key, StringComparer.Ordinal);
        var minimumSampleSize = options.MinimumRankingSampleSize;
        var systemAverageCompletion = Average(CompletionDays(current));

        var outstanding = currentMetrics
            .Where(metric => IsEligibleForOutstandingRecognition(metric, minimumSampleSize))
            .Select(metric => ToOutstandingRecognition(metric, systemAverageCompletion, minimumSampleSize))
            .OrderByDescending(row => row.Score)
            .ThenByDescending(row => row.TransactionCount)
            .ThenBy(row => row.DepartmentName, StringComparer.Ordinal)
            .Take(5);

        var improved = currentMetrics
            .Where(metric => metric.TransactionCount >= minimumSampleSize)
            .SelectMany(metric => previousMetrics.TryGetValue(metric.Key, out var previousMetric)
                ? new[] { new DepartmentRecognitionComparison(metric, previousMetric) }
                : Array.Empty<DepartmentRecognitionComparison>())
            .Where(pair => pair.Previous.TransactionCount >= minimumSampleSize)
            .Select(pair => ToImprovedRecognition(pair.Current, pair.Previous, minimumSampleSize))
            .Where(row => row.ImprovementValue >= 10)
            .OrderByDescending(row => row.ImprovementValue)
            .ThenByDescending(row => row.TransactionCount)
            .ThenBy(row => row.DepartmentName, StringComparer.Ordinal)
            .Take(5);

        return outstanding.Concat(improved).ToList();
    }

    private static IEnumerable<DepartmentRecognitionMetrics> BuildDepartmentRecognitionMetrics(
        IReadOnlyList<TransactionReportSnapshot> snapshots)
    {
        return snapshots
            .Where(snapshot => snapshot.ResponsibleDepartmentId.HasValue || !string.IsNullOrWhiteSpace(snapshot.ResponsibleDepartment))
            .GroupBy(snapshot => new
            {
                snapshot.ResponsibleDepartmentId,
                Name = BlankToUnknown(snapshot.ResponsibleDepartment)
            })
            .Select(group =>
            {
                var rows = group.ToList();
                var totalCount = rows.Count;
                var closedCount = rows.Count(snapshot => snapshot.IsClosed);
                var overdueCount = rows.Count(snapshot => snapshot.IsOverdue);
                var pendingAssignmentsCount = rows.Sum(snapshot => snapshot.PendingReplyAssignmentCount);
                var key = group.Key.ResponsibleDepartmentId?.ToString(CultureInfo.InvariantCulture) ?? group.Key.Name;
                var completionDays = CompletionDays(rows).ToList();
                return new DepartmentRecognitionMetrics(
                    Key: key,
                    DepartmentId: group.Key.ResponsibleDepartmentId,
                    DepartmentName: group.Key.Name,
                    TransactionCount: totalCount,
                    ClosedCount: closedCount,
                    OverdueCount: overdueCount,
                    OverdueRate: Math.Round(overdueCount * 100.0 / Math.Max(1, totalCount), 1),
                    OnTimeCompletionRate: CalculateOnTimeRate(rows),
                    AverageCompletionDays: completionDays.Count == 0 ? 0 : Math.Round(completionDays.Average(), 1),
                    DataCompletenessRate: CalculateCompletenessRate(rows),
                    PendingAssignmentsRate: Math.Round(pendingAssignmentsCount * 100.0 / Math.Max(1, totalCount), 1));
            });
    }

    private static bool IsEligibleForOutstandingRecognition(DepartmentRecognitionMetrics metric, int minimumSampleSize) =>
        metric.TransactionCount >= minimumSampleSize
        && !ReportDepartmentNameNormalizer.IsUndefined(metric.DepartmentName)
        && metric.DataCompletenessRate >= 85
        && metric.OnTimeCompletionRate >= 70
        && metric.OverdueRate <= 20;

    private static DepartmentRecognitionRowDto ToOutstandingRecognition(
        DepartmentRecognitionMetrics metric,
        double systemAverageCompletion,
        int minimumSampleSize)
    {
        var score = CalculateOutstandingScore(metric);
        var reasons = new List<string>();
        if (metric.OnTimeCompletionRate >= 80)
            reasons.Add("ارتفاع نسبة الإنجاز في الوقت");
        if (metric.OverdueRate <= 10)
            reasons.Add("انخفاض المتأخرات");
        if (metric.AverageCompletionDays > 0 && (systemAverageCompletion <= 0 || metric.AverageCompletionDays <= systemAverageCompletion))
            reasons.Add("تحسن مدة المعالجة");
        if (metric.PendingAssignmentsRate <= 10)
            reasons.Add("اكتمال الإفادات");
        if (metric.DataCompletenessRate >= 90)
            reasons.Add("جودة بيانات مرتفعة");
        if (metric.TransactionCount >= minimumSampleSize)
            reasons.Add("حجم معاملات مناسب");

        return new DepartmentRecognitionRowDto
        {
            DepartmentId = metric.DepartmentId,
            DepartmentName = metric.DepartmentName,
            RecognitionType = "متميزة",
            TransactionCount = metric.TransactionCount,
            OnTimeCompletionRate = metric.OnTimeCompletionRate,
            OverdueCount = metric.OverdueCount,
            AverageCompletionDays = metric.AverageCompletionDays,
            DataCompletenessRate = metric.DataCompletenessRate,
            ImprovementValue = 0,
            Reason = string.Join("، ", reasons.Distinct()),
            Score = score,
            HasSufficientSample = metric.TransactionCount >= minimumSampleSize,
            IsExcludedByDataQuality = metric.DataCompletenessRate < 85
        };
    }

    private static DepartmentRecognitionRowDto ToImprovedRecognition(
        DepartmentRecognitionMetrics current,
        DepartmentRecognitionMetrics previous,
        int minimumSampleSize)
    {
        var onTimeImprovement = Math.Max(0, current.OnTimeCompletionRate - previous.OnTimeCompletionRate);
        var overdueImprovement = Math.Max(0, previous.OverdueRate - current.OverdueRate);
        var completionImprovement = previous.AverageCompletionDays <= 0 || current.AverageCompletionDays <= 0
            ? 0
            : Math.Max(0, previous.AverageCompletionDays - current.AverageCompletionDays);
        var completenessImprovement = Math.Max(0, current.DataCompletenessRate - previous.DataCompletenessRate);
        var pendingImprovement = Math.Max(0, previous.PendingAssignmentsRate - current.PendingAssignmentsRate);
        var improvementValue = Math.Round(
            onTimeImprovement * 0.35
            + overdueImprovement * 0.25
            + Math.Min(completionImprovement * 2, 20)
            + completenessImprovement * 0.15
            + pendingImprovement * 0.15,
            1);

        return new DepartmentRecognitionRowDto
        {
            DepartmentId = current.DepartmentId,
            DepartmentName = current.DepartmentName,
            RecognitionType = "الأكثر تحسنًا",
            TransactionCount = current.TransactionCount,
            OnTimeCompletionRate = current.OnTimeCompletionRate,
            OverdueCount = current.OverdueCount,
            AverageCompletionDays = current.AverageCompletionDays,
            DataCompletenessRate = current.DataCompletenessRate,
            ImprovementValue = improvementValue,
            Reason = BuildImprovementReason(onTimeImprovement, overdueImprovement, completionImprovement, completenessImprovement, pendingImprovement),
            Score = CalculateOutstandingScore(current),
            HasSufficientSample = current.TransactionCount >= minimumSampleSize,
            IsExcludedByDataQuality = current.DataCompletenessRate < 85
        };
    }

    private static double CalculateOutstandingScore(DepartmentRecognitionMetrics metric)
    {
        var overdueComponent = Math.Max(0, 100 - metric.OverdueRate);
        var pendingComponent = Math.Max(0, 100 - metric.PendingAssignmentsRate);
        return Math.Round(
            metric.OnTimeCompletionRate * 0.35
            + overdueComponent * 0.25
            + metric.DataCompletenessRate * 0.25
            + pendingComponent * 0.15,
            1);
    }

    private static string BuildImprovementReason(
        double onTimeImprovement,
        double overdueImprovement,
        double completionImprovement,
        double completenessImprovement,
        double pendingImprovement)
    {
        var reasons = new List<string>();
        if (onTimeImprovement >= 5)
            reasons.Add("ارتفاع نسبة الإنجاز في الوقت");
        if (overdueImprovement >= 5)
            reasons.Add("انخفاض المتأخرات");
        if (completionImprovement >= 1)
            reasons.Add("تحسن مدة المعالجة");
        if (pendingImprovement >= 5)
            reasons.Add("اكتمال الإفادات");
        if (completenessImprovement >= 5)
            reasons.Add("تحسن جودة البيانات مقارنة بالفترة السابقة");

        return reasons.Count == 0 ? "تحسن مركب في مؤشرات الأداء مقارنة بالفترة السابقة" : string.Join("، ", reasons);
    }

    private static List<ExternalPartyAnalysisRowDto> BuildExternalParties(IReadOnlyList<TransactionReportSnapshot> snapshots) =>
        snapshots
            .GroupBy(s => BlankToUnknown(s.IncomingParty))
            .Select(g =>
            {
                var pending = g.Where(s => s.RequiresResponse && !s.ResponseCompleted).ToList();
                var responseDays = ResponseDays(g).ToList();
                var topCategories = string.Join("، ", g.GroupBy(s => BlankToUnknown(s.CategoryName))
                    .OrderByDescending(c => c.Count())
                    .Take(3)
                    .Select(c => c.Key));
                return new ExternalPartyAnalysisRowDto
                {
                    ExternalPartyName = g.Key,
                    IncomingCount = g.Count(),
                    OutgoingCount = g.Count(s => !string.IsNullOrWhiteSpace(s.OutgoingNumber)),
                    PendingResponseCount = pending.Count,
                    OverdueResponseCount = pending.Count(s => s.IsOpenOverdue),
                    AverageResponseDays = responseDays.Count == 0 ? 0 : Math.Round(responseDays.Average(), 1),
                    MedianResponseDays = InstitutionalReportStatistics.Median(responseDays),
                    FollowUpCount = g.Count(s => s.LastFollowUpDate.HasValue),
                    OldestPendingResponseDays = pending.Select(s => s.ElapsedDays).DefaultIfEmpty(0).Max(),
                    TopCategories = topCategories
                };
            })
            .OrderByDescending(p => p.PendingResponseCount)
            .ThenByDescending(p => p.IncomingCount)
            .ToList();

    private static List<CategoryAnalysisRowDto> BuildCategories(IReadOnlyList<TransactionReportSnapshot> snapshots) =>
        snapshots
            .GroupBy(s => BlankToUnknown(s.CategoryName))
            .Select(g => new CategoryAnalysisRowDto
            {
                CategoryName = g.Key,
                TransactionCount = g.Count(),
                OpenCount = g.Count(s => s.IsOpen),
                OverdueCount = g.Count(s => s.IsOverdue),
                OnTimeCompletionRate = CalculateOnTimeRate(g),
                AverageCompletionDays = Average(CompletionDays(g)),
                PendingAssignments = g.Sum(s => s.PendingReplyAssignmentCount)
            })
            .OrderByDescending(c => c.TransactionCount)
            .ToList();

    private static List<PriorityAnalysisRowDto> BuildPriorities(IReadOnlyList<TransactionReportSnapshot> snapshots) =>
        snapshots
            .GroupBy(s => PriorityLabel(s.Priority))
            .Select(g => new PriorityAnalysisRowDto
            {
                Priority = g.Key,
                Count = g.Count(),
                OpenCount = g.Count(s => s.IsOpen),
                OverdueCount = g.Count(s => s.IsOverdue),
                AverageAgeDays = g.Any() ? Math.Round(g.Average(s => s.ElapsedDays), 1) : 0,
                OnTimeRate = CalculateOnTimeRate(g)
            })
            .OrderByDescending(p => p.OverdueCount)
            .ThenByDescending(p => p.Count)
            .ToList();

    private static List<BottleneckRowDto> BuildBottlenecks(IReadOnlyList<TransactionReportSnapshot> snapshots, DateTime referenceDate, int staleDays)
    {
        var rows = snapshots.Where(s => s.IsOpen).Select(s => new { Snapshot = s, Reason = BottleneckReason(s, referenceDate, staleDays) }).ToList();
        var total = rows.Count == 0 ? 1 : rows.Count;
        return rows
            .GroupBy(row => row.Reason)
            .Select(g => new BottleneckRowDto
            {
                ReasonCode = g.Key.Code,
                ReasonLabel = g.Key.Label,
                Count = g.Count(),
                SharePercent = Math.Round(g.Count() * 100.0 / total, 1),
                AverageDelayDays = Math.Round(g.Average(x => Math.Max(0, x.Snapshot.ElapsedDays)), 1),
                TopDepartments = string.Join("، ", g.GroupBy(x => BlankToUnknown(x.Snapshot.ResponsibleDepartment)).OrderByDescending(x => x.Count()).Take(3).Select(x => x.Key)),
                TopExternalParties = string.Join("، ", g.GroupBy(x => BlankToUnknown(x.Snapshot.IncomingParty)).OrderByDescending(x => x.Count()).Take(3).Select(x => x.Key)),
                ExampleTransactionIds = g.Select(x => x.Snapshot.TransactionId).Take(5).ToList()
            })
            .OrderByDescending(r => r.Count)
            .ToList();
    }

    private static (string Code, string Label) BottleneckReason(TransactionReportSnapshot snapshot, DateTime referenceDate, int staleDays)
    {
        if (snapshot.PendingReplyAssignmentCount > 0)
            return ("pending_department_assignment", "إفادة أو تكليف إدارة معلق");
        if (snapshot.RequiresResponse && !snapshot.ResponseCompleted && snapshot.ResponseDueDate.HasValue)
            return ("external_response_delay", "معاملة منتظرة من الجهة");
        if (snapshot.IsPartialReply)
            return ("partial_response", "رد جزئي غير مكتمل");
        if (ReportingTemporalCalculator.IsStale(snapshot, referenceDate, staleDays))
            return ("stale_without_update", "بلا تحديث حديث");
        if (string.IsNullOrWhiteSpace(snapshot.CategoryName) || string.IsNullOrWhiteSpace(snapshot.ResponsibleDepartment))
            return ("missing_information", "بيانات تشغيلية ناقصة");
        return ("unknown_unclassified", "غير مصنف من البيانات المتاحة");
    }

    private static List<DataQualityIssueDto> BuildDataQualityIssues(IReadOnlyList<TransactionReportSnapshot> snapshots)
    {
        var total = snapshots.Count == 0 ? 1 : snapshots.Count;
        var definitions = new (string Code, string Label, Func<TransactionReportSnapshot, bool> Predicate, string Fields, string Correction, AnalyticalSeverity Severity)[]
        {
            ("missing_category", "تصنيف مفقود", s => string.IsNullOrWhiteSpace(s.CategoryName), "CategoryId/Category", "استكمال تصنيف المعاملة.", AnalyticalSeverity.Medium),
            ("missing_external_party", "جهة واردة مفقودة", s => string.IsNullOrWhiteSpace(s.IncomingParty), "IncomingFromPartyId/IncomingFrom", "ربط الجهة الواردة أو توثيقها.", AnalyticalSeverity.Medium),
            ("missing_responsible_department", "معاملات بلا إدارة مختصة", s => ReportDepartmentNameNormalizer.IsUndefined(s.ResponsibleDepartment), "Assignments/OutgoingDepartments", "تحديد الإدارة المختصة.", AnalyticalSeverity.High),
            ("missing_due_date", "مهلة رد مفقودة", s => s.RequiresResponse && !s.ResponseDueDate.HasValue && !s.EarliestPendingReplyDueDate.HasValue, "ResponseDueDate/AssignmentDueDate", "إدخال تاريخ المهلة عند اشتراط الرد.", AnalyticalSeverity.Medium),
            ("partial_outgoing_data", "بيانات صادر غير مكتملة", s => !string.IsNullOrWhiteSpace(s.OutgoingNumber) && !s.OutgoingDate.HasValue, "OutgoingNumber/OutgoingDate", "استكمال تاريخ الصادر.", AnalyticalSeverity.Low),
            ("invalid_date_sequence", "تسلسل تواريخ غير منطقي", s => s.ClosedAt.HasValue && s.ClosedAt.Value.Date < s.IncomingDate.Date, "IncomingDate/ClosedAt", "مراجعة تواريخ الوارد والإغلاق.", AnalyticalSeverity.High),
            ("missing_priority", "أولوية غير محددة", s => !Enum.IsDefined(s.Priority), "Priority", "تحديد أولوية صحيحة.", AnalyticalSeverity.Medium),
        };

        return definitions
            .Select(def =>
            {
                var count = snapshots.Count(def.Predicate);
                return new DataQualityIssueDto
                {
                    IssueCode = def.Code,
                    Label = def.Label,
                    Count = count,
                    SharePercent = Math.Round(count * 100.0 / total, 1),
                    Severity = def.Severity,
                    AffectedFields = def.Fields,
                    SuggestedCorrection = def.Correction
                };
            })
            .Where(issue => issue.Count > 0)
            .OrderByDescending(issue => issue.Severity)
            .ThenByDescending(issue => issue.Count)
            .ToList();
    }

    private static List<SignificantFindingDto> BuildFindings(
        InstitutionalMetricsResult current,
        InstitutionalMetricsResult? previous,
        IReadOnlyList<DepartmentAnalysisRowDto> departments,
        IReadOnlyList<ExternalPartyAnalysisRowDto> externalParties,
        IReadOnlyList<DataQualityIssueDto> dataQuality,
        ReportingAnalysisOptions options)
    {
        var findings = new List<SignificantFindingDto>();
        if (previous is not null && previous.TotalTransactions >= options.MinimumComparisonSampleSize)
        {
            var currentOverdueRate = Rate(current.OverdueCount, current.TotalTransactions);
            var previousOverdueRate = Rate(previous.OverdueCount, previous.TotalTransactions);
            if (currentOverdueRate - previousOverdueRate >= options.SignificantChangeThresholdPercent)
            {
                findings.Add(new SignificantFindingDto
                {
                    Code = "OVERDUE_RATE_INCREASED",
                    Title = "ارتفاع نسبة التأخر",
                    Description = $"ارتفعت نسبة التأخر من {previousOverdueRate:N1}% إلى {currentOverdueRate:N1}%.",
                    Evidence = $"current={currentOverdueRate};previous={previousOverdueRate}",
                    Severity = AnalyticalSeverity.High,
                    CurrentValue = currentOverdueRate,
                    PreviousValue = previousOverdueRate,
                    AffectedScope = "التقرير"
                });
            }

            if (current.OpenCount > previous.OpenCount)
            {
                findings.Add(new SignificantFindingDto
                {
                    Code = "BACKLOG_INCREASED",
                    Title = "ارتفاع الرصيد المفتوح",
                    Description = $"زاد الرصيد المفتوح من {previous.OpenCount:N0} إلى {current.OpenCount:N0} معاملة.",
                    Evidence = $"currentOpen={current.OpenCount};previousOpen={previous.OpenCount}",
                    Severity = AnalyticalSeverity.Medium,
                    CurrentValue = current.OpenCount,
                    PreviousValue = previous.OpenCount,
                    AffectedScope = "التقرير"
                });
            }
        }

        var topDepartment = departments.FirstOrDefault();
        if (topDepartment is not null
            && topDepartment.OverdueCount > 0
            && !topDepartment.HasSmallSample
            && !ReportDepartmentNameNormalizer.IsUndefined(topDepartment.DepartmentName))
        {
            findings.Add(new SignificantFindingDto
            {
                Code = "DEPARTMENT_BACKLOG_CONCENTRATION",
                Title = "تركز التأخر في إدارة",
                Description = $"تسجل {topDepartment.DepartmentName} أعلى عدد معاملات متأخرة ({topDepartment.OverdueCount:N0}).",
                Evidence = $"department={topDepartment.DepartmentName};overdue={topDepartment.OverdueCount};sample={topDepartment.SampleSize}",
                Severity = AnalyticalSeverity.High,
                CurrentValue = topDepartment.OverdueCount,
                AffectedScope = topDepartment.DepartmentName
            });
        }

        var topParty = externalParties.FirstOrDefault();
        if (topParty is not null && topParty.PendingResponseCount > 0)
        {
            findings.Add(new SignificantFindingDto
            {
                Code = "EXTERNAL_PENDING_RESPONSES",
                Title = "معاملات منتظرة من جهة",
                Description = $"توجد {topParty.PendingResponseCount:N0} معاملة منتظرة من {topParty.ExternalPartyName}.",
                Evidence = $"party={topParty.ExternalPartyName};pending={topParty.PendingResponseCount}",
                Severity = AnalyticalSeverity.Medium,
                CurrentValue = topParty.PendingResponseCount,
                AffectedScope = topParty.ExternalPartyName
            });
        }

        var topQuality = dataQuality.FirstOrDefault();
        if (topQuality is not null)
        {
            findings.Add(new SignificantFindingDto
            {
                Code = "DATA_QUALITY_ISSUE",
                Title = "ملاحظة جودة بيانات",
                Description = $"{topQuality.Label}: {topQuality.Count:N0} سجل ({topQuality.SharePercent:N1}%).",
                Evidence = $"issue={topQuality.IssueCode};count={topQuality.Count};share={topQuality.SharePercent}",
                Severity = topQuality.Severity,
                CurrentValue = topQuality.Count,
                AffectedScope = topQuality.AffectedFields
            });
        }

        return findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Code, StringComparer.Ordinal)
            .ToList();
    }

    private static List<AnalyticalRecommendationDto> BuildRecommendations(
        IReadOnlyList<SignificantFindingDto> findings,
        IReadOnlyList<CriticalCaseDto> criticalCases,
        IReadOnlyList<DataQualityIssueDto> dataQuality)
    {
        var recommendations = new List<AnalyticalRecommendationDto>();
        foreach (var finding in findings)
        {
            recommendations.Add(new AnalyticalRecommendationDto
            {
                RecommendationId = "REC-" + finding.Code,
                SourceFindingCode = finding.Code,
                Priority = finding.Severity >= AnalyticalSeverity.High ? "high" : "medium",
                RecommendationText = RecommendationText(finding.Code),
                ResponsibleScope = ResponsibleScope(finding.AffectedScope),
                SuggestedDueDays = finding.Severity >= AnalyticalSeverity.High ? 2 : 7,
                EvidenceSummary = finding.Evidence
            });
        }

        if (criticalCases.Count > 0)
        {
            recommendations.Add(new AnalyticalRecommendationDto
            {
                RecommendationId = "REC-CRITICAL-CASES",
                SourceFindingCode = "CRITICAL_CASES",
                Priority = "high",
                RecommendationText = "مراجعة الحالات الحرجة المدرجة خلال 24 ساعة وتوثيق الإجراء التالي لكل حالة.",
                ResponsibleScope = "إدارة المتابعة",
                SuggestedDueDays = 1,
                EvidenceSummary = $"criticalCases={criticalCases.Count}"
            });
        }

        var quality = dataQuality.FirstOrDefault();
        if (quality is not null)
        {
            recommendations.Add(new AnalyticalRecommendationDto
            {
                RecommendationId = "REC-" + quality.IssueCode.ToUpperInvariant(),
                SourceFindingCode = quality.IssueCode,
                Priority = quality.Severity >= AnalyticalSeverity.High ? "high" : "medium",
                RecommendationText = quality.SuggestedCorrection,
                ResponsibleScope = "مالك البيانات",
                SuggestedDueDays = 10,
                EvidenceSummary = $"{quality.IssueCode}={quality.Count}"
            });
        }

        return recommendations
            .GroupBy(r => r.RecommendationId, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
    }

    private static string RecommendationText(string code) => code switch
    {
        "OVERDUE_RATE_INCREASED" => "مراجعة المعاملات المتأخرة حسب الإدارات الأعلى أثرًا ووضع إجراءات متابعة يومية حتى عودة النسبة للمستوى المستقر.",
        "BACKLOG_INCREASED" => "مراجعة الرصيد المفتوح وتحديد معاملات يمكن إغلاقها أو احالةها لمسؤول واضح خلال أسبوع.",
        "DEPARTMENT_BACKLOG_CONCENTRATION" => "مراجعة مسار الاحالة والعمل داخل الإدارة ذات أعلى تراكم وتحديد أسباب الانتظار الموثقة.",
        "EXTERNAL_PENDING_RESPONSES" => "متابعة المعاملات المنتظرة من الجهة وفق قائمة الحالات المرفقة دون نسبة التأخر إليها إلا بدليل تشغيلي.",
        "DATA_QUALITY_ISSUE" => "استكمال حقول جودة البيانات المحددة في قسم جودة البيانات لضمان دقة المؤشرات القادمة.",
        _ => "مراجعة النتيجة المرتبطة واتخاذ إجراء تشغيلي موثق."
    };

    private static string ResponsibleScope(string? value) =>
        ReportDepartmentNameNormalizer.DataQualityOwnerOrDepartment(value);

    private static List<TimeSeriesPointDto> BuildTimeSeries(IReadOnlyList<TransactionReportSnapshot> snapshots, ReportTimeGrouping grouping)
    {
        return snapshots
            .GroupBy(s => PeriodStart(s.IncomingDate, grouping))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var incoming = g.Count();
                var closed = g.Count(s => s.IsClosed);
                var overdue = g.Count(s => s.IsOverdue);
                return new TimeSeriesPointDto
                {
                    PeriodStart = g.Key,
                    PeriodLabel = PeriodLabel(g.Key, grouping),
                    Incoming = incoming,
                    Closed = closed,
                    OpenBalance = g.Count(s => s.IsOpen),
                    Overdue = overdue,
                    OnTimeRate = CalculateOnTimeRate(g),
                    AverageCompletionDays = Average(CompletionDays(g)),
                    BacklogGrowth = incoming - closed
                };
            })
            .ToList();
    }

    /// <summary>
    /// Departmental breakdown of BuildTimeSeries: same IncomingDate/grouping basis, further
    /// split by ResponsibleDepartment so each transaction is counted under its single
    /// responsible department per period — never duplicated across joint departments.
    /// </summary>
    private static List<DepartmentTimeSeriesPointDto> BuildDepartmentTimeSeries(
        IReadOnlyList<TransactionReportSnapshot> snapshots,
        ReportTimeGrouping grouping)
    {
        return snapshots
            .GroupBy(s => PeriodStart(s.IncomingDate, grouping))
            .SelectMany(periodGroup => periodGroup
                .GroupBy(s => new { s.ResponsibleDepartmentId, Name = BlankToUnknown(s.ResponsibleDepartment) })
                .Select(deptGroup =>
                {
                    var incoming = deptGroup.Count();
                    var closed = deptGroup.Count(s => s.IsClosed);
                    return new DepartmentTimeSeriesPointDto
                    {
                        DepartmentId = deptGroup.Key.ResponsibleDepartmentId,
                        DepartmentName = deptGroup.Key.Name,
                        PeriodStart = periodGroup.Key,
                        PeriodLabel = PeriodLabel(periodGroup.Key, grouping),
                        IncomingCount = incoming,
                        ClosedCount = closed,
                        OpenCount = deptGroup.Count(s => s.IsOpen),
                        OverdueCount = deptGroup.Count(s => s.IsOverdue),
                        OnTimeCompletionRate = CalculateOnTimeRate(deptGroup),
                        AverageCompletionDays = Average(CompletionDays(deptGroup)),
                        PendingAssignments = deptGroup.Sum(s => s.PendingReplyAssignmentCount),
                        PartialReplies = deptGroup.Count(s => s.IsPartialReply),
                        BacklogGrowth = incoming - closed
                    };
                }))
            .OrderBy(p => p.PeriodStart)
            .ThenByDescending(p => p.OverdueCount)
            .ThenByDescending(p => p.OpenCount)
            .ThenByDescending(p => p.IncomingCount)
            .ToList();
    }

    private static MethodologyDto BuildMethodology(
        ReportMetadataDto metadata,
        ReportFiltersDto filters,
        ReportBuildRequestDto request,
        ReportBuildRequestDto? comparisonRequest,
        IReadOnlyList<TransactionReportSnapshot> previousSnapshots,
        int detailLimit,
        bool detailRowsTruncated,
        ReportComparisonMode comparisonMode)
    {
        var deferred = new List<string>
        {
            "AverageFirstActionHours: يحتاج حدث أول إجراء موثوق.",
            "ReopenedTransactionsRate: لا توجد حالة إعادة فتح مستقلة.",
            "Outgoing external-party causality: بيانات الجهات الصادرة غير مكتملة الاستخدام.",
            "AverageResponseDays (تقديري): لا يوجد حقل ResponseCompletedAt مستقل في النموذج الحالي. " +
            "يُستخدم ClosedAt كبديل تقريبي للمعاملات ذات ResponseCompleted=true. " +
            "التحسين المستقبلي يتطلب حقل ResponseCompletedAt أو مصدر رد مكتمل من التكليفات.",
            "التحليل الزمني حسب الإدارة يعتمد على تاريخ الوارد وتجميع المعاملة تحت الإدارة المسؤولة الأساسية الحالية، " +
            "ولا يكرر المعاملة على كل الإدارات المشاركة.",
        };
        if (detailRowsTruncated)
            deferred.Add("Detail rows truncated: بعض الجداول التفصيلية محدودة حسب إعدادات التصدير.");

        return new MethodologyDto
        {
            ReportName = metadata.Title,
            ReportVersion = InstitutionalReportStyles.TemplateVersion,
            GeneratedAtUtc = metadata.GeneratedAt,
            DataPeriod = $"{metadata.PeriodFrom:yyyy-MM-dd} إلى {metadata.PeriodTo:yyyy-MM-dd}",
            PeriodBasis = BuildPeriodBasisNote(request.ReportType, filters.IncludeOverdue),
            ComparisonPeriod = BuildComparisonPeriodLabel(request, comparisonRequest, previousSnapshots, comparisonMode),
            Filters = BuildFilterSummary(filters),
            RowLimits = $"DetailLimit={detailLimit:N0}; Truncated={(detailRowsTruncated ? "yes" : "no")}",
            DeferredMetrics = deferred
        };
    }

    /// <summary>Documents which date field the report's period actually filters on, per the report type.</summary>
    private static string BuildPeriodBasisNote(InstitutionalReportType reportType, bool includeOverdue)
    {
        if (reportType == InstitutionalReportType.OverdueTransactions)
        {
            return "يعرض التقرير المعاملات المتأخرة حتى تاريخ نهاية الفترة (تاريخ التقييم)، ويشمل المفتوحة المتأخرة والمنجزة/المغلقة بعد الاستحقاق، " +
                   "بغض النظر عن كون تاريخ الوارد قبل بداية الفترة، مع استبعاد المعاملات ذات تاريخ وارد بعد نهاية الفترة. " +
                   "أيام التأخير محسوبة حتى تاريخ نهاية الفترة نفسه.";
        }

        var basis = "الفترة الزمنية مبنية على تاريخ الوارد.";
        if (includeOverdue)
        {
            basis += " فلتر «تضمين المتأخرات» يعرض المعاملات المتأخرة ضمن نطاق تاريخ الوارد المحدد فقط، " +
                     "وليس كل المعاملات المتأخرة في النظام (استخدم تقرير المعاملات المتأخرة لذلك).";
        }

        return basis;
    }

    private static string BuildComparisonPeriodLabel(
        ReportBuildRequestDto request,
        ReportBuildRequestDto? comparisonRequest,
        IReadOnlyList<TransactionReportSnapshot> previousSnapshots,
        ReportComparisonMode comparisonMode)
    {
        if (comparisonMode == ReportComparisonMode.None || comparisonRequest is null)
            return "لا توجد مقارنة";

        var from = comparisonRequest.Filters.DateFrom;
        var to = comparisonRequest.Filters.DateTo;
        var period = $"{from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";
        return previousSnapshots.Count == 0
            ? $"{period} (فترة مقارنة صالحة بلا معاملات مطابقة)"
            : period;
    }

    private static string BuildFilterSummary(ReportFiltersDto filters)
    {
        var parts = new List<string>();
        if (filters.DateFrom.HasValue || filters.DateTo.HasValue)
            parts.Add($"الفترة={filters.DateFrom:yyyy-MM-dd}..{filters.DateTo:yyyy-MM-dd}");
        if (filters.DepartmentIds.Count > 0)
            parts.Add($"departments={filters.DepartmentIds.Count}");
        if (filters.PartyIds.Count > 0)
            parts.Add($"parties={filters.PartyIds.Count}");
        if (filters.CategoryIds.Count > 0)
            parts.Add($"categories={filters.CategoryIds.Count}");
        if (filters.Priorities.Count > 0)
            parts.Add($"priorities={filters.Priorities.Count}");
        if (filters.Statuses.Count > 0)
            parts.Add($"statuses={filters.Statuses.Count}");
        if (!string.IsNullOrWhiteSpace(filters.Search))
            parts.Add("search=applied");
        return parts.Count == 0 ? "بدون فلاتر إضافية" : string.Join("; ", parts);
    }

    private static DateTime? ResolveComparisonFrom(ReportBuildRequestDto request) =>
        request.ComparisonMode == ReportComparisonMode.Custom
            ? request.ComparisonDateFrom
            : CreateComparisonRequest(request)?.Filters.DateFrom;

    private static DateTime? ResolveComparisonTo(ReportBuildRequestDto request) =>
        request.ComparisonMode == ReportComparisonMode.Custom
            ? request.ComparisonDateTo
            : CreateComparisonRequest(request)?.Filters.DateTo;

    private static IEnumerable<int> CompletionDays(IEnumerable<TransactionReportSnapshot> snapshots) =>
        snapshots
            .Where(s => s.IsClosed && s.ClosedAt.HasValue)
            .Select(s => Math.Max(0, (s.ClosedAt!.Value.Date - s.IncomingDate.Date).Days));

    private static IEnumerable<int> ResponseDays(IEnumerable<TransactionReportSnapshot> snapshots) =>
        snapshots
            .Where(s => s.ResponseCompleted && s.ClosedAt.HasValue)
            .Select(s => Math.Max(0, (s.ClosedAt!.Value.Date - s.IncomingDate.Date).Days));

    private static double Average(IEnumerable<int> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0 : Math.Round(list.Average(), 1);
    }

    private static double CalculateOnTimeRate(IEnumerable<TransactionReportSnapshot> snapshots)
    {
        var measurable = snapshots.Where(s => s.IsClosed && s.ClosedAt.HasValue && s.ResponseDueDate.HasValue).ToList();
        if (measurable.Count == 0)
            return 0;
        var onTime = measurable.Count(s => s.ClosedAt!.Value.Date <= s.ResponseDueDate!.Value.Date);
        return Math.Round(onTime * 100.0 / measurable.Count, 1);
    }

    private static double CalculateCompletenessRate(IReadOnlyList<TransactionReportSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
            return 100;

        var expected = 0;
        var present = 0;
        foreach (var snapshot in snapshots)
        {
            CountField(!string.IsNullOrWhiteSpace(snapshot.IncomingNumber));
            CountField(!string.IsNullOrWhiteSpace(snapshot.Subject));
            CountField(!string.IsNullOrWhiteSpace(snapshot.CategoryName));
            CountField(!string.IsNullOrWhiteSpace(snapshot.IncomingParty));
            CountField(!string.IsNullOrWhiteSpace(snapshot.ResponsibleDepartment));
            CountField(Enum.IsDefined(snapshot.Priority));
            if (snapshot.RequiresResponse)
                CountField(snapshot.ResponseDueDate.HasValue || snapshot.EarliestPendingReplyDueDate.HasValue);
        }

        return Math.Round(present * 100.0 / expected, 1);

        void CountField(bool isPresent)
        {
            expected++;
            if (isPresent)
                present++;
        }
    }

    private static decimal Rate(int numerator, int denominator) =>
        denominator == 0 ? 0 : Math.Round(numerator * 100m / denominator, 1);

    private static string FormatValue(decimal value, string unit, string format)
    {
        if (format == MetricValueTypes.Percent)
            return $"{value:N1}%";

        return string.IsNullOrWhiteSpace(unit)
            ? value.ToString("N0", CultureInfo.InvariantCulture)
            : $"{value:N1} {unit}";
    }

    private static string ResolveSystemComparison(double average, double systemAverageCompletion)
    {
        if (Math.Abs(average) < double.Epsilon || Math.Abs(systemAverageCompletion) < double.Epsilon)
            return "غير قابل للمقارنة";

        return average <= systemAverageCompletion
            ? "أفضل من متوسط النظام"
            : "أعلى من متوسط النظام";
    }

    private static int ResolvePositive(int? requested, int configuredMaximum)
    {
        var resolved = requested.HasValue && requested.Value > 0 ? requested.Value : configuredMaximum;
        return Math.Min(resolved, configuredMaximum);
    }

    private static string BlankToUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "غير محدد" : value.Trim();

    private static DateTime PeriodStart(DateTime value, ReportTimeGrouping grouping)
    {
        var date = value.Date;
        return grouping switch
        {
            ReportTimeGrouping.Daily => date,
            ReportTimeGrouping.Weekly => date.AddDays(-(int)date.DayOfWeek),
            ReportTimeGrouping.Quarterly => new DateTime(date.Year, ((date.Month - 1) / 3 * 3) + 1, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc)
        };
    }

    private static string PeriodLabel(DateTime value, ReportTimeGrouping grouping) =>
        grouping switch
        {
            ReportTimeGrouping.Daily => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ReportTimeGrouping.Weekly => "أسبوع " + value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ReportTimeGrouping.Quarterly => $"{value.Year} Q{((value.Month - 1) / 3) + 1}",
            _ => value.ToString("yyyy-MM", CultureInfo.InvariantCulture)
        };

    private static string PriorityLabel(Priority priority) => priority switch
    {
        Priority.VeryUrgent => "عاجلة جدًا",
        Priority.Urgent => "عاجلة",
        Priority.Normal => "عادية",
        _ => priority.ToString()
    };
}

internal static class InstitutionalReportStatistics
{
    internal static double Median(IEnumerable<int> values) => Percentile(values, 50);

    internal static double P75(IEnumerable<int> values) => Percentile(values, 75);

    internal static double P90(IEnumerable<int> values) => Percentile(values, 90);

    private static double Percentile(IEnumerable<int> values, double percentile)
    {
        var ordered = values.Order().ToList();
        if (ordered.Count == 0)
            return 0;

        if (ordered.Count == 1)
            return ordered[0];

        var position = (ordered.Count - 1) * percentile / 100d;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return ordered[lower];

        var weight = position - lower;
        return Math.Round(ordered[lower] * (1 - weight) + ordered[upper] * weight, 1);
    }
}
