using System.Globalization;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Reporting.Rendering;

namespace Uqeb.Api.Reporting.Services;

internal static class InstitutionalReportAnalysisService
{
    private const string NotAvailable = "غير متاح من البيانات الحالية";

    internal static InstitutionalReportAnalysisResult Build(
        ReportBuildRequestDto request,
        ReportMetadataDto metadata,
        ReportFiltersDto filters,
        InstitutionalMetricsResult currentMetrics,
        IReadOnlyList<TransactionReportSnapshot> currentSnapshots,
        InstitutionalMetricsResult? previousMetrics,
        IReadOnlyList<TransactionReportSnapshot> previousSnapshots,
        ReportingAnalysisOptions options,
        int detailLimit,
        bool detailRowsTruncated)
    {
        var contentLevel = request.ContentLevel ?? ReportContentLevel.Analytical;
        var comparisonMode = request.IncludeComparison == false
            ? ReportComparisonMode.None
            : request.ComparisonMode ?? ReportComparisonMode.PreviousEquivalentPeriod;
        var timeGrouping = request.TimeGrouping ?? ReportTimeGrouping.Monthly;
        var maxFindings = ResolvePositive(request.MaxFindings, options.MaxExecutiveFindings);
        var maxCriticalCases = ResolvePositive(request.MaxCriticalCases, options.MaxExecutiveCriticalCases);
        var maxRecommendations = ResolvePositive(request.MaxRecommendations, options.MaxRecommendations);

        var kpis = BuildKpis(currentMetrics, currentSnapshots, previousMetrics, options);
        var criticalCases = request.IncludeCriticalCases == false
            ? []
            : BuildCriticalCases(currentSnapshots, options).Take(maxCriticalCases).ToList();
        var departments = request.IncludeDepartmentPerformance == false
            ? []
            : BuildDepartments(currentSnapshots, previousSnapshots, options);
        var externalParties = request.IncludeExternalPartyAnalysis == false
            ? []
            : BuildExternalParties(currentSnapshots);
        var categories = request.IncludeCategoryAnalysis == false
            ? []
            : BuildCategories(currentSnapshots);
        var priorities = request.IncludeCategoryAnalysis == false
            ? []
            : BuildPriorities(currentSnapshots);
        var bottlenecks = request.IncludeBottleneckAnalysis == false
            ? []
            : BuildBottlenecks(currentSnapshots);
        var dataQuality = request.IncludeDataQuality == false
            ? []
            : BuildDataQualityIssues(currentSnapshots);
        var completenessRate = CalculateCompletenessRate(currentSnapshots);
        var findings = BuildFindings(currentMetrics, previousMetrics, departments, externalParties, dataQuality, options)
            .Take(maxFindings)
            .ToList();
        var recommendations = request.IncludeRecommendations == false
            ? []
            : BuildRecommendations(findings, criticalCases, dataQuality).Take(maxRecommendations).ToList();
        var timeSeries = request.IncludeTimeTrends == false
            ? []
            : BuildTimeSeries(currentSnapshots, timeGrouping);
        var insights = request.IncludeExecutiveSummary == false
            ? []
            : BuildExecutiveInsights(currentMetrics, kpis, findings, criticalCases, departments, externalParties, options)
                .Take(options.MaxExecutiveFindings)
                .ToList();

        return new InstitutionalReportAnalysisResult
        {
            ContentLevel = contentLevel,
            ComparisonMode = comparisonMode,
            ComparisonFrom = ResolveComparisonFrom(request, metadata),
            ComparisonTo = ResolveComparisonTo(request, metadata),
            Kpis = kpis,
            ExecutiveInsights = insights,
            Findings = findings,
            CriticalCases = criticalCases,
            TimeSeries = timeSeries,
            DepartmentPerformance = departments,
            ExternalParties = externalParties,
            Categories = categories,
            Priorities = priorities,
            Bottlenecks = bottlenecks,
            DataQualityIssues = dataQuality,
            DataCompletenessRate = completenessRate,
            Recommendations = recommendations,
            Methodology = BuildMethodology(metadata, filters, request, detailLimit, detailRowsTruncated, comparisonMode)
        };
    }

    internal static ReportBuildRequestDto? CreateComparisonRequest(ReportBuildRequestDto request)
    {
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
            return null;
        }

        if (!from.HasValue || !to.HasValue || from.Value > to.Value)
            return null;

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
            Filters = new ReportFiltersDto
            {
                DateFrom = from,
                DateTo = to,
                DepartmentIds = request.Filters.DepartmentIds.ToList(),
                PartyIds = request.Filters.PartyIds.ToList(),
                CategoryIds = request.Filters.CategoryIds.ToList(),
                Priorities = request.Filters.Priorities.ToList(),
                Statuses = request.Filters.Statuses.ToList(),
                IncludeJointDepartmentTransactions = request.Filters.IncludeJointDepartmentTransactions,
                IncludeOverdue = request.Filters.IncludeOverdue,
                IncludeDetails = request.Filters.IncludeDetails,
                IncludeRisks = request.Filters.IncludeRisks,
                IncludeRecommendations = request.Filters.IncludeRecommendations,
                Search = request.Filters.Search
            }
        };
    }

    private static List<AnalyticalKpiDto> BuildKpis(
        InstitutionalMetricsResult current,
        IReadOnlyList<TransactionReportSnapshot> currentSnapshots,
        InstitutionalMetricsResult? previous,
        ReportingAnalysisOptions options)
    {
        var open = currentSnapshots.Where(s => s.IsOpen).ToList();
        var closedDurations = CompletionDays(currentSnapshots).ToList();
        var responseDurations = ResponseDays(currentSnapshots).ToList();
        var stale = open.Count(s => DaysSinceLastAction(s) >= options.StaleTransactionDays);
        var oldestOpen = open.Count == 0 ? 0 : open.Max(s => s.ElapsedDays);
        var pendingAssignments = currentSnapshots.Sum(s => s.PendingReplyAssignmentCount);
        var completionMedian = InstitutionalReportStatistics.Median(closedDurations);
        var responseAverage = responseDurations.Count == 0 ? 0 : Math.Round(responseDurations.Average(), 1);
        var responseMedian = InstitutionalReportStatistics.Median(responseDurations);
        var followUpAverage = current.TotalTransactions == 0
            ? 0
            : Math.Round(currentSnapshots.Count(s => s.LastFollowUpDate.HasValue) / (double)current.TotalTransactions, 2);
        var completeness = CalculateCompletenessRate(currentSnapshots);
        var previousOpen = previous?.OpenCount ?? 0;
        var previousTotal = previous?.TotalTransactions ?? 0;
        var closureToIncomingRatio = current.TotalTransactions == 0
            ? 0
            : Math.Round(current.ClosedCount * 100.0 / current.TotalTransactions, 1);
        var backlogGrowth = current.OpenCount - previousOpen;

        return
        [
            Kpi("TotalTransactions", "إجمالي المعاملات", "عدد المعاملات الفريدة المطابقة للفلاتر.", "count(distinct TransactionId)", "Transaction.Id", current.TotalTransactions, "معاملة", "number", current.TotalTransactions, 1, KpiDirection.Neutral, previous?.TotalTransactions, options),
            Kpi("IncomingTransactions", "المعاملات الواردة", "عدد المعاملات حسب تاريخ الوارد في الفترة.", "count by IncomingDate", "IncomingDate", current.TotalTransactions, "معاملة", "number", current.TotalTransactions, 1, KpiDirection.Neutral, previousTotal, options),
            Kpi("ClosedTransactions", "المعاملات المغلقة", "عدد المعاملات ذات الحالة المغلقة.", "count(IsClosed)", "Status, ClosedAt", current.ClosedCount, "معاملة", "number", current.ClosedCount, 1, KpiDirection.HigherIsBetter, previous?.ClosedCount, options),
            Kpi("OpenTransactions", "المعاملات المفتوحة", "عدد المعاملات التشغيلية غير المغلقة.", "count(IsOpen)", "Status", current.OpenCount, "معاملة", "number", current.OpenCount, 1, KpiDirection.LowerIsBetter, previous?.OpenCount, options),
            Kpi("OnTimeCompletionRate", "نسبة الإنجاز ضمن المهلة", "نسبة المعاملات المغلقة قبل أو في تاريخ المهلة.", "on-time closed / measurable closed", "ClosedAt, ResponseDueDate", (decimal)current.OnTimeCompletionRate, "%", "percent", current.ClosedCount, options.MinimumComparisonSampleSize, KpiDirection.HigherIsBetter, previous is null ? null : (decimal)previous.OnTimeCompletionRate, options),
            Kpi("OverdueRate", "نسبة التأخر", "نسبة المعاملات المفتوحة المتأخرة من إجمالي المعاملات.", "overdue open / total", "ResponseDueDate, AssignmentDueDate, Status", Rate(current.OverdueCount, current.TotalTransactions), "%", "percent", current.TotalTransactions, options.MinimumComparisonSampleSize, KpiDirection.LowerIsBetter, previous is null ? null : Rate(previous.OverdueCount, previous.TotalTransactions), options),
            Kpi("AverageCompletionDays", "متوسط مدة الإنجاز", "متوسط الأيام بين الوارد والإغلاق للمعاملات المغلقة.", "avg(ClosedAt - IncomingDate)", "IncomingDate, ClosedAt", (decimal)current.AverageCompletionDays, "يوم", "decimal", closedDurations.Count, options.MinimumComparisonSampleSize, KpiDirection.LowerIsBetter, previous is null ? null : (decimal)previous.AverageCompletionDays, options),
            Kpi("MedianCompletionDays", "وسيط مدة الإنجاز", "وسيط أيام الإنجاز لتقليل أثر القيم الشاذة.", "median(ClosedAt - IncomingDate)", "IncomingDate, ClosedAt", (decimal)completionMedian, "يوم", "decimal", closedDurations.Count, options.MinimumComparisonSampleSize, KpiDirection.LowerIsBetter, null, options),
            Kpi("AverageResponseDays", "متوسط زمن الرد", "متوسط الأيام بين الوارد وتاريخ إكمال الرد عند توفره.", "avg(ResponseCompletedDate proxy via ClosedAt when response completed)", "IncomingDate, ClosedAt, ResponseCompleted", (decimal)responseAverage, "يوم", "decimal", responseDurations.Count, options.MinimumComparisonSampleSize, KpiDirection.LowerIsBetter, null, options),
            Kpi("PendingAssignmentsCount", "الإفادات المعلقة", "عدد الإفادات/التكليفات المطلوبة ولم ترد.", "sum(PendingReplyAssignmentCount)", "Assignments", pendingAssignments, "إفادة", "number", current.TotalTransactions, 1, KpiDirection.LowerIsBetter, null, options),
            Kpi("PartialRepliesCount", "الردود الجزئية", "عدد المعاملات المفتوحة ذات رد جزئي.", "count(IsPartialReply)", "Assignments, Status", current.PartialResponseCount, "معاملة", "number", current.TotalTransactions, 1, KpiDirection.LowerIsBetter, previous?.PartialResponseCount, options),
            Kpi("ResponseRequiredOpenCount", "مفتوحة تتطلب ردًا", "عدد المعاملات المفتوحة التي تتطلب ردًا.", "count(IsOpen && RequiresResponse)", "RequiresResponse, Status", open.Count(s => s.RequiresResponse), "معاملة", "number", open.Count, 1, KpiDirection.LowerIsBetter, null, options),
            Kpi("StaleTransactionsCount", "معاملات بلا تحديث حديث", $"معاملات مفتوحة بلا تحديث أو متابعة منذ {options.StaleTransactionDays} أيام فأكثر.", "last action age >= threshold", "UpdatedAt, CreatedAt, LastFollowUpDate", stale, "معاملة", "number", open.Count, 1, KpiDirection.LowerIsBetter, null, options),
            Kpi("BacklogGrowthRate", "تغير الرصيد المفتوح", "الفرق بين الرصيد المفتوح الحالي والسابق.", "current open - previous open", "Status", backlogGrowth, "معاملة", "number", current.TotalTransactions, options.MinimumComparisonSampleSize, KpiDirection.LowerIsBetter, previousOpen, options),
            Kpi("ClosureToIncomingRatio", "نسبة الإغلاق إلى الوارد", "نسبة المغلق إلى إجمالي الوارد في الفترة.", "closed / incoming", "Status, IncomingDate", (decimal)closureToIncomingRatio, "%", "percent", current.TotalTransactions, options.MinimumComparisonSampleSize, KpiDirection.HigherIsBetter, previous is null ? null : Rate(previous.ClosedCount, previous.TotalTransactions), options),
            Kpi("OldestOpenTransactionAgeDays", "عمر أقدم معاملة مفتوحة", "أكبر عمر بالأيام بين المعاملات المفتوحة.", "max(ElapsedDays where IsOpen)", "IncomingDate, Status", oldestOpen, "يوم", "number", open.Count, 1, KpiDirection.LowerIsBetter, null, options),
            Kpi("AverageFollowUpsPerTransaction", "متوسط المتابعات لكل معاملة", "مؤشر تقريبي يعتمد على وجود آخر متابعة؛ لا يحسب كل أحداث المتابعة.", "transactions with follow-up / total", "LastFollowUpDate", (decimal)followUpAverage, "متابعة", "decimal", current.TotalTransactions, 1, KpiDirection.Neutral, null, options),
            Kpi("DataCompletenessRate", "نسبة اكتمال البيانات", "نسبة اكتمال الحقول التشغيلية المطلوبة والشرطية.", "completed required fields / expected fields", "category, party, department, due date, priority", (decimal)completeness, "%", "percent", current.TotalTransactions, 1, KpiDirection.HigherIsBetter, null, options),
            UnavailableKpi("AverageFirstActionHours", "متوسط ساعات أول إجراء", "لا توجد علامة زمنية موثوقة لأول إجراء في snapshot الحالي."),
            UnavailableKpi("ReopenedTransactionsRate", "نسبة إعادة الفتح", "لا توجد حالة أو حدث إعادة فتح مستقل في بيانات التقرير الحالية.")
        ];
    }

    private static AnalyticalKpiDto Kpi(
        string key,
        string title,
        string definition,
        string formula,
        string fields,
        decimal value,
        string unit,
        string format,
        int sampleSize,
        int minimumSampleSize,
        KpiDirection direction,
        decimal? previousValue,
        ReportingAnalysisOptions options)
    {
        return new AnalyticalKpiDto
        {
            Key = key,
            Title = title,
            Definition = definition,
            Formula = formula,
            FieldsUsed = fields,
            NumericValue = value,
            DisplayValue = FormatValue(value, unit, format),
            Unit = unit,
            Format = format,
            SampleSize = sampleSize,
            MinimumSampleSize = minimumSampleSize,
            Direction = direction,
            Comparison = Compare(value, previousValue, direction, options)
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
        var classification = !percent.HasValue
            ? "not_comparable"
            : absPercent < options.StableChangeThresholdPercent
                ? "stable"
                : absPercent >= options.SignificantChangeThresholdPercent
                    ? "significant"
                    : "moderate";
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

    private static List<ExecutiveInsightDto> BuildExecutiveInsights(
        InstitutionalMetricsResult metrics,
        IReadOnlyList<AnalyticalKpiDto> kpis,
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
                Text = $"توجد {metrics.OverdueCount:N0} معاملة متأخرة تمثل {overdueRate:N1}% من نطاق التقرير.",
                Evidence = $"overdue={metrics.OverdueCount};rate={overdueRate}",
                Severity = overdueRate >= 20 ? AnalyticalSeverity.High : AnalyticalSeverity.Medium
            });
        }

        var topDepartment = departments.OrderByDescending(d => d.OverdueCount).ThenByDescending(d => d.OpenCount).FirstOrDefault();
        if (topDepartment is not null && topDepartment.OverdueCount > 0)
        {
            insights.Add(new ExecutiveInsightDto
            {
                Code = "EXEC_DEPARTMENT_CONCENTRATION",
                Text = $"تركز أعلى عدد من حالات التأخر في {topDepartment.DepartmentName} بعدد {topDepartment.OverdueCount:N0} معاملة متأخرة.",
                Evidence = $"department={topDepartment.DepartmentName};overdue={topDepartment.OverdueCount}",
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

    private static List<CriticalCaseDto> BuildCriticalCases(IReadOnlyList<TransactionReportSnapshot> snapshots, ReportingAnalysisOptions options) =>
        snapshots
            .Where(s => s.IsOpen)
            .SelectMany(s => CriticalRules(s, options))
            .OrderByDescending(c => c.Severity)
            .ThenByDescending(c => c.DaysOverdue ?? c.AgeDays)
            .ThenBy(c => c.TransactionId)
            .ToList();

    private static IEnumerable<CriticalCaseDto> CriticalRules(TransactionReportSnapshot snapshot, ReportingAnalysisOptions options)
    {
        if (snapshot.Priority is Priority.VeryUrgent or Priority.Urgent && snapshot.IsOverdue)
            yield return CriticalCase(snapshot, "CRITICAL_PRIORITY_OVERDUE", "أولوية عالية متأخرة", "مراجعة المعاملة المتأخرة فورًا", AnalyticalSeverity.Critical);

        if (snapshot.IsOverdue && snapshot.ElapsedDays >= options.CriticalOverdueDays)
            yield return CriticalCase(snapshot, "OPEN_OLDER_THAN_THRESHOLD", $"معاملة مفتوحة متأخرة لأكثر من {options.CriticalOverdueDays} أيام", "تصعيد المعاملة المتأخرة", AnalyticalSeverity.High);

        if (snapshot.PendingReplyAssignmentCount > 0 && snapshot.EarliestPendingReplyDueDate.HasValue && snapshot.EarliestPendingReplyDueDate.Value.Date < DateTime.UtcNow.Date)
            yield return CriticalCase(snapshot, "PENDING_ASSIGNMENT_OVERDUE", "إفادة إدارة معلقة بعد تاريخ الاستحقاق", "طلب الإفادة الناقصة", AnalyticalSeverity.High);

        if (snapshot.RequiresResponse && !snapshot.ResponseCompleted && snapshot.ResponseDueDate.HasValue && snapshot.ResponseDueDate.Value.Date < DateTime.UtcNow.Date)
            yield return CriticalCase(snapshot, "RESPONSE_REQUIRED_OVERDUE", "معاملة تتطلب ردًا بعد تاريخ المهلة", "استكمال الرد المطلوب", AnalyticalSeverity.High);

        if (DaysSinceLastAction(snapshot) >= options.StaleTransactionDays)
            yield return CriticalCase(snapshot, "STALE_WITHOUT_UPDATE", $"لا يوجد تحديث حديث منذ {options.StaleTransactionDays} أيام أو أكثر", "تحديث حالة المعاملة أو تحديد إجراء تالٍ", AnalyticalSeverity.Medium);

        if (snapshot.IsPartialReply)
            yield return CriticalCase(snapshot, "PARTIAL_REPLY_PENDING", "ردود جزئية لم تكتمل", "استكمال الردود المتبقية", AnalyticalSeverity.Medium);
    }

    private static CriticalCaseDto CriticalCase(TransactionReportSnapshot snapshot, string code, string label, string action, AnalyticalSeverity severity) => new()
    {
        TransactionId = snapshot.TransactionId,
        IncomingNumber = snapshot.IncomingNumber,
        Subject = snapshot.Subject,
        Department = snapshot.ResponsibleDepartment,
        ExternalParty = snapshot.IncomingParty,
        Priority = PriorityLabel(snapshot.Priority),
        AgeDays = snapshot.ElapsedDays,
        DaysOverdue = CalculateDaysOverdue(snapshot),
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
                    SystemComparison = average == 0 || systemAverageCompletion == 0
                        ? "غير قابل للمقارنة"
                        : average <= systemAverageCompletion ? "أفضل من متوسط النظام" : "أعلى من متوسط النظام"
                };
            })
            .OrderByDescending(d => d.OverdueCount)
            .ThenByDescending(d => d.OpenCount)
            .ThenByDescending(d => d.SampleSize)
            .ToList();
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
                    OverdueResponseCount = pending.Count(s => s.IsOverdue),
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
                AverageAgeDays = g.Count() == 0 ? 0 : Math.Round(g.Average(s => s.ElapsedDays), 1),
                OnTimeRate = CalculateOnTimeRate(g)
            })
            .OrderByDescending(p => p.OverdueCount)
            .ThenByDescending(p => p.Count)
            .ToList();

    private static List<BottleneckRowDto> BuildBottlenecks(IReadOnlyList<TransactionReportSnapshot> snapshots)
    {
        var rows = snapshots.Where(s => s.IsOpen).Select(s => new { Snapshot = s, Reason = BottleneckReason(s) }).ToList();
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

    private static (string Code, string Label) BottleneckReason(TransactionReportSnapshot snapshot)
    {
        if (snapshot.PendingReplyAssignmentCount > 0)
            return ("pending_department_assignment", "إفادة أو تكليف إدارة معلق");
        if (snapshot.RequiresResponse && !snapshot.ResponseCompleted && snapshot.ResponseDueDate.HasValue)
            return ("external_response_delay", "معاملة منتظرة من الجهة");
        if (snapshot.IsPartialReply)
            return ("partial_response", "رد جزئي غير مكتمل");
        if (DaysSinceLastAction(snapshot) >= 7)
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
            ("missing_responsible_department", "إدارة مسؤولة مفقودة", s => string.IsNullOrWhiteSpace(s.ResponsibleDepartment), "Assignments/OutgoingDepartments", "تحديد الإدارة المسؤولة.", AnalyticalSeverity.High),
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
        if (topDepartment is not null && topDepartment.OverdueCount > 0 && !topDepartment.HasSmallSample)
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
                ResponsibleScope = string.IsNullOrWhiteSpace(finding.AffectedScope) ? "إدارة المتابعة" : finding.AffectedScope,
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
        "BACKLOG_INCREASED" => "مراجعة الرصيد المفتوح وتحديد معاملات يمكن إغلاقها أو تحويلها لمسؤول واضح خلال أسبوع.",
        "DEPARTMENT_BACKLOG_CONCENTRATION" => "مراجعة مسار التحويل والعمل داخل الإدارة ذات أعلى تراكم وتحديد أسباب الانتظار الموثقة.",
        "EXTERNAL_PENDING_RESPONSES" => "متابعة المعاملات المنتظرة من الجهة وفق قائمة الحالات المرفقة دون نسبة التأخر إليها إلا بدليل تشغيلي.",
        "DATA_QUALITY_ISSUE" => "استكمال حقول جودة البيانات المحددة في قسم جودة البيانات لضمان دقة المؤشرات القادمة.",
        _ => "مراجعة النتيجة المرتبطة واتخاذ إجراء تشغيلي موثق."
    };

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

    private static MethodologyDto BuildMethodology(
        ReportMetadataDto metadata,
        ReportFiltersDto filters,
        ReportBuildRequestDto request,
        int detailLimit,
        bool detailRowsTruncated,
        ReportComparisonMode comparisonMode)
    {
        var deferred = new List<string>
        {
            "AverageFirstActionHours: يحتاج حدث أول إجراء موثوق.",
            "ReopenedTransactionsRate: لا توجد حالة إعادة فتح مستقلة.",
            "Outgoing external-party causality: بيانات الجهات الصادرة غير مكتملة الاستخدام."
        };
        if (detailRowsTruncated)
            deferred.Add("Detail rows truncated: بعض الجداول التفصيلية محدودة حسب إعدادات التصدير.");

        return new MethodologyDto
        {
            ReportName = metadata.Title,
            ReportVersion = InstitutionalReportStyles.TemplateVersion,
            GeneratedAtUtc = metadata.GeneratedAt,
            DataPeriod = $"{metadata.PeriodFrom:yyyy-MM-dd} إلى {metadata.PeriodTo:yyyy-MM-dd}",
            ComparisonPeriod = comparisonMode == ReportComparisonMode.None
                ? "لا توجد مقارنة"
                : $"{ResolveComparisonFrom(request, metadata):yyyy-MM-dd} إلى {ResolveComparisonTo(request, metadata):yyyy-MM-dd}",
            Filters = BuildFilterSummary(filters),
            RowLimits = $"DetailLimit={detailLimit:N0}; Truncated={(detailRowsTruncated ? "yes" : "no")}",
            DeferredMetrics = deferred
        };
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

    private static DateTime? ResolveComparisonFrom(ReportBuildRequestDto request, ReportMetadataDto metadata) =>
        request.ComparisonMode == ReportComparisonMode.Custom
            ? request.ComparisonDateFrom
            : CreateComparisonRequest(request)?.Filters.DateFrom;

    private static DateTime? ResolveComparisonTo(ReportBuildRequestDto request, ReportMetadataDto metadata) =>
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

        return expected == 0 ? 100 : Math.Round(present * 100.0 / expected, 1);

        void CountField(bool isPresent)
        {
            expected++;
            if (isPresent)
                present++;
        }
    }

    private static decimal Rate(int numerator, int denominator) =>
        denominator == 0 ? 0 : Math.Round(numerator * 100m / denominator, 1);

    private static string FormatValue(decimal value, string unit, string format) =>
        format == "percent"
            ? $"{value:N1}%"
            : string.IsNullOrWhiteSpace(unit)
                ? value.ToString("N0", CultureInfo.InvariantCulture)
                : $"{value:N1} {unit}";

    private static int ResolvePositive(int? requested, int fallback) =>
        requested.HasValue && requested.Value > 0 ? requested.Value : fallback;

    private static string BlankToUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "غير محدد" : value.Trim();

    private static int DaysSinceLastAction(TransactionReportSnapshot snapshot)
    {
        var lastAction = new[] { snapshot.UpdatedAt, snapshot.LastFollowUpDate, snapshot.ClosedAt }
            .Where(d => d.HasValue)
            .Select(d => d!.Value.Date)
            .DefaultIfEmpty(snapshot.CreatedAt.Date)
            .Max();
        return Math.Max(0, (DateTime.UtcNow.Date - lastAction).Days);
    }

    private static int? CalculateDaysOverdue(TransactionReportSnapshot snapshot)
    {
        var dueDates = new[] { snapshot.ResponseDueDate, snapshot.EarliestPendingReplyDueDate }
            .Where(d => d.HasValue)
            .Select(d => d!.Value.Date)
            .ToList();
        if (dueDates.Count == 0)
            return snapshot.IsOverdue ? snapshot.ElapsedDays : null;
        return Math.Max(0, (DateTime.UtcNow.Date - dueDates.Min()).Days);
    }

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
