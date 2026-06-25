using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Models;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class ReportingAnalysisInstrumentationTests
{
    private static readonly DateTime ReferenceDate = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Build_RecordsExpectedStagesInStableOrder()
    {
        var capture = new CapturingReportingAnalysisInstrumentation();
        var snapshots = CreateSnapshots();
        var metrics = InstitutionalReportMetricsCalculator.Calculate(snapshots, ReferenceDate);

        _ = InstitutionalReportAnalysisService.Build(
            new InstitutionalReportAnalysisService.InstitutionalReportAnalysisInput
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
                    Title = "Instrumentation test",
                    ReportNumber = "REP-INST"
                },
                Filters = new ReportFiltersDto(),
                CurrentMetrics = metrics,
                CurrentSnapshots = snapshots,
                PreviousMetrics = null,
                PreviousSnapshots = [],
                Options = new ReportingAnalysisOptions(),
                DetailLimit = 500,
                DetailRowsTruncated = false
            },
            capture);

        Assert.Contains("kpis", capture.Stages);
        Assert.Contains("findings", capture.Stages);
        Assert.Contains("methodology", capture.Stages);
        Assert.Equal(1, capture.TotalCalls);
        Assert.Equal(2, capture.LastSnapshotCount);
        Assert.Equal(
            InstitutionalReportType.ExecutiveComprehensive.ToString(),
            capture.LastReportType);
        Assert.True(capture.LastTotalMilliseconds >= 0);
    }

    private static List<TransactionReportSnapshot> CreateSnapshots() =>
    [
        new()
        {
            TransactionId = 1,
            TrackingNumber = "INT-0001",
            IncomingNumber = "IN-0001",
            IncomingDate = ReferenceDate.AddDays(-10),
            Subject = "معاملة 1",
            IncomingParty = "جهة",
            CategoryName = "تصنيف",
            Priority = Priority.Urgent,
            Status = TransactionStatus.New,
            ResponsibleDepartment = "الشؤون الإدارية",
            ResponsibleDepartmentId = 1,
            IsOpen = true,
            IsClosed = false,
            IsOverdue = true,
            ElapsedDays = 10,
            ActiveAssignmentCount = 1,
            PendingReplyAssignmentCount = 1,
        },
        new()
        {
            TransactionId = 2,
            TrackingNumber = "INT-0002",
            IncomingNumber = "IN-0002",
            IncomingDate = ReferenceDate.AddDays(-5),
            Subject = "معاملة 2",
            IncomingParty = "جهة",
            CategoryName = "تصنيف",
            Priority = Priority.Normal,
            Status = TransactionStatus.Closed,
            ResponsibleDepartment = "الموارد البشرية",
            ResponsibleDepartmentId = 2,
            IsOpen = false,
            IsClosed = true,
            IsOverdue = false,
            ElapsedDays = 5,
            ClosedAt = ReferenceDate.AddDays(-1),
        },
    ];

    private sealed class CapturingReportingAnalysisInstrumentation : IReportingAnalysisInstrumentation
    {
        public List<string> Stages { get; } = [];
        public int TotalCalls { get; private set; }
        public string LastReportType { get; private set; } = string.Empty;
        public int LastSnapshotCount { get; private set; }
        public double LastTotalMilliseconds { get; private set; }

        public void RecordStage(string stage, double milliseconds, string reportType, int snapshotCount)
        {
            Stages.Add(stage);
            LastReportType = reportType;
            LastSnapshotCount = snapshotCount;
        }

        public void RecordTotal(double milliseconds, string reportType, int snapshotCount)
        {
            TotalCalls++;
            LastTotalMilliseconds = milliseconds;
            LastReportType = reportType;
            LastSnapshotCount = snapshotCount;
        }
    }
}
