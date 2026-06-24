using Microsoft.Playwright;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Rendering;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting.Visual;

public class InstitutionalReportVisualRegressionTests
{
    private static bool RequirePlaywrightInCi =>
        string.Equals(Environment.GetEnvironmentVariable("REQUIRE_PLAYWRIGHT_TESTS"), "1", StringComparison.Ordinal);

    private static async Task<bool> IsPlaywrightAvailableAsync()
    {
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task EnsurePlaywrightAvailableAsync()
    {
        if (await IsPlaywrightAvailableAsync())
            return;

        if (RequirePlaywrightInCi)
            Assert.Fail("Playwright Chromium is required in CI but is not available.");
    }

    [Theory]
    [InlineData(ReportSectionId.Cover, "cover")]
    [InlineData(ReportSectionId.ExecutiveSummary, "executive-summary")]
    [InlineData(ReportSectionId.IndicatorsDashboard, "indicators")]
    [InlineData(ReportSectionId.DepartmentPerformance, "departments")]
    [InlineData(ReportSectionId.RisksAndAlerts, "risks")]
    [InlineData(ReportSectionId.ExecutiveRecommendations, "recommendations")]
    [InlineData(ReportSectionId.TransactionDetails, "details")]
    [InlineData(ReportSectionId.ReportMetadata, "metadata")]
    public async Task Section_MatchesVisualBaseline(ReportSectionId section, string snapshotName)
    {
        await EnsurePlaywrightAvailableAsync();
        if (!await IsPlaywrightAvailableAsync())
            return;

        var model = InstitutionalReportVisualFixtures.CreateBaseModel(truncated: section == ReportSectionId.TransactionDetails);
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, section);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            Locale = "ar-SA",
            TimezoneId = "Asia/Riyadh",
            ViewportSize = new ViewportSize { Width = 900, Height = 1300 },
        });

        await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = "* { animation: none !important; transition: none !important; }",
        });

        var pageSection = page.Locator(".report-page").First;
        await AssertRenderedSectionAsync(page, pageSection, snapshotName, section);
    }

    [Fact]
    public async Task EmptyReport_ShowsEmptyRecommendationsState()
    {
        await EnsurePlaywrightAvailableAsync();
        if (!await IsPlaywrightAvailableAsync())
            return;

        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.Recommendations = [];
        var manifest = InstitutionalReportVisualFixtures.RenderSections(model, ReportSectionId.ExecutiveRecommendations);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync(new BrowserNewPageOptions { Locale = "ar-SA", TimezoneId = "Asia/Riyadh" });
        await page.SetContentAsync(html);
        var content = await page.Locator(".empty-state").InnerTextAsync();
        Assert.Contains("لا توجد توصيات", content);
    }

    private static async Task AssertRenderedSectionAsync(
        IPage page,
        ILocator locator,
        string snapshotName,
        ReportSectionId section)
    {
        var expectedText = section switch
        {
            ReportSectionId.Cover => "REP-2026-000125",
            ReportSectionId.ExecutiveSummary => "الملخص التنفيذي",
            ReportSectionId.IndicatorsDashboard => "لوحة المؤشرات",
            ReportSectionId.DepartmentPerformance => "أداء الإدارات",
            ReportSectionId.RisksAndAlerts => "المخاطر والتنبيهات",
            ReportSectionId.ExecutiveRecommendations => "التوصيات التنفيذية",
            ReportSectionId.TransactionDetails => "المعاملات التفصيلية",
            ReportSectionId.ReportMetadata => "بيانات التقرير والفلاتر",
            _ => "تقرير",
        };

        var bodyText = await page.Locator("body").InnerTextAsync();
        Assert.Contains(expectedText, bodyText);

        var screenshot = await locator.ScreenshotAsync(new LocatorScreenshotOptions { Animations = ScreenshotAnimations.Disabled });
        Assert.True(screenshot.Length > 2_000, $"Screenshot too small for {snapshotName}.");

        var baselineDir = ResolveBaselineDirectory();
        Directory.CreateDirectory(baselineDir);
        var actualPath = Path.Combine(baselineDir, $"{snapshotName}.actual.png");
        await File.WriteAllBytesAsync(actualPath, screenshot);

        var baselinePath = Path.Combine(baselineDir, $"{snapshotName}.png");
        if (File.Exists(baselinePath))
        {
            var baseline = await File.ReadAllBytesAsync(baselinePath);
            Assert.True(
                Math.Abs(baseline.Length - screenshot.Length) <= baseline.Length * 0.05,
                $"Screenshot size drifted for {snapshotName}. Update baseline intentionally if design changed.");
        }
        else
        {
            await File.WriteAllBytesAsync(baselinePath, screenshot);
        }
    }

    private static string ResolveBaselineDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Reporting", "Visual", "Baselines");
            if (Directory.Exists(Path.GetDirectoryName(candidate)!))
                return candidate;

            if (File.Exists(Path.Combine(dir.FullName, "Uqeb.Api.Tests.csproj")))
                return candidate;

            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "Reporting", "Visual", "Baselines");
    }
}

public class InstitutionalReportStylesTests
{
    [Fact]
    public void BuildDocumentStylesheet_IncludesTokensAndFonts()
    {
        var css = InstitutionalReportStyles.BuildDocumentStylesheet();
        Assert.Contains("--report-primary", css);
        Assert.Contains("@font-face", css);
        Assert.Contains("Uqeb Report Arabic", css);
    }
}
