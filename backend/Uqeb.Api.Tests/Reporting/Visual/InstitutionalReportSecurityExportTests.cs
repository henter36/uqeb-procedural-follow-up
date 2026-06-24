using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
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

    [Theory]
    [InlineData("javascript:alert(1)", "javascript&#58;")]
    [InlineData("JaVaScRiPt   :alert(1)", "javascript&#58;")]
    [InlineData("file:///etc/passwd", "file&#58;//")]
    [InlineData("FiLe://secret", "file&#58;//")]
    [InlineData("http://example.com", "[رابط خارجي]")]
    [InlineData("https://example.com/path?q=1", "[رابط خارجي]")]
    [InlineData("نص عربي عادي", "نص عربي عادي")]
    [InlineData("<b>html</b>", "&lt;b&gt;html&lt;/b&gt;")]
    public void Esc_SanitizesProtocolsWhilePreservingArabicText(string input, string expectedFragment)
    {
        var result = InvokeEsc(input);
        Assert.Contains(expectedFragment, result);
    }

    [Fact]
    public void Esc_RegexInstances_UseFiniteMatchTimeout()
    {
        foreach (var fieldName in new[] { "JavascriptProtocolRegex", "FileProtocolRegex", "ExternalHttpUrlRegex" })
        {
            var regex = GetRendererRegex(fieldName);
            Assert.NotEqual(Regex.InfiniteMatchTimeout, regex.MatchTimeout);
            Assert.True(regex.MatchTimeout.TotalMilliseconds > 0);
        }
    }

    [Fact]
    public void Esc_LongInput_CompletesWithinReasonableTime()
    {
        var payload = new string('a', 50_000) + "javascript:alert(1)";
        var stopwatch = Stopwatch.StartNew();
        var result = InvokeEsc(payload);
        stopwatch.Stop();

        Assert.Contains("javascript&#58;", result);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void BuildFileName_SanitizesReportNumberPathCharacters()
    {
        var fileName = typeof(InstitutionalReportService)
            .GetMethod("BuildFileName", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { "REP/2026\\0001", new ReportExportRequestDto(), "pdf", new List<int> { 1 } })!
            .ToString();

        Assert.DoesNotContain("/", fileName);
        Assert.DoesNotContain("\\", fileName);
    }

    private static string InvokeEsc(string value) =>
        typeof(InstitutionalReportRenderer)
            .GetMethod("Esc", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object?[] { value })!
            .ToString()!;

    private static Regex GetRendererRegex(string fieldName) =>
        (Regex)typeof(InstitutionalReportRenderer)
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
}
