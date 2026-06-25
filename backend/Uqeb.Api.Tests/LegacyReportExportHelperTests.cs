using Uqeb.Api.Helpers;
using Xunit;

namespace Uqeb.Api.Tests;

public class LegacyReportExportHelperTests
{
    [Theory]
    [InlineData("open", "report-open.xlsx")]
    [InlineData("waiting-replies", "report-waiting-replies.xlsx")]
    [InlineData("../secrets", "report-secrets.xlsx")]
    [InlineData("..\\..\\web.config", "report-web-config.xlsx")]
    [InlineData("", "report-report.xlsx")]
    public void BuildExcelFileName_removes_path_traversal_characters(string reportType, string expectedFileName)
    {
        Assert.Equal(expectedFileName, LegacyReportExportHelper.BuildExcelFileName(reportType));
        Assert.DoesNotContain("..", LegacyReportExportHelper.BuildExcelFileName(reportType));
        Assert.DoesNotContain("\\", LegacyReportExportHelper.BuildExcelFileName(reportType));
    }
}
