using Uqeb.Api.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Uqeb.Api.Helpers;
using Uqeb.Api.Middleware;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Reporting.Operations;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportExportServiceExceptionTests
{
    private static readonly ReportExportRequestDto ValidRequest = new()
    {
        ExportFormat = ExportFormat.Html,
        BuildRequest = new ReportBuildRequestDto
        {
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIds = [ReportSectionId.Cover],
        },
    };

    [Fact]
    public async Task ExportAsync_ReportingExportRejectedException_PassesThroughWithoutExportFailedLog()
    {
        var lifecycle = new RecordingLifecycleObserver();
        var expected = new ReportingExportRejectedException(
            ReportingErrorCodes.ConcurrencyLimit,
            "تجاوز حد التزامن.");
        var service = CreateService(new ThrowingBuildSupport(expected), lifecycle);

        var ex = await Assert.ThrowsAsync<ReportingExportRejectedException>(
            () => service.ExportAsync(ValidRequest));

        Assert.Same(expected, ex);
        Assert.DoesNotContain("export_failed", lifecycle.FailedResults);
    }

    [Fact]
    public async Task ExportAsync_ReportingConfigurationException_PassesThroughWithoutExportFailedLog()
    {
        var lifecycle = new RecordingLifecycleObserver();
        var expected = new ReportingConfigurationException(
            ReportingErrorCodes.ChromiumUnavailable,
            "متصفح Chromium غير متاح.");
        var service = CreateService(new ThrowingBuildSupport(expected), lifecycle);

        var ex = await Assert.ThrowsAsync<ReportingConfigurationException>(
            () => service.ExportAsync(ValidRequest));

        Assert.Same(expected, ex);
        Assert.DoesNotContain("export_failed", lifecycle.FailedResults);
    }

    [Fact]
    public async Task ExportAsync_FieldValidationException_PassesThroughWithoutExportFailedLog()
    {
        var lifecycle = new RecordingLifecycleObserver();
        var expected = new FieldValidationException(new Dictionary<string, string>
        {
            ["selectedPages"] = "يجب تحديد الصفحة الحالية للتصدير.",
        });
        var service = CreateService(new ThrowingBuildSupport(expected), lifecycle);

        var ex = await Assert.ThrowsAsync<FieldValidationException>(
            () => service.ExportAsync(ValidRequest));

        Assert.Same(expected, ex);
        Assert.DoesNotContain("export_failed", lifecycle.FailedResults);
    }

    [Fact]
    public async Task ExportAsync_UnexpectedException_WrapsInInstitutionalReportExportException()
    {
        var lifecycle = new RecordingLifecycleObserver();
        var logger = new RecordingLogger<InstitutionalReportExportService>();
        var expected = new InvalidOperationException("boom");
        var service = CreateService(
            new ThrowingBuildSupport(expected),
            lifecycle,
            logger: logger,
            correlationId: "corr-export-wrap");

        var ex = await Assert.ThrowsAsync<InstitutionalReportExportException>(
            () => service.ExportAsync(ValidRequest));

        Assert.Same(expected, ex.InnerException);
        Assert.Contains("ExportFormat=", ex.Message);
        Assert.Contains("ReportType=", ex.Message);
        Assert.Contains("corr-export-wrap", ex.Message);
        Assert.Contains("export_failed", lifecycle.FailedResults);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ExportAsync_ExternalCancellation_LogsCancelledAndRethrowsWithoutExportFailedLog()
    {
        var lifecycle = new RecordingLifecycleObserver();
        var logger = new RecordingLogger<InstitutionalReportExportService>();
        var expected = new OperationCanceledException("export cancelled by client");
        var service = CreateService(
            new ThrowingBuildSupport(expected),
            lifecycle,
            logger: logger,
            correlationId: "corr-export-cancel");

        var ex = await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ExportAsync(ValidRequest, CancellationToken.None));

        Assert.Same(expected, ex);
        Assert.Equal(1, lifecycle.CancelledCount);
        Assert.DoesNotContain("export_failed", lifecycle.FailedResults);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ExportAsync_ExternalCancellationViaToken_LogsCancelledOnceAndRethrowsOperationCanceledException()
    {
        var lifecycle = new RecordingLifecycleObserver();
        var logger = new RecordingLogger<InstitutionalReportExportService>();
        var buildSupport = new SlowBuildSupport();
        var service = CreateService(
            buildSupport,
            lifecycle,
            logger: logger,
            correlationId: "corr-token-cancel");
        using var cts = new CancellationTokenSource();

        var exportTask = service.ExportAsync(ValidRequest, cts.Token);

        await buildSupport.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await exportTask);

        Assert.Equal(1, lifecycle.CancelledCount);
        Assert.DoesNotContain("export_failed", lifecycle.FailedResults);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ExportAsync_ExportTimeout_LogsTimeoutCodeAndThrowsRejectedExceptionWithInnerCancellation()
    {
        var lifecycle = new RecordingLifecycleObserver();
        var service = CreateService(
            new SlowBuildSupport(),
            lifecycle,
            new ReportingOptions { MaxExportDurationSeconds = 1, MaxPdfDetailRows = 10_000 });

        var ex = await Assert.ThrowsAsync<ReportingExportRejectedException>(
            () => service.ExportAsync(ValidRequest));

        Assert.Equal(ReportingErrorCodes.ExportTimeout, ex.ErrorCode);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ex.StatusCode);
        Assert.Equal("انتهت مهلة تصدير التقرير.", ex.Message);
        Assert.IsAssignableFrom<OperationCanceledException>(ex.InnerException);
        Assert.Contains(ReportingErrorCodes.ExportTimeout, lifecycle.FailedResults);
        Assert.DoesNotContain("export_failed", lifecycle.FailedResults);
        Assert.Equal(0, lifecycle.CancelledCount);
    }

    [Fact]
    public async Task ExportAsync_PdfMeasuredPagination_ResolvesSelectedPagesAfterMeasuredManifest()
    {
        var model = CreateTransactionDetailsModel(rowCount: 3);
        var pdfExporter = new RecordingPdfExporter();
        var measurer = new StubMeasuredPaginationMeasurer(
        [
            [model.Transactions[0]],
            [model.Transactions[1]],
            [model.Transactions[2]],
        ]);
        var service = CreateService(
            new SuccessfulBuildSupport(model),
            new RecordingLifecycleObserver(),
            pdfExporter: pdfExporter,
            pdfPaginationMeasurer: measurer);
        var request = new ReportExportRequestDto
        {
            ExportFormat = ExportFormat.Pdf,
            ExportMode = ExportMode.SelectedPages,
            SelectedPageNumbers = [3],
            BuildRequest = new ReportBuildRequestDto
            {
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                SectionIds = [ReportSectionId.TransactionDetails],
            },
        };

        var result = await service.ExportAsync(request);

        Assert.Equal(1, measurer.CallCount);
        Assert.NotNull(pdfExporter.Manifest);
        var exportedPage = Assert.Single(pdfExporter.Manifest.Pages);
        Assert.Equal(3, exportedPage.OriginalPageNumber);
        Assert.Equal(ReportSectionId.TransactionDetails, exportedPage.SectionId);
        Assert.Contains("INT-0003", pdfExporter.HtmlDocument);
        Assert.DoesNotContain("INT-0001", pdfExporter.HtmlDocument);
        Assert.DoesNotContain("INT-0002", pdfExporter.HtmlDocument);
        Assert.Equal(1, CountOccurrences(pdfExporter.HtmlDocument, "<footer class=\"report-footer"));
        Assert.DoesNotContain("<tbody></tbody>", pdfExporter.HtmlDocument);
        var resultPage = Assert.Single(result.Manifest!.Pages);
        Assert.Equal(3, resultPage.OriginalPageNumber);
    }

    private static InstitutionalReportExportService CreateService(
        IInstitutionalReportBuildSupport buildSupport,
        RecordingLifecycleObserver lifecycle,
        ReportingOptions? reportingOptions = null,
        ILogger<InstitutionalReportExportService>? logger = null,
        string? correlationId = null,
        IInstitutionalReportPdfExporter? pdfExporter = null,
        IInstitutionalReportPdfPaginationMeasurer? pdfPaginationMeasurer = null)
    {
        var options = Options.Create(reportingOptions ?? new ReportingOptions { MaxPdfDetailRows = 10_000 });
        var metrics = new ReportingMetrics();
        var tempFileManager = new ReportingTempFileManager(
            options,
            NullLogger<ReportingTempFileManager>.Instance,
            metrics);
        var concurrencyGate = new ReportingExportConcurrencyGate(options);
        var resourceGuard = new ReportingExportResourceGuard(options, tempFileManager);
        var auditService = new NoOpAuditService();
        var currentUser = new TestCurrentUserService();
        var admission = new ReportingExportAdmissionService(
            concurrencyGate,
            resourceGuard,
            auditService,
            currentUser,
            metrics);
        var scopeFactory = new ReportingExportScopeFactory(
            concurrencyGate,
            tempFileManager,
            lifecycle,
            metrics);
        var httpContextAccessor = new HttpContextAccessor();
        if (correlationId is not null)
        {
            httpContextAccessor.HttpContext = new DefaultHttpContext();
            httpContextAccessor.HttpContext.Items[CorrelationIdMiddleware.ItemKey] = correlationId;
        }

        var correlationIdProvider = new ReportingCorrelationIdProvider(httpContextAccessor);
        var exportGuard = new ReportingExportGuard(
            admission,
            resourceGuard,
            scopeFactory,
            correlationIdProvider,
            concurrencyGate);

        return new InstitutionalReportExportService(
            buildSupport,
            pdfExporter ?? new StubPdfExporter(),
            options,
            logger ?? NullLogger<InstitutionalReportExportService>.Instance,
            new InstitutionalReportExportRuntimeDependencies(
                exportGuard,
                metrics,
                correlationIdProvider,
                pdfPaginationMeasurer));
    }

    private static InstitutionalReportModel CreateTransactionDetailsModel(int rowCount)
    {
        var issueDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        return new InstitutionalReportModel
        {
            TotalMatchedRows = rowCount,
            ExportedDetailRows = rowCount,
            DetailPartsCount = 1,
            Metadata = new ReportMetadataDto
            {
                ReportNumber = "REP-2026-SELECT",
                ReportType = InstitutionalReportType.ExecutiveComprehensive,
                ReportTypeName = "التقرير التنفيذي الشامل",
                Title = "تقرير اختبار اختيار الصفحات",
                IssueDate = issueDate,
                PeriodFrom = new DateTime(2026, 1, 1),
                PeriodTo = new DateTime(2026, 6, 15),
                GeneratedAt = issueDate,
                TotalMatchingTransactions = rowCount,
                IncludedTransactionCount = rowCount,
                DetailRowLimit = 500,
            },
            Transactions = Enumerable.Range(1, rowCount)
                .Select(i => new TransactionDetailRowDto
                {
                    Sequence = i,
                    TransactionId = i,
                    TrackingNumber = $"INT-{i:D4}",
                    IncomingNumber = $"IN-{i:D4}",
                    IncomingDate = issueDate.AddDays(-i),
                    Subject = $"معاملة اختبار {i}",
                    IncomingParty = "جهة حكومية",
                    ResponsibleDepartment = "إدارة الاختبار",
                    Status = "مفتوحة",
                    FollowUpStage = "بانتظار رد",
                    ElapsedDays = i,
                    ResponseState = "بانتظار",
                })
                .ToList(),
        };
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private sealed class ThrowingBuildSupport(Exception exception) : IInstitutionalReportBuildSupport
    {
        public Task<int> CountMatchingTransactionsAsync(ReportBuildRequestDto request, CancellationToken ct) =>
            Task.FromException<int>(exception);

        public Task<InstitutionalReportModel> BuildInternalAsync(
            ReportBuildRequestDto request,
            CancellationToken ct,
            ReportAssemblyOptions options) =>
            Task.FromException<InstitutionalReportModel>(exception);
    }

    private sealed class SlowBuildSupport : IInstitutionalReportBuildSupport
    {
        private readonly TaskCompletionSource<bool> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public async Task<int> CountMatchingTransactionsAsync(
            ReportBuildRequestDto request,
            CancellationToken ct)
        {
            _started.TrySetResult(true);

            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return 0;
        }

        public Task<InstitutionalReportModel> BuildInternalAsync(
            ReportBuildRequestDto request,
            CancellationToken ct,
            ReportAssemblyOptions options) =>
            Task.FromException<InstitutionalReportModel>(
                new InvalidOperationException("unreachable"));
    }

    private sealed class SuccessfulBuildSupport(InstitutionalReportModel model) : IInstitutionalReportBuildSupport
    {
        public Task<int> CountMatchingTransactionsAsync(ReportBuildRequestDto request, CancellationToken ct) =>
            Task.FromResult(model.TotalMatchedRows);

        public Task<InstitutionalReportModel> BuildInternalAsync(
            ReportBuildRequestDto request,
            CancellationToken ct,
            ReportAssemblyOptions options) =>
            Task.FromResult(model);
    }

    private sealed class RecordingLifecycleObserver : IReportingExportLifecycleObserver
    {
        public List<string> FailedResults { get; } = [];
        public int CancelledCount { get; private set; }

        public Task LogStartedAsync(ReportingExportSessionContext session, int? matchedRows) =>
            Task.CompletedTask;

        public Task LogCompletedAsync(
            ReportingExportSessionContext session,
            ReportingExportCompletedLog completed) =>
            Task.CompletedTask;

        public Task LogFailedAsync(ReportingExportSessionContext session, ReportingExportFailedLog failed)
        {
            FailedResults.Add(failed.Result);
            return Task.CompletedTask;
        }

        public Task LogCancelledAsync(ReportingExportSessionContext session, ReportingExportCancelledLog cancelled)
        {
            CancelledCount++;
            return Task.CompletedTask;
        }

        public Task LogRejectedAsync(ReportingExportSessionContext session, string errorCode) =>
            Task.CompletedTask;
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public int UserId => 1;
        public string Username => "test";
        public UserRole Role => UserRole.Admin;
        public int? DepartmentId => null;
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public AuditLog TrackLog(
            int userId,
            AuditAction action,
            string? entityName,
            int? entityId,
            int? transactionId,
            string? oldValue,
            string? newValue) => new AuditLog();

        public Task LogAsync(
            int userId,
            AuditAction action,
            string? entityName,
            int? entityId,
            int? transactionId,
            string? oldValue,
            string? newValue) =>
            Task.CompletedTask;
    }

    private sealed class StubPdfExporter : IInstitutionalReportPdfExporter
    {
        public Task<byte[]> ExportAsync(
            RenderedReportManifestDto manifest,
            string htmlDocument,
            CancellationToken ct = default) =>
            Task.FromResult("%PDF-1.4"u8.ToArray());
    }

    private sealed class RecordingPdfExporter : IInstitutionalReportPdfExporter
    {
        public RenderedReportManifestDto? Manifest { get; private set; }
        public string HtmlDocument { get; private set; } = string.Empty;

        public Task<byte[]> ExportAsync(
            RenderedReportManifestDto manifest,
            string htmlDocument,
            CancellationToken ct = default)
        {
            Manifest = manifest;
            HtmlDocument = htmlDocument;
            return Task.FromResult("%PDF-1.4"u8.ToArray());
        }
    }

    private sealed class StubMeasuredPaginationMeasurer(
        IReadOnlyList<IReadOnlyList<TransactionDetailRowDto>> chunks) : IInstitutionalReportPdfPaginationMeasurer
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<IReadOnlyList<TransactionDetailRowDto>>> MeasureTransactionDetailChunksAsync(
            RenderedReportManifestDto preflightManifest,
            string preflightHtmlDocument,
            IReadOnlyList<TransactionDetailRowDto> sourceRows,
            CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(chunks);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
