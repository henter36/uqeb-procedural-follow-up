using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Operations;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class ReportingExportConcurrencyGateTests
{
    [Fact]
    public async Task AcquireAsync_Throws_WhenPdfSlotsAreSaturated()
    {
        using var gate = CreateGate(maxPdfExports: 1, waitSeconds: 1);
        await gate.AcquireAsync(ExportFormat.Pdf, CancellationToken.None);

        Assert.Equal(1, gate.ActivePdfExports);
        Assert.False(gate.HasCapacity(ExportFormat.Pdf));

        var rejected = await Assert.ThrowsAsync<ReportingExportRejectedException>(() =>
            gate.AcquireAsync(ExportFormat.Pdf, CancellationToken.None));

        Assert.Equal(ReportingErrorCodes.ConcurrencyLimit, rejected.ErrorCode);
        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.StatusCode);
        Assert.Equal(1, gate.ActivePdfExports);
    }

    [Fact]
    public async Task Release_AllowsNextAcquisition_AndRestoresCapacity()
    {
        using var gate = CreateGate(maxPdfExports: 1, waitSeconds: 1);
        await gate.AcquireAsync(ExportFormat.Pdf, CancellationToken.None);
        Assert.Equal(1, gate.ActivePdfExports);
        Assert.False(gate.HasCapacity(ExportFormat.Pdf));

        gate.Release(ExportFormat.Pdf);

        Assert.Equal(0, gate.ActivePdfExports);
        Assert.True(gate.HasCapacity(ExportFormat.Pdf));

        await gate.AcquireAsync(ExportFormat.Pdf, CancellationToken.None);
        Assert.Equal(1, gate.ActivePdfExports);
        gate.Release(ExportFormat.Pdf);
        Assert.Equal(0, gate.ActivePdfExports);
    }

    [Fact]
    public async Task AcquireAsync_DoesNotExceedMaxConcurrentPdfExports()
    {
        using var gate = CreateGate(maxPdfExports: 2, waitSeconds: 1);
        await gate.AcquireAsync(ExportFormat.Pdf, CancellationToken.None);
        await gate.AcquireAsync(ExportFormat.Pdf, CancellationToken.None);

        Assert.Equal(2, gate.ActivePdfExports);
        Assert.False(gate.HasCapacity(ExportFormat.Pdf));

        gate.Release(ExportFormat.Pdf);
        Assert.Equal(1, gate.ActivePdfExports);
        Assert.True(gate.HasCapacity(ExportFormat.Pdf));
    }

    [Fact]
    public async Task AcquireAsync_ThrowsOperationCanceledException_WhenCancelledWhileWaiting()
    {
        using var gate = CreateGate(maxPdfExports: 1, waitSeconds: 30);
        await gate.AcquireAsync(ExportFormat.Pdf, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var blockerReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitTask = Task.Run(async () =>
        {
            try
            {
                await gate.AcquireAsync(ExportFormat.Pdf, cts.Token);
            }
            finally
            {
                blockerReleased.TrySetResult();
            }
        }, CancellationToken.None);

        await Task.Delay(50);
        Assert.False(waitTask.IsCompleted);

        await cts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => waitTask);
        await blockerReleased.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, gate.ActivePdfExports);
        Assert.True(gate.HasCapacity(ExportFormat.Pdf) == false);

        gate.Release(ExportFormat.Pdf);
        Assert.Equal(0, gate.ActivePdfExports);
    }

    [Fact]
    public async Task DisposeAsync_ReleasesPermit_WhenScopeEnds()
    {
        var options = Options.Create(new ReportingOptions
        {
            MaxConcurrentPdfExports = 1,
            MaxExportDurationSeconds = 30,
        });
        var metrics = new ReportingMetrics();
        var temp = new ReportingTempFileManager(options, NullLogger<ReportingTempFileManager>.Instance, metrics);
        var gate = new ReportingExportConcurrencyGate(options);
        var lifecycle = new ReportingExportLifecycleObserver(
            new NoOpAuditService(),
            new TestCurrentUser(),
            metrics,
            NullLogger<ReportingExportLifecycleObserver>.Instance);
        var scopeFactory = new ReportingExportScopeFactory(gate, temp, lifecycle, metrics);

        await gate.AcquireAsync(ExportFormat.Pdf, CancellationToken.None);
        Assert.Equal(1, gate.ActivePdfExports);

        using var timeout = new CancellationTokenSource();
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var session = new ReportingExportSessionContext(
            new Uqeb.Api.Reporting.DTOs.ReportExportRequestDto(),
            ExportFormat.Pdf,
            "corr",
            temp.CreateSessionDirectory(),
            timeout);
        var scope = scopeFactory.Create(session);
        await scope.DisposeAsync();

        Assert.Equal(0, gate.ActivePdfExports);
        Assert.True(gate.HasCapacity(ExportFormat.Pdf));
    }

    private static ReportingExportConcurrencyGate CreateGate(int maxPdfExports, int waitSeconds) =>
        new(Options.Create(new ReportingOptions
        {
            MaxConcurrentPdfExports = maxPdfExports,
            MaxConcurrentNonPdfExports = maxPdfExports,
            ExportConcurrencyWaitSeconds = waitSeconds,
        }));

    private sealed class NoOpAuditService : Uqeb.Api.Services.IAuditService
    {
        public void TrackLog(
            int userId,
            Uqeb.Api.Models.Enums.AuditAction action,
            string? entityName,
            int? entityId,
            int? transactionId,
            string? oldValue,
            string? newValue) { }

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
