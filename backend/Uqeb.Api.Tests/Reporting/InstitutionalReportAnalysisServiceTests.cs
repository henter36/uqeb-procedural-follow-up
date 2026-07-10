using System.Text.Json;
using System.Text.Encodings.Web;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportAnalysisServiceTests
{
    private static readonly DateTime ReferenceDate = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Build_ProducesStableAnalyticalContentAndSerializedLiteralValues()
    {
        var currentSnapshots = CreateCurrentSnapshots();
        var previousSnapshots = CreatePreviousSnapshots();
        var currentMetrics = InstitutionalReportMetricsCalculator.Calculate(currentSnapshots, ReferenceDate);
        var previousMetrics = InstitutionalReportMetricsCalculator.Calculate(previousSnapshots, ReferenceDate.AddMonths(-1));

        var result = InstitutionalReportAnalysisService.Build(new InstitutionalReportAnalysisService.InstitutionalReportAnalysisInput
        {
            Request = new ReportBuildRequestDto
            {
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [ReportSectionId.KeyPerformanceIndicators, ReportSectionId.SignificantFindings],
                Filters = new ReportFiltersDto
                {
                    DateFrom = new DateTime(2026, 6, 1),
                    DateTo = ReferenceDate
                }
            },
            Metadata = new ReportMetadataDto
            {
                GeneratedAt = ReferenceDate,
                PeriodFrom = new DateTime(2026, 6, 1),
                PeriodTo = ReferenceDate,
                Title = "تقرير اختبار التحليل",
                ReportNumber = "REP-TEST"
            },
            Filters = new ReportFiltersDto(),
            CurrentMetrics = currentMetrics,
            CurrentSnapshots = currentSnapshots,
            PreviousMetrics = previousMetrics,
            PreviousSnapshots = previousSnapshots,
            Options = new ReportingAnalysisOptions
            {
                SignificantChangeThresholdPercent = 10,
                MinimumComparisonSampleSize = 1,
                MinimumRankingSampleSize = 1,
                MaxExecutiveFindings = 10,
                MaxExecutiveCriticalCases = 10,
                MaxRecommendations = 10
            },
            DetailLimit = 500,
            DetailRowsTruncated = false
        });

        Assert.Equal(
            ["TotalTransactions", "IncomingTransactions", "CarriedOpenBalance", "TotalActiveBurden"],
            result.Kpis.Take(4).Select(k => k.Key));
        Assert.Equal("معاملة", result.Kpis.Single(k => k.Key == "TotalTransactions").Unit);
        Assert.Equal("number", result.Kpis.Single(k => k.Key == "TotalTransactions").Format);
        Assert.Equal("percent", result.Kpis.Single(k => k.Key == "OverdueRate").Format);
        Assert.Equal("decimal", result.Kpis.Single(k => k.Key == "AverageCompletionDays").Format);
        Assert.Equal("المعاملات مرحلة لفترة سابقة", result.Kpis.Single(k => k.Key == "CarriedOpenBalance").Title);\n        Assert.Equal("إجمالي المعاملات القائمة", result.Kpis.Single(k => k.Key == "TotalActiveBurden").Title);
        Assert.DoesNotContain(result.Kpis, k => k.Key == "AverageFollowUpsPerTransaction");
        Assert.DoesNotContain(result.Kpis, k => k.Title == "متوسط المتابعات لكل معاملة");

        Assert.Equal(
            ["DEPARTMENT_BACKLOG_CONCENTRATION", "OVERDUE_RATE_INCREASED", "BACKLOG_INCREASED"],
            result.Findings.Take(3).Select(f => f.Code));
        var overdueFinding = result.Findings.Single(f => f.Code == "OVERDUE_RATE_INCREASED");
        Assert.Equal(AnalyticalSeverity.High, overdueFinding.Severity);
        Assert.Equal("التقرير", overdueFinding.AffectedScope);
        Assert.Equal(66.7m, overdueFinding.CurrentValue);
        Assert.Equal(0m, overdueFinding.PreviousValue);

        var serialized = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        Assert.Contains("معاملة", serialized);
        Assert.Contains("number", serialized);
        Assert.Contains("percent", serialized);
        Assert.Contains("decimal", serialized);
    }

    [Fact]
    public void Build_WithDisabledComparison_ReportsNoComparisonPeriod()
    {
        var currentSnapshots = CreateCurrentSnapshots();
        var currentMetrics = InstitutionalReportMetricsCalculator.Calculate(currentSnapshots, ReferenceDate);

        var result = InstitutionalReportAnalysisService.Build(new InstitutionalReportAnalysisService.InstitutionalReportAnalysisInput
        {
            Request = new ReportBuildRequestDto
            {
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [ReportSectionId.KeyPerformanceIndicators],
                IncludeComparison = false,
                Filters = new ReportFiltersDto
                {
                    DateFrom = new DateTime(2026, 6, 1),
                    DateTo = ReferenceDate,
                },
            },
            Metadata = new ReportMetadataDto
            {
                GeneratedAt = ReferenceDate,
                PeriodFrom = new DateTime(2026, 6, 1),
                PeriodTo = ReferenceDate,
                Title = "تقرير بدون مقارنة",
                ReportNumber = "REP-NOCMP",
            },
            Filters = new ReportFiltersDto(),
            CurrentMetrics = currentMetrics,
            CurrentSnapshots = currentSnapshots,
            PreviousMetrics = null,
            PreviousSnapshots = [],
            Options = new ReportingAnalysisOptions(),
            DetailLimit = 500,
            DetailRowsTruncated = false,
        });

        Assert.Equal(ReportComparisonMode.None, result.ComparisonMode);
        Assert.Equal("لا توجد مقارنة", result.Methodology.ComparisonPeriod);
    }

    [Fact]
    public void CreateComparisonRequest_NotRequested_ReturnsNullReasonAndNullRequest()
    {
        var request = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            IncludeComparison = false,
            Filters = new ReportFiltersDto { DateFrom = new DateTime(2026, 1, 1), DateTo = new DateTime(2026, 1, 31) },
        };

        var comparisonRequest = InstitutionalReportAnalysisService.CreateComparisonRequest(request, out var reason);

        Assert.Null(comparisonRequest);
        Assert.Null(reason);
    }

    [Fact]
    public void CreateComparisonRequest_ModeNone_ReturnsNullReasonAndNullRequest()
    {
        var request = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            IncludeComparison = true,
            ComparisonMode = ReportComparisonMode.None,
            Filters = new ReportFiltersDto { DateFrom = new DateTime(2026, 1, 1), DateTo = new DateTime(2026, 1, 31) },
        };

        var comparisonRequest = InstitutionalReportAnalysisService.CreateComparisonRequest(request, out var reason);

        Assert.Null(comparisonRequest);
        Assert.Null(reason);
    }

    [Fact]
    public void CreateComparisonRequest_RequestedButPeriodIncomplete_ReturnsNonNullReason()
    {
        var request = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            IncludeComparison = true,
            ComparisonMode = ReportComparisonMode.PreviousEquivalentPeriod,
            Filters = new ReportFiltersDto { DateFrom = new DateTime(2026, 1, 1), DateTo = null },
        };

        var comparisonRequest = InstitutionalReportAnalysisService.CreateComparisonRequest(request, out var reason);

        Assert.Null(comparisonRequest);
        Assert.NotNull(reason);
    }

    [Fact]
    public void CreateComparisonRequest_ValidPeriod_ReturnsRequestWithNullReasonAndCopiesDepartmentIds()
    {
        var request = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.DepartmentTransactions,
            IncludeComparison = true,
            ComparisonMode = ReportComparisonMode.PreviousEquivalentPeriod,
            Filters = new ReportFiltersDto
            {
                DateFrom = new DateTime(2026, 1, 15),
                DateTo = new DateTime(2026, 1, 31),
                DepartmentIds = [10, 20],
            },
        };

        var comparisonRequest = InstitutionalReportAnalysisService.CreateComparisonRequest(request, out var reason);

        Assert.Null(reason);
        Assert.NotNull(comparisonRequest);
        Assert.Equal(InstitutionalReportType.DepartmentTransactions, comparisonRequest.ReportType);
        Assert.Equal([10, 20], comparisonRequest.Filters.DepartmentIds);
    }

    [Fact]
    public void Build_DepartmentRecognitions_ClassifiesOutstandingAndImprovedDepartmentsWithoutChangingPerformanceRows()
    {
        var currentSnapshots = new List<TransactionReportSnapshot>();
        currentSnapshots.AddRange(RecognitionSnapshots(100, 10, "إدارة التميز", 5, onTime: true, overdue: false, completionDays: 2));
        currentSnapshots.AddRange(RecognitionSnapshots(200, 20, "إدارة صغيرة", 2, onTime: true, overdue: false, completionDays: 1));
        currentSnapshots.AddRange(RecognitionSnapshots(300, 30, "إدارة بيانات ضعيفة", 5, onTime: true, overdue: false, completionDays: 1, completeData: false));
        currentSnapshots.AddRange(RecognitionSnapshots(400, 40, "إدارة متحسنة", 5, onTime: false, overdue: false, completionDays: 3));
        currentSnapshots.AddRange(RecognitionSnapshots(500, 50, "إدارة التأخر", 5, onTime: false, overdue: true, completionDays: 12));

        var previousSnapshots = new List<TransactionReportSnapshot>();
        previousSnapshots.AddRange(RecognitionSnapshots(1400, 40, "إدارة متحسنة", 5, onTime: false, overdue: true, completionDays: 12));
        previousSnapshots.AddRange(RecognitionSnapshots(1100, 10, "إدارة التميز", 5, onTime: true, overdue: false, completionDays: 2));

        var result = BuildAnalysis(currentSnapshots, previousSnapshots, minimumRankingSampleSize: 5);

        Assert.Contains(result.DepartmentRecognitions, row =>
            row.DepartmentName == "إدارة التميز" &&
            row.RecognitionType == "متميزة" &&
            row.HasSufficientSample);
        Assert.DoesNotContain(result.DepartmentRecognitions, row =>
            row.DepartmentName == "إدارة صغيرة" &&
            row.RecognitionType == "متميزة");
        Assert.DoesNotContain(result.DepartmentRecognitions, row =>
            row.DepartmentName == "إدارة بيانات ضعيفة" &&
            row.RecognitionType == "متميزة");
        Assert.Contains(result.DepartmentRecognitions, row =>
            row.DepartmentName == "إدارة متحسنة" &&
            row.RecognitionType == "الأكثر تحسنًا" &&
            row.ImprovementValue > 0);
        Assert.DoesNotContain(result.DepartmentRecognitions, row =>
            row.DepartmentName == "إدارة متحسنة" &&
            row.RecognitionType == "متميزة");
        Assert.Equal("إدارة التأخر", result.DepartmentPerformance.First().DepartmentName);
    }

    private static List<TransactionReportSnapshot> CreateCurrentSnapshots() =>
    [
        Snapshot(1, TransactionStatus.New, Priority.Urgent, isOpen: true, isClosed: false, isOverdue: true, elapsedDays: 30, department: "الشؤون الإدارية", pendingReplies: 1, dueDate: ReferenceDate.AddDays(-10)),
        Snapshot(2, TransactionStatus.New, Priority.Urgent, isOpen: true, isClosed: false, isOverdue: true, elapsedDays: 18, department: "الشؤون الإدارية", pendingReplies: 1, dueDate: ReferenceDate.AddDays(-5)),
        Snapshot(3, TransactionStatus.Closed, Priority.Normal, isOpen: false, isClosed: true, isOverdue: false, elapsedDays: 4, department: "الموارد البشرية", closedAt: ReferenceDate.AddDays(-1), dueDate: ReferenceDate.AddDays(2)),
    ];

    private static List<TransactionReportSnapshot> CreatePreviousSnapshots() =>
    [
        Snapshot(11, TransactionStatus.Closed, Priority.Normal, isOpen: false, isClosed: true, isOverdue: false, elapsedDays: 3, department: "الشؤون الإدارية", closedAt: ReferenceDate.AddMonths(-1), dueDate: ReferenceDate.AddMonths(-1).AddDays(2)),
        Snapshot(12, TransactionStatus.Closed, Priority.Normal, isOpen: false, isClosed: true, isOverdue: false, elapsedDays: 2, department: "الموارد البشرية", closedAt: ReferenceDate.AddMonths(-1).AddDays(-1), dueDate: ReferenceDate.AddMonths(-1).AddDays(1)),
    ];

    private static InstitutionalReportAnalysisResult BuildAnalysis(
        List<TransactionReportSnapshot> currentSnapshots,
        List<TransactionReportSnapshot> previousSnapshots,
        int minimumRankingSampleSize)
    {
        var currentMetrics = InstitutionalReportMetricsCalculator.Calculate(currentSnapshots, ReferenceDate);
        var previousMetrics = InstitutionalReportMetricsCalculator.Calculate(previousSnapshots, ReferenceDate.AddMonths(-1));

        return InstitutionalReportAnalysisService.Build(new InstitutionalReportAnalysisService.InstitutionalReportAnalysisInput
        {
            Request = new ReportBuildRequestDto
            {
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [ReportSectionId.DepartmentPerformance, ReportSectionId.OutstandingAndImprovedDepartments],
                Filters = new ReportFiltersDto
                {
                    DateFrom = new DateTime(2026, 6, 1),
                    DateTo = ReferenceDate
                }
            },
            Metadata = new ReportMetadataDto
            {
                GeneratedAt = ReferenceDate,
                PeriodFrom = new DateTime(2026, 6, 1),
                PeriodTo = ReferenceDate,
                Title = "تقرير اختبار التميز",
                ReportNumber = "REP-RECOG"
            },
            Filters = new ReportFiltersDto(),
            CurrentMetrics = currentMetrics,
            CurrentSnapshots = currentSnapshots,
            PreviousMetrics = previousMetrics,
            PreviousSnapshots = previousSnapshots,
            Options = new ReportingAnalysisOptions
            {
                SignificantChangeThresholdPercent = 10,
                MinimumComparisonSampleSize = 1,
                MinimumRankingSampleSize = minimumRankingSampleSize,
                MaxExecutiveFindings = 10,
                MaxExecutiveCriticalCases = 10,
                MaxRecommendations = 10
            },
            DetailLimit = 500,
            DetailRowsTruncated = false
        });
    }

    private static IEnumerable<TransactionReportSnapshot> RecognitionSnapshots(
        int startId,
        int departmentId,
        string department,
        int count,
        bool onTime,
        bool overdue,
        int completionDays,
        bool completeData = true)
    {
        for (var i = 0; i < count; i++)
        {
            var incomingDate = ReferenceDate.AddDays(-(completionDays + 2 + i));
            var closedAt = incomingDate.AddDays(completionDays);
            yield return new TransactionReportSnapshot
            {
                TransactionId = startId + i,
                TrackingNumber = $"INT-{startId + i:D4}",
                IncomingNumber = $"IN-{startId + i:D4}",
                IncomingDate = incomingDate,
                Subject = $"معاملة تصنيف {startId + i}",
                IncomingParty = completeData ? "جهة اختبار" : string.Empty,
                CategoryName = completeData ? "تصنيف اختبار" : string.Empty,
                Priority = Priority.Normal,
                Status = TransactionStatus.Closed,
                RequiresResponse = false,
                ResponseCompleted = true,
                ResponseDueDate = onTime ? closedAt.AddDays(1) : closedAt.AddDays(-1),
                ClosedAt = closedAt,
                CreatedAt = incomingDate,
                UpdatedAt = closedAt,
                ResponsibleDepartment = department,
                ResponsibleDepartmentId = departmentId,
                IsClosed = true,
                IsOpen = false,
                IsOverdue = overdue,
                IsCompletedLate = overdue,
                ElapsedDays = completionDays
            };
        }
    }

    private static TransactionReportSnapshot Snapshot(
        int id,
        TransactionStatus status,
        Priority priority,
        bool isOpen,
        bool isClosed,
        bool isOverdue,
        int elapsedDays,
        string department,
        int pendingReplies = 0,
        DateTime? dueDate = null,
        DateTime? closedAt = null) => new()
    {
        TransactionId = id,
        TrackingNumber = $"INT-{id:D4}",
        IncomingNumber = $"IN-{id:D4}",
        IncomingDate = ReferenceDate.AddDays(-elapsedDays),
        Subject = $"معاملة اختبار {id}",
        IncomingParty = "جهة اختبار",
        CategoryName = "تصنيف اختبار",
        Priority = priority,
        Status = status,
        RequiresResponse = pendingReplies > 0,
        ResponseCompleted = isClosed,
        ResponseDueDate = dueDate,
        ClosedAt = closedAt,
        CreatedAt = ReferenceDate.AddDays(-elapsedDays),
        UpdatedAt = ReferenceDate.AddDays(-elapsedDays / 2),
        ResponsibleDepartment = department,
        ResponsibleDepartmentId = id,
        ActiveAssignmentCount = pendingReplies,
        PendingReplyAssignmentCount = pendingReplies,
        EarliestPendingReplyDueDate = dueDate,
        IsClosed = isClosed,
        IsOpen = isOpen,
        IsOverdue = isOverdue,
        IsWaitingForStatement = pendingReplies > 0,
        ElapsedDays = elapsedDays
    };
}
