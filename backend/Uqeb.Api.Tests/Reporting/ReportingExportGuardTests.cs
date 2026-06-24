using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Operations;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class ReportingExportGuardTests
{
    [Fact]
    public async Task BeginExportAsync_ReleasesConcurrencyPermit_WhenSessionCreationFails()
    {
        var options = Options.Create(new ReportingOptions
        {
            MaxConcurrentPdfExports = 1,
            MaxExportDurationSeconds = 30,
        });
        var metrics = new ReportingMetrics();
        var temp = new ReportingTempFileManager(options, NullLogger<ReportingTempFileManager>.Instance, metrics);
        var gate = new ReportingExportConcurrencyGate(options);
        var admission = new ReportingExportAdmissionService(
            gate,
            new ThrowingResourceGuard(),
            new NoOpAuditService(),
            new TestCurrentUser(),
            metrics);
        var lifecycle = new ReportingExportLifecycleObserver(
            new NoOpAuditService(),
            new TestCurrentUser(),
            metrics,
            NullLogger<ReportingExportLifecycleObserver>.Instance);
        var scopeFactory = new ReportingExportScopeFactory(gate, temp, lifecycle, metrics);
        var guard = new ReportingExportGuard(
            admission,
            new ThrowingResourceGuard(),
            scopeFactory,
            new ReportingCorrelationIdProvider(new HttpContextAccessor()),
            gate);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.BeginExportAsync(new Uqeb.Api.Reporting.DTOs.ReportExportRequestDto(), CancellationToken.None));

        Assert.Equal(0, gate.ActivePdfExports);
        Assert.True(gate.HasCapacity(ExportFormat.Pdf));
    }

    private sealed class ThrowingResourceGuard : IReportingExportResourceGuard
    {
        public void EnsureDiskSpaceForExport() { }

        public (string SessionDirectory, CancellationTokenSource TimeoutSource) BeginSession(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("session bootstrap failed");
    }

    private sealed class NoOpAuditService : Uqeb.Api.Services.IAuditService
    {
        public Task LogAsync(
            int userId,
            Uqeb.Api.Models.Enums.AuditAction action,
            string? entityName,
            int? entityId,
            int? transactionId,
            string? oldValue,
            string? newValue) => Task.CompletedTask;
    }

    private sealed class TestCurrentUser : Uqeb.Api.Services.ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public int UserId => 1;
        public string Username => "test";
        public Uqeb.Api.Models.Enums.UserRole Role => Uqeb.Api.Models.Enums.UserRole.Admin;
        public int? DepartmentId => null;
    }
}
