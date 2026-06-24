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
        var service = CreateService(new SlowBuildSupport(), lifecycle, logger: logger, correlationId: "corr-token-cancel");
        using var cts = new CancellationTokenSource();

        var exportTask = service.ExportAsync(ValidRequest, cts.Token);
        await Task.Delay(50);
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

    private static InstitutionalReportExportService CreateService(
        IInstitutionalReportBuildSupport buildSupport,
        RecordingLifecycleObserver lifecycle,
        ReportingOptions? reportingOptions = null,
        ILogger<InstitutionalReportExportService>? logger = null,
        string? correlationId = null)
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
            new StubPdfExporter(),
            options,
            exportGuard,
            metrics,
            logger ?? NullLogger<InstitutionalReportExportService>.Instance,
            correlationIdProvider);
    }

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
        public async Task<int> CountMatchingTransactionsAsync(ReportBuildRequestDto request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        public Task<InstitutionalReportModel> BuildInternalAsync(
            ReportBuildRequestDto request,
            CancellationToken ct,
            ReportAssemblyOptions options) =>
            Task.FromException<InstitutionalReportModel>(new InvalidOperationException("unreachable"));
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
