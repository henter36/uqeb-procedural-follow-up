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
                OrganizationName = "الهيئة العامة للمتابعة الإجرائية",
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
