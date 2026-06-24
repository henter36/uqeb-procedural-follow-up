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
        var options = Options.Create(new ReportingOptions
        {
            MaxConcurrentPdfExports = 1,
            MaxConcurrentNonPdfExports = 1,
            ExportConcurrencyWaitSeconds = 1,
        });
        using var gate = new ReportingExportConcurrencyGate(options);
        await gate.AcquireAsync(ExportFormat.Pdf, CancellationToken.None);

        await Assert.ThrowsAsync<ReportingExportRejectedException>(() =>
            gate.AcquireAsync(ExportFormat.Pdf, CancellationToken.None));
    }

    [Fact]
    public async Task Release_AllowsNextAcquisition()
    {
        var options = Options.Create(new ReportingOptions
        {
            MaxConcurrentPdfExports = 1,
            ExportConcurrencyWaitSeconds = 1,
        });
        using var gate = new ReportingExportConcurrencyGate(options);
        await gate.AcquireAsync(ExportFormat.Pdf, CancellationToken.None);
        gate.Release(ExportFormat.Pdf);
        await gate.AcquireAsync(ExportFormat.Pdf, CancellationToken.None);
        gate.Release(ExportFormat.Pdf);
    }
}
