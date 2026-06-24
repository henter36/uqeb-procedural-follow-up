using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Exporters;
using Uqeb.Api.Reporting.Services;

namespace Uqeb.Api.Tests.Reporting;

internal sealed class TestInstitutionalReportNumberAllocator : IInstitutionalReportNumberAllocator
{
    private int _counter;

    public Task<string> AllocateAsync(CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        var next = Interlocked.Increment(ref _counter);
        return Task.FromResult($"REP-{year}-{next:D6}");
    }
}

internal sealed class TrackingInstitutionalReportNumberAllocator : IInstitutionalReportNumberAllocator
{
    private int _counter;

    public int AllocateCallCount { get; private set; }

    public Task<string> AllocateAsync(CancellationToken ct = default)
    {
        AllocateCallCount++;
        var year = DateTime.UtcNow.Year;
        var next = Interlocked.Increment(ref _counter);
        return Task.FromResult($"REP-{year}-{next:D6}");
    }
}

internal sealed class TestInstitutionalReportPdfExporter : IInstitutionalReportPdfExporter
{
    public Task<byte[]> ExportAsync(RenderedReportManifestDto manifest, string htmlDocument, CancellationToken ct = default) =>
        Task.FromResult("%PDF-1.4"u8.ToArray());
}
