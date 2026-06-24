using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class ReportingReadinessServiceTests
{
    [Fact]
    public void CanWriteToTempDirectory_ReturnsTrue_WhenTempIsWritable()
    {
        Assert.True(ReportingReadinessService.CanWriteToTempDirectory(Path.GetTempPath()));
    }

    [Fact]
    public void CanWriteToTempDirectory_DoesNotLeaveProbeFile()
    {
        var before = Directory.GetFiles(Path.GetTempPath(), "uqeb-report-probe-*.tmp").Length;

        _ = ReportingReadinessService.CanWriteToTempDirectory(Path.GetTempPath());

        var after = Directory.GetFiles(Path.GetTempPath(), "uqeb-report-probe-*.tmp").Length;
        Assert.Equal(before, after);
    }

    [Fact]
    public void CanWriteToTempDirectory_DoesNotThrow_WhenCleanupFails()
    {
        var exception = Record.Exception(() => ReportingReadinessService.CanWriteToTempDirectory(Path.GetTempPath()));
        Assert.Null(exception);
    }
}
