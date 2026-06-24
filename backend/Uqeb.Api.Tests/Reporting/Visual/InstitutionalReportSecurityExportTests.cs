using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting.Visual;

public class InstitutionalReportSecurityExportTests
{
    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://evil.example.com/payload")]
    public void Renderer_EscapesUnsafeUserContent(string payload)
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel(title: payload);
        model.Transactions[0].Subject = payload;
        var manifest = InstitutionalReportVisualFixtures.RenderSections(
            model,
            ReportSectionId.Cover,
            ReportSectionId.TransactionDetails);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("file://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.example.com", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFileName_SanitizesReportNumberPathCharacters()
    {
        var fileName = typeof(InstitutionalReportService)
            .GetMethod("BuildFileName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { "REP/2026\\0001", new ReportExportRequestDto(), "pdf", new List<int> { 1 } })!
            .ToString();

        Assert.DoesNotContain("/", fileName);
        Assert.DoesNotContain("\\", fileName);
    }
}
