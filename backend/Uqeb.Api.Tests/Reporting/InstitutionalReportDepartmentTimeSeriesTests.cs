using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

/// <summary>
/// DepartmentTimeSeries groups the same IncomingDate/TimeGrouping basis as the existing
/// (department-agnostic) TimeSeries, further split by ResponsibleDepartment — one row per
/// department per period, never duplicating a transaction across joint departments.
/// </summary>
public class InstitutionalReportDepartmentTimeSeriesTests
{
    private static readonly DateTime ReferenceDate = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

    private static InstitutionalReportAnalysisResult BuildAnalysis(
        List<TransactionReportSnapshot> snapshots,
        bool? includeTimeTrends = null,
        bool? includeDepartmentPerformance = null,
        InstitutionalReportType reportType = InstitutionalReportType.ExecutiveComprehensive)
    {
        var metrics = InstitutionalReportMetricsCalculator.Calculate(snapshots, ReferenceDate);
        return InstitutionalReportAnalysisService.Build(new InstitutionalReportAnalysisService.InstitutionalReportAnalysisInput
        {
            Request = new ReportBuildRequestDto
            {
                ReportType = reportType,
                SectionIds = [ReportSectionId.TimeTrends],
                IncludeComparison = false,
                IncludeTimeTrends = includeTimeTrends,
                IncludeDepartmentPerformance = includeDepartmentPerformance,
                Filters = new ReportFiltersDto
                {
                    DateFrom = new DateTime(2026, 4, 1),
                    DateTo = ReferenceDate,
                },
            },
            Metadata = new ReportMetadataDto
            {
                GeneratedAt = ReferenceDate,
                PeriodFrom = new DateTime(2026, 4, 1),
                PeriodTo = ReferenceDate,
                Title = "تقرير اختبار التحليل الزمني حسب الإدارة",
                ReportNumber = "REP-DEPT-TS",
            },
            Filters = new ReportFiltersDto(),
            CurrentMetrics = metrics,
            CurrentSnapshots = snapshots,
            PreviousMetrics = null,
            PreviousSnapshots = [],
            Options = new ReportingAnalysisOptions
            {
                MinimumComparisonSampleSize = 1,
                MinimumRankingSampleSize = 1,
            },
            DetailLimit = 500,
            DetailRowsTruncated = false,
        });
    }

    private static TransactionReportSnapshot Snapshot(
        int id,
        DateTime incomingDate,
        int? departmentId,
        string departmentName,
        bool isOpen,
        bool isClosed,
        bool isOverdue = false,
        DateTime? closedAt = null,
        DateTime? responseDueDate = null,
        int pendingReplies = 0,
        bool isPartialReply = false) => new()
    {
        TransactionId = id,
        TrackingNumber = $"INT-{id:D4}",
        IncomingNumber = $"IN-{id:D4}",
        IncomingDate = incomingDate,
        Subject = $"معاملة اختبار {id}",
        IncomingParty = "جهة اختبار",
        Priority = Priority.Normal,
        Status = isClosed ? TransactionStatus.Closed : TransactionStatus.New,
        RequiresResponse = responseDueDate.HasValue,
        ResponseCompleted = isClosed,
        ResponseDueDate = responseDueDate,
        ClosedAt = closedAt,
        CreatedAt = incomingDate,
        ResponsibleDepartment = departmentName,
        ResponsibleDepartmentId = departmentId,
        ActiveAssignmentCount = pendingReplies,
        PendingReplyAssignmentCount = pendingReplies,
        IsClosed = isClosed,
        IsOpen = isOpen,
        IsOverdue = isOverdue,
        IsPartialReply = isPartialReply,
        ElapsedDays = Math.Max(0, (ReferenceDate.Date - incomingDate.Date).Days),
    };

    [Fact]
    public void DepartmentTimeSeries_SameDepartment_TwoMonths_ProducesTwoPeriodPoints()
    {
        var snapshots = new List<TransactionReportSnapshot>
        {
            Snapshot(1, new DateTime(2026, 5, 5), 1, "الشؤون الإدارية", isOpen: true, isClosed: false),
            Snapshot(2, new DateTime(2026, 6, 10), 1, "الشؤون الإدارية", isOpen: true, isClosed: false),
        };

        var result = BuildAnalysis(snapshots);

        var deptPoints = result.DepartmentTimeSeries.Where(p => p.DepartmentId == 1).ToList();
        Assert.Equal(2, deptPoints.Count);
        Assert.Contains(deptPoints, p => p.PeriodLabel == "2026-05" && p.IncomingCount == 1);
        Assert.Contains(deptPoints, p => p.PeriodLabel == "2026-06" && p.IncomingCount == 1);
    }

    [Fact]
    public void DepartmentTimeSeries_TwoDepartments_SamePeriod_AppearAsSeparateRows()
    {
        var snapshots = new List<TransactionReportSnapshot>
        {
            Snapshot(1, new DateTime(2026, 6, 3), 1, "الشؤون الإدارية", isOpen: true, isClosed: false),
            Snapshot(2, new DateTime(2026, 6, 4), 2, "الموارد البشرية", isOpen: true, isClosed: false),
        };

        var result = BuildAnalysis(snapshots);

        var junePoints = result.DepartmentTimeSeries.Where(p => p.PeriodLabel == "2026-06").ToList();
        Assert.Equal(2, junePoints.Count);
        Assert.Contains(junePoints, p => p.DepartmentId == 1 && p.DepartmentName == "الشؤون الإدارية");
        Assert.Contains(junePoints, p => p.DepartmentId == 2 && p.DepartmentName == "الموارد البشرية");
    }

    [Fact]
    public void DepartmentTimeSeries_ClosedOnTime_ComputesOnTimeRateAndAverageCompletionDays()
    {
        var snapshots = new List<TransactionReportSnapshot>
        {
            Snapshot(
                1, new DateTime(2026, 6, 1), 1, "الشؤون الإدارية",
                isOpen: false, isClosed: true,
                closedAt: new DateTime(2026, 6, 5),
                responseDueDate: new DateTime(2026, 6, 10)),
        };

        var result = BuildAnalysis(snapshots);

        var point = Assert.Single(result.DepartmentTimeSeries);
        Assert.Equal(100, point.OnTimeCompletionRate);
        Assert.Equal(4, point.AverageCompletionDays);
    }

    [Fact]
    public void DepartmentTimeSeries_OpenOverdueTransaction_IncrementsOverdueCountInCorrectPeriod()
    {
        var snapshots = new List<TransactionReportSnapshot>
        {
            Snapshot(1, new DateTime(2026, 6, 2), 1, "الشؤون الإدارية", isOpen: true, isClosed: false, isOverdue: true),
            Snapshot(2, new DateTime(2026, 5, 2), 1, "الشؤون الإدارية", isOpen: true, isClosed: false, isOverdue: false),
        };

        var result = BuildAnalysis(snapshots);

        var june = result.DepartmentTimeSeries.Single(p => p.PeriodLabel == "2026-06");
        var may = result.DepartmentTimeSeries.Single(p => p.PeriodLabel == "2026-05");
        Assert.Equal(1, june.OverdueCount);
        Assert.Equal(0, may.OverdueCount);
    }

    [Fact]
    public void DepartmentTimeSeries_IncludeTimeTrendsFalse_IsNotBuilt()
    {
        var snapshots = new List<TransactionReportSnapshot>
        {
            Snapshot(1, new DateTime(2026, 6, 2), 1, "الشؤون الإدارية", isOpen: true, isClosed: false),
        };

        var result = BuildAnalysis(snapshots, includeTimeTrends: false);

        Assert.Empty(result.DepartmentTimeSeries);
        // Sanity: this is a real "not built" signal, not just an empty dataset artifact —
        // the department-agnostic TimeSeries is suppressed by the same flag too.
        Assert.Empty(result.TimeSeries);
    }

    [Fact]
    public void DepartmentTimeSeries_IncludeDepartmentPerformanceFalse_IsEmpty_ButTimeSeriesStillBuilt()
    {
        var snapshots = new List<TransactionReportSnapshot>
        {
            Snapshot(1, new DateTime(2026, 6, 2), 1, "الشؤون الإدارية", isOpen: true, isClosed: false),
        };

        var result = BuildAnalysis(snapshots, includeDepartmentPerformance: false);

        Assert.Empty(result.DepartmentTimeSeries);
        Assert.Single(result.TimeSeries);
    }

    [Fact]
    public void DepartmentTimeSeries_DoesNotAffectExistingTimeSeriesOrDepartmentPerformance()
    {
        var snapshots = new List<TransactionReportSnapshot>
        {
            Snapshot(1, new DateTime(2026, 6, 2), 1, "الشؤون الإدارية", isOpen: true, isClosed: false, isOverdue: true),
            Snapshot(2, new DateTime(2026, 6, 3), 2, "الموارد البشرية", isOpen: false, isClosed: true,
                closedAt: new DateTime(2026, 6, 6), responseDueDate: new DateTime(2026, 6, 8)),
        };

        var result = BuildAnalysis(snapshots);

        // The pre-existing, department-agnostic time series must total the same as before.
        var timeSeriesPoint = Assert.Single(result.TimeSeries);
        Assert.Equal(2, timeSeriesPoint.Incoming);
        Assert.Equal(1, timeSeriesPoint.Closed);
        Assert.Equal(1, timeSeriesPoint.Overdue);

        // The pre-existing aggregate department performance table must also be unaffected.
        Assert.Equal(2, result.DepartmentPerformance.Count);
        Assert.Contains(result.DepartmentPerformance, d => d.DepartmentName == "الشؤون الإدارية" && d.OverdueCount == 1);
        Assert.Contains(result.DepartmentPerformance, d => d.DepartmentName == "الموارد البشرية" && d.ClosedCount == 1);
    }

    [Fact]
    public void DepartmentTimeSeries_SkipsMissingDepartmentRows()
    {
        var snapshots = new List<TransactionReportSnapshot>
        {
            Snapshot(1, new DateTime(2026, 6, 2), 1, "الشؤون الإدارية", isOpen: true, isClosed: false),
            Snapshot(2, new DateTime(2026, 6, 3), null, "غير محدد", isOpen: true, isClosed: false),
            Snapshot(3, new DateTime(2026, 6, 4), 2, "   ", isOpen: true, isClosed: false),
        };

        var result = BuildAnalysis(snapshots);

        var point = Assert.Single(result.DepartmentTimeSeries);
        Assert.Equal(1, point.DepartmentId);
        Assert.Equal("الشؤون الإدارية", point.DepartmentName);
        Assert.DoesNotContain(result.DepartmentTimeSeries, row =>
            row.DepartmentId is null ||
            string.IsNullOrWhiteSpace(row.DepartmentName) ||
            row.DepartmentName.Contains("غير محدد", StringComparison.Ordinal));
    }

    [Fact]
    public void OverdueReport_KpisAndOverdueRate_UnaffectedByNewDepartmentTimeSeriesField()
    {
        // Scenario 6 (overdue side): PR #101's overdue-report metrics must compute exactly as
        // before — this analysis only adds a new, additive field, it does not touch the
        // snapshot set, the overdue evaluation date, or InstitutionalReportMetricsCalculator.
        var snapshots = new List<TransactionReportSnapshot>
        {
            Snapshot(
                1, new DateTime(2026, 6, 2), 1, "الشؤون الإدارية",
                isOpen: true, isClosed: false, isOverdue: true,
                responseDueDate: ReferenceDate.AddDays(-1)),
        };

        var result = BuildAnalysis(snapshots, reportType: InstitutionalReportType.OverdueTransactions);

        var overdueRateKpi = result.Kpis.Single(k => k.Key == "OverdueRate");
        Assert.Equal(100m, overdueRateKpi.NumericValue);
        Assert.Single(result.DepartmentTimeSeries);
    }
}
