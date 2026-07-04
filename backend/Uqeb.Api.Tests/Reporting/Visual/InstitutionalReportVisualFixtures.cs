using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Services;

namespace Uqeb.Api.Tests.Reporting.Visual;

internal static class InstitutionalReportVisualFixtures
{
    internal static readonly DateTime FixedUtc = new(2026, 6, 15, 9, 30, 0, DateTimeKind.Utc);
    internal static readonly DateTime FixedIssueDate = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

    internal static InstitutionalReportModel CreateBaseModel(
        int totalMatched = 125,
        int exportedRows = 125,
        bool truncated = false,
        string title = "تقرير المتابعة الإجرائية للمعاملات") =>
        new()
        {
            TotalMatchedRows = totalMatched,
            ExportedDetailRows = exportedRows,
            DetailRowsTruncated = truncated,
            DetailPartsCount = truncated ? 3 : 1,
            Metadata = new ReportMetadataDto
            {
                ReportNumber = "REP-2026-000125",
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                ReportTypeName = "التقرير التنفيذي الشامل",
                Title = title,
                OrganizationName = "المتابعة الإجرائية",
                DepartmentName = "إدارة المتابعة والتقارير",
                IssueDate = FixedIssueDate,
                PeriodFrom = new DateTime(2026, 1, 1),
                PeriodTo = new DateTime(2026, 6, 15),
                GeneratedAt = FixedUtc,
                VerificationId = "VISUALFIX001",
                TotalMatchingTransactions = totalMatched,
                IncludedTransactionCount = exportedRows,
                DetailRowLimit = 500,
                TotalPages = 8,
            },
            Summary = new ExecutiveSummaryDto
            {
                KpiCards =
                [
                    new KpiCardDto { Key = "total", Title = "إجمالي المعاملات", Value = totalMatched.ToString("N0") },
                    new KpiCardDto { Key = "open", Title = "المفتوحة", Value = "42" },
                    new KpiCardDto { Key = "closed", Title = "المغلقة", Value = "83" },
                    new KpiCardDto { Key = "overdue", Title = "المتأخرة", Value = "11" },
                ],
                ExecutiveNarrative = "ملخص تنفيذي ثابت للاختبار البصري — KPI محسوبة من كامل النتائج.",
            },
            Charts =
            [
                new ChartDto
                {
                    Key = "openClosed",
                    Title = "المعاملات المفتوحة مقابل المغلقة",
                    ChartType = "bar",
                    Series =
                    [
                        new ChartSeriesPointDto { Label = "مفتوحة", Value = 42 },
                        new ChartSeriesPointDto { Label = "مغلقة", Value = 83 },
                    ],
                },
            ],
            DepartmentPerformance =
            [
                new DepartmentPerformanceRowDto
                {
                    DepartmentName = "الشؤون الإدارية",
                    TotalTransactions = 40,
                    ClosedCount = 25,
                    OpenCount = 15,
                    OverdueCount = 3,
                    AverageCompletionDays = 12.5,
                    OnTimeCompletionRate = 88.2,
                    Rating = DepartmentRatingLevel.Good,
                    RatingLabel = "جيد",
                },
            ],
            Risks =
            [
                new RiskAlertRowDto
                {
                    Sequence = 1,
                    Alert = "معاملة متأخرة: طلب صيانة",
                    DepartmentName = "الشؤون الإدارية",
                    Severity = RiskSeverity.High,
                    SeverityLabel = "مرتفع",
                    ElapsedDays = 45,
                    SuggestedAction = "متابعة فورية",
                },
            ],
            Recommendations =
            [
                new RecommendationRowDto
                {
                    Observation = "تراكم 11 معاملة متأخرة",
                    RequiredAction = "عقد اجتماع متابعة",
                    ResponsibleDepartment = "الشؤون الإدارية",
                    Priority = "عالية",
                    TargetDate = "2026-06-22",
                    Source = RecommendationSource.Automated,
                    SourceLabel = "مولّد آليًا",
                },
            ],
            Transactions = CreateTransactions(Math.Min(exportedRows, 24)),
            Analysis = CreateAnalysis(),
        };

    internal static List<TransactionDetailRowDto> CreateTransactions(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new TransactionDetailRowDto
            {
                Sequence = i,
                TransactionId = i,
                TrackingNumber = $"INT-{i:D4}",
                IncomingNumber = $"IN-{i:D4}",
                IncomingDate = FixedIssueDate.AddDays(-i),
                Subject = i == 1
                    ? "معاملة بعنوان طويل جدًا يختبر التفاف النص العربي دون قص أو خروج من الجدول في PDF والمعاينة"
                    : $"معاملة رقم {i}",
                IncomingParty = "جهة حكومية",
                ResponsibleDepartment = "الشؤون الإدارية",
                Priority = "عادي",
                Status = "مفتوحة",
                FollowUpStage = "بانتظار رد",
                ElapsedDays = i * 2,
                ResponseState = "بانتظار",
            })
            .ToList();

    internal static InstitutionalReportAnalysisResult CreateAnalysis() => new()
    {
        ContentLevel = ReportContentLevel.Analytical,
        ComparisonMode = ReportComparisonMode.PreviousEquivalentPeriod,
        ComparisonFrom = new DateTime(2025, 7, 19),
        ComparisonTo = new DateTime(2025, 12, 31),
        Kpis =
        [
            new AnalyticalKpiDto
            {
                Key = "TotalTransactions",
                Title = "إجمالي المعاملات",
                Definition = "عدد المعاملات الفريدة المطابقة للفلاتر.",
                Formula = "count(distinct TransactionId)",
                FieldsUsed = "Transaction.Id",
                DisplayValue = "125.0 معاملة",
                NumericValue = 125,
                Unit = "معاملة",
                Direction = KpiDirection.Neutral,
                SampleSize = 125,
                MinimumSampleSize = 1,
                Comparison = new KpiComparisonDto
                {
                    CurrentValue = 125,
                    PreviousValue = 100,
                    AbsoluteChange = 25,
                    PercentageChange = 25,
                    TrendDirection = TrendDirection.Improved,
                    TrendClassification = "significant",
                    ComparisonLabel = "+25 (+25%)",
                },
            },
            new AnalyticalKpiDto
            {
                Key = "OverdueRate",
                Title = "نسبة التأخر",
                Definition = "نسبة المعاملات المفتوحة المتأخرة.",
                Formula = "overdue / total",
                FieldsUsed = "ResponseDueDate, AssignmentDueDate, Status",
                DisplayValue = "8.8%",
                NumericValue = 8.8m,
                Unit = "%",
                Format = "percent",
                Direction = KpiDirection.LowerIsBetter,
                SampleSize = 125,
                MinimumSampleSize = 10,
            },
        ],
        ExecutiveInsights = [],
        Findings =
        [
            new SignificantFindingDto
            {
                Code = "OVERDUE_RATE_INCREASED",
                Title = "ارتفاع نسبة التأخر",
                Description = "ارتفعت نسبة التأخر مقارنة بالفترة السابقة.",
                Evidence = "current=8.8;previous=5.1",
                Severity = AnalyticalSeverity.High,
                CurrentValue = 8.8m,
                PreviousValue = 5.1m,
                AffectedScope = "التقرير",
            },
        ],
        CriticalCases =
        [
            new CriticalCaseDto
            {
                TransactionId = 1,
                IncomingNumber = "IN-0001",
                Subject = "معاملة حرجة متأخرة",
                Department = "الشؤون الإدارية",
                ExternalParty = "جهة حكومية",
                Priority = "عاجلة جدًا",
                AgeDays = 45,
                DaysOverdue = 12,
                ReasonCode = "CRITICAL_PRIORITY_OVERDUE",
                ReasonLabel = "أولوية عالية متأخرة",
                RequiredAction = "مراجعة المعاملة المتأخرة فورًا",
                SuggestedOwner = "الشؤون الإدارية",
                Severity = AnalyticalSeverity.Critical,
            },
        ],
        TimeSeries =
        [
            new TimeSeriesPointDto { PeriodLabel = "2026-06", PeriodStart = new DateTime(2026, 6, 1), Incoming = 125, Closed = 83, OpenBalance = 42, Overdue = 11, OnTimeRate = 88.2, AverageCompletionDays = 12.5, BacklogGrowth = 42 },
        ],
        DepartmentTimeSeries =
        [
            new DepartmentTimeSeriesPointDto { DepartmentId = 1, DepartmentName = "الشؤون الإدارية", PeriodStart = new DateTime(2026, 6, 1), PeriodLabel = "2026-06", IncomingCount = 40, ClosedCount = 25, OpenCount = 15, OverdueCount = 3, OnTimeCompletionRate = 88.2, AverageCompletionDays = 12.5, PendingAssignments = 4, PartialReplies = 2, BacklogGrowth = 15 },
        ],
        DepartmentPerformance =
        [
            new DepartmentAnalysisRowDto { DepartmentName = "الشؤون الإدارية", IncomingCount = 40, ClosedCount = 25, OpenCount = 15, OverdueCount = 3, OnTimeCompletionRate = 88.2, AverageCompletionDays = 12.5, MedianCompletionDays = 11, PendingAssignments = 4, PartialReplies = 2, OldestOpenAgeDays = 45, DataCompletenessRate = 94, SampleSize = 40, SystemComparison = "أفضل من متوسط النظام" },
        ],
        ExternalParties =
        [
            new ExternalPartyAnalysisRowDto { ExternalPartyName = "جهة حكومية", IncomingCount = 50, PendingResponseCount = 7, OverdueResponseCount = 2, AverageResponseDays = 9.5, MedianResponseDays = 8, FollowUpCount = 4, OldestPendingResponseDays = 28, TopCategories = "صيانة" },
        ],
        Categories =
        [
            new CategoryAnalysisRowDto { CategoryName = "صيانة", TransactionCount = 40, OpenCount = 12, OverdueCount = 4, OnTimeCompletionRate = 80, AverageCompletionDays = 13.4, PendingAssignments = 3 },
        ],
        Priorities =
        [
            new PriorityAnalysisRowDto { Priority = "عاجلة", Count = 20, OpenCount = 8, OverdueCount = 3, AverageAgeDays = 19, OnTimeRate = 75 },
        ],
        Bottlenecks =
        [
            new BottleneckRowDto { ReasonCode = "pending_department_assignment", ReasonLabel = "إفادة أو تكليف إدارة معلق", Count = 9, SharePercent = 21.4, AverageDelayDays = 18.3, TopDepartments = "الشؤون الإدارية", TopExternalParties = "جهة حكومية", ExampleTransactionIds = [1, 2, 3] },
        ],
        DataQualityIssues =
        [
            new DataQualityIssueDto { IssueCode = "missing_due_date", Label = "مهلة رد مفقودة", Count = 5, SharePercent = 4, Severity = AnalyticalSeverity.Medium, AffectedFields = "ResponseDueDate", SuggestedCorrection = "إدخال تاريخ المهلة عند اشتراط الرد." },
        ],
        DataCompletenessRate = 96,
        Recommendations =
        [
            new AnalyticalRecommendationDto { RecommendationId = "REC-OVERDUE", SourceFindingCode = "OVERDUE_RATE_INCREASED", Priority = "high", RecommendationText = "مراجعة المعاملات المتأخرة حسب الإدارات الأعلى أثرًا.", ResponsibleScope = "إدارة المتابعة", SuggestedDueDays = 2, EvidenceSummary = "overdue=11", Status = "Proposed" },
        ],
        Methodology = new MethodologyDto
        {
            ReportName = "تقرير المتابعة الإجرائية للمعاملات",
            ReportVersion = "2026.06.2",
            GeneratedAtUtc = FixedUtc,
            DataPeriod = "2026-01-01 إلى 2026-06-15",
            PeriodBasis = "الفترة الزمنية مبنية على تاريخ الوارد.",
            ComparisonPeriod = "2025-07-19 إلى 2025-12-31",
            Filters = "بدون فلاتر إضافية",
            RowLimits = "DetailLimit=500",
            DeferredMetrics = ["AverageFirstActionHours: يحتاج حدث أول إجراء موثوق."],
        },
    };

    internal static RenderedReportManifestDto RenderSections(
        InstitutionalReportModel model,
        params ReportSectionId[] sections) =>
        new InstitutionalReportRenderer().RenderManifest(model, sections);

    internal static RenderedReportManifestDto RenderAllSections(InstitutionalReportModel model) =>
        RenderSections(
            model,
            ReportSectionId.Cover,
            ReportSectionId.ExecutiveSummary,
            ReportSectionId.IndicatorsDashboard,
            ReportSectionId.DepartmentPerformance,
            ReportSectionId.RisksAndAlerts,
            ReportSectionId.ExecutiveRecommendations,
            ReportSectionId.TransactionDetails,
            ReportSectionId.ReportMetadata);
}
