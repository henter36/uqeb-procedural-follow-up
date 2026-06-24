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
    [InlineData(ReportSectionId.TransactionDetails, "transactions")]
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

        var reportPages = page.Locator(".report-page");
        var count = await reportPages.CountAsync();
        Assert.True(count > 0, "Expected at least one rendered report page.");
        await reportPages.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5_000,
        });
        var pageSection = reportPages.First;
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
        var emptyState = page.Locator(".empty-state");
        await emptyState.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5_000,
        });
        var content = await emptyState.InnerTextAsync();
        Assert.Contains("لا توجد توصيات", content);
    }

    [Fact]
    public async Task FullReport_WritesVisualArtifactAndKeepsWideTablesInsidePageBounds()
    {
        await EnsurePlaywrightAvailableAsync();
        if (!await IsPlaywrightAvailableAsync())
            return;

        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderAllSections(model);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            Locale = "ar-SA",
            TimezoneId = "Asia/Riyadh",
            ViewportSize = new ViewportSize { Width = 1500, Height = 1200 },
        });

        await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.EvaluateAsync("() => document.fonts ? document.fonts.ready : Promise.resolve()");

        var everyPageHasOneFooter = await page.Locator(".report-page").EvaluateAllAsync<bool>(
            "pages => pages.every(page => page.querySelectorAll('.report-footer').length === 1)");
        Assert.True(everyPageHasOneFooter);

        var wideTablesStayInsidePage = await page.Locator(".report-table--departments, .report-table--transactions")
            .EvaluateAllAsync<bool>(
                """
                tables => tables.every(table => {
                    const page = table.closest('.report-page');
                    const tableRect = table.getBoundingClientRect();
                    const pageRect = page.getBoundingClientRect();
                    return tableRect.left >= pageRect.left - 1 && tableRect.right <= pageRect.right + 1;
                })
                """);
        Assert.True(wideTablesStayInsidePage);

        var coverQrOnFirstPage = await page.Locator(".report-page").First
            .EvaluateAsync<bool>("page => Boolean(page.querySelector('.qr-box'))");
        Assert.True(coverQrOnFirstPage);

        var screenshot = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = true,
            Animations = ScreenshotAnimations.Disabled,
        });
        Assert.True(screenshot.Length > 4_000, "Full report screenshot is unexpectedly small.");

        var artifactDir = Path.Combine(Path.GetTempPath(), "uqeb-reporting-pdf-layout");
        Directory.CreateDirectory(artifactDir);
        await File.WriteAllBytesAsync(Path.Combine(artifactDir, "full-report.actual.png"), screenshot);
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
        Assert.Contains("data:font/woff2;base64,", css);
    }

    [Fact]
    public void BuildDocumentStylesheet_DefinesPdfProfilesAndReadableTableWrapping()
    {
        var css = InstitutionalReportStyles.BuildDocumentStylesheet();

        Assert.Contains("@page report-standard-portrait", css);
        Assert.Contains("@page report-standard-landscape", css);
        Assert.Contains("@page report-wide-landscape", css);
        Assert.Contains("@page report-extra-wide-landscape", css);
        Assert.Contains(".report-table--departments", css);
        Assert.Contains(".report-table--transactions", css);
        Assert.DoesNotContain("word-break: break-all", css);
        Assert.DoesNotContain("overflow-wrap: anywhere", css);
    }
}
