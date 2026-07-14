using System.Text.Json;
using System.Text.RegularExpressions;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Rendering;
using Uqeb.Api.Reporting.Services;
using Uqeb.Api.Tests.Reporting.Visual;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportRendererTests
{
    private const string TestReportTitle = "تقرير متابعة المعاملات المتأخرة";
    private const string TestReportId = "REP-2026-000125";

    private readonly InstitutionalReportRenderer _renderer = new();

    [Fact]
    public void BuildExportManifest_PreservesOperationalMetadata()
    {
        var source = CreateSourceManifest(3);
        source.TotalMatchedRows = 12_500;
        source.ExportedDetailRows = 500;
        source.DetailRowsTruncated = true;
        source.DetailPartsCount = 3;
        source.LoadedDetailRows = 500;
        source.TemplateVersion = "2026.06.1";
        source.Stylesheet = "body { margin: 0; }";
        source.OverflowAction = DetailOverflowAction.SplitPdf;
        source.FileFingerprint = "fp-test";
        source.IsSummaryOnly = false;

        var result = _renderer.BuildExportManifest(source, [1, 2], new ReportExportRequestDto
        {
            IncludePartialCover = true,
            IncludePartialManifest = true,
            PageNumberingMode = PageNumberingMode.Restart,
        });

        Assert.Equal(source.LoadedDetailRows, result.LoadedDetailRows);
        Assert.Equal(source.TemplateVersion, result.TemplateVersion);
        Assert.Equal(source.TotalMatchedRows, result.TotalMatchedRows);
        Assert.Equal(source.ExportedDetailRows, result.ExportedDetailRows);
        Assert.Equal(source.DetailPartsCount, result.DetailPartsCount);
        Assert.Equal(source.Stylesheet, result.Stylesheet);
        Assert.Equal(source.OverflowAction, result.OverflowAction);
        Assert.Equal(source.FileFingerprint, result.FileFingerprint);
        Assert.Equal(source.IsSummaryOnly, result.IsSummaryOnly);
        Assert.Equal(source.ReportTitle, result.ReportTitle);
        Assert.Equal(source.ReportId, result.ReportId);
        Assert.True(result.IsPartialExport);
    }

    [Fact]
    public void BuildExportManifest_PreservesAnalysisPayload()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var source = InstitutionalReportVisualFixtures.RenderSections(
            model,
            ReportSectionId.KeyPerformanceIndicators,
            ReportSectionId.SignificantFindings);

        var result = _renderer.BuildExportManifest(source, [source.Pages[0].OriginalPageNumber], new ReportExportRequestDto
        {
            IncludePartialCover = true,
            IncludePartialManifest = true,
        });

        Assert.Same(source.Analysis, result.Analysis);
    }

    [Fact]
    public void BuildExportManifest_OriginalNumberingStillUsesFinalExportPageCount()
    {
        var source = CreateSourceManifest(4);
        source.TotalMatchedRows = 100;
        source.ExportedDetailRows = 100;
        source.LoadedDetailRows = 100;
        source.TemplateVersion = "2026.06.1";

        var result = _renderer.BuildExportManifest(source, [2, 3], new ReportExportRequestDto
        {
            PageNumberingMode = PageNumberingMode.Original,
        });

        Assert.Equal(source.LoadedDetailRows, result.LoadedDetailRows);
        Assert.Equal(source.TemplateVersion, result.TemplateVersion);
        Assert.Equal(source.ReportTitle, result.ReportTitle);
        Assert.Equal(source.ReportId, result.ReportId);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal([1, 2], result.Pages.Select(p => p.RenderedPageNumber));
        Assert.All(result.Pages, page => Assert.Contains(" من 2", page.HtmlContent));
    }

    [Fact]
    public void BuildExportManifest_RenumbersPartialCoverAndManifestPages()
    {
        var source = CreateSourceManifest(3);
        var request = new ReportExportRequestDto
        {
            IncludePartialCover = true,
            IncludePartialManifest = true,
            PageNumberingMode = PageNumberingMode.Restart,
        };

        var manifest = _renderer.BuildExportManifest(source, [1, 2], request);

        Assert.Equal(4, manifest.Pages.Count);
        Assert.Equal(1, manifest.Pages[0].RenderedPageNumber);
        Assert.Equal(2, manifest.Pages[1].RenderedPageNumber);
        Assert.DoesNotContain("الصفحة 0 من 0", manifest.Pages[0].HtmlContent);
        Assert.DoesNotContain("الصفحة 0 من 0", manifest.Pages[1].HtmlContent);
        Assert.Contains("الصفحة 1 من 4", manifest.Pages[0].HtmlContent);
        Assert.Contains("الصفحة 4 من 4", manifest.Pages[^1].HtmlContent);
    }

    [Fact]
    public void RenderManifest_UsesRealReportTitleAndReportIdInFooter()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel(title: TestReportTitle);
        model.Metadata.ReportNumber = TestReportId;

        var manifest = _renderer.RenderManifest(model, [ReportSectionId.Cover, ReportSectionId.ExecutiveSummary]);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.Equal(TestReportTitle, manifest.ReportTitle);
        Assert.Equal(TestReportId, manifest.ReportId);
        Assert.Contains($"""<span class="footer-title">{TestReportTitle}</span>""", html);
        Assert.Contains($"""<span class="footer-id">{TestReportId}</span>""", html);
        Assert.DoesNotContain($"""<span class="footer-title">{TestReportId}</span>""", html);
    }

    [Fact]
    public void RenderManifest_CoverShowsFixedTitleAndPeriodOnly()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel(title: "عنوان داخلي لا يظهر في الغلاف");
        model.Metadata.PeriodFrom = new DateTime(2026, 1, 1);
        model.Metadata.PeriodTo = new DateTime(2026, 6, 15);
        model.Metadata.IssueDate = new DateTime(2026, 6, 20);
        model.Metadata.ReportNumber = "REP-COVER-HIDDEN";

        var manifest = _renderer.RenderManifest(model, [ReportSectionId.Cover]);
        var coverHtml = Assert.Single(manifest.Pages).HtmlContent;

        Assert.Contains("<h1 class=\"cover-title\">تقرير المتابعة الإجرائية</h1>", coverHtml);
        Assert.Contains("<div class=\"cover-period\">الفترة: من 2026-01-01 إلى 2026-06-15</div>", coverHtml);
        Assert.DoesNotContain("تاريخ الإصدار", coverHtml);
        Assert.DoesNotContain("2026-06-20", coverHtml);
        Assert.DoesNotContain("رقم التقرير", coverHtml);
        Assert.DoesNotContain("REP-COVER-HIDDEN", coverHtml);
        Assert.DoesNotContain("عنوان داخلي لا يظهر في الغلاف", coverHtml);
        Assert.DoesNotContain("report-header", coverHtml);
        Assert.DoesNotContain("report-footer", coverHtml);
        Assert.Equal("عنوان داخلي لا يظهر في الغلاف", manifest.ReportTitle);
    }

    [Theory]
    [InlineData(PageNumberingMode.Restart)]
    [InlineData(PageNumberingMode.Original)]
    public void BuildExportManifest_PartialExportKeepsRealFooterTitleAndReportId(PageNumberingMode numberingMode)
    {
        var source = CreateSourceManifest(3);
        var manifest = _renderer.BuildExportManifest(source, [2], new ReportExportRequestDto
        {
            IncludePartialCover = true,
            IncludePartialManifest = true,
            PageNumberingMode = numberingMode,
        });

        Assert.Equal(source.ReportTitle, manifest.ReportTitle);
        Assert.Equal(source.ReportId, manifest.ReportId);
        Assert.Contains(manifest.Pages, page => page.SectionId == ReportSectionId.PartialCover);
        Assert.Contains(manifest.Pages, page => page.SectionId == ReportSectionId.PartialManifest);
        Assert.All(manifest.Pages, page =>
        {
            Assert.Contains($"""<span class="footer-title">نسخة جزئية — {source.ReportTitle}</span>""", page.HtmlContent);
            Assert.Contains($"""<span class="footer-id">{source.ReportId}</span>""", page.HtmlContent);
            Assert.DoesNotContain($"""<span class="footer-title">نسخة جزئية — {source.ReportId}</span>""", page.HtmlContent);
        });
    }

    [Fact]
    public void InjectFooter_ReplacesExistingFooterInsteadOfDuplicating()
    {
        var source = CreateSourceManifest(1);
        var request = new ReportExportRequestDto
        {
            PageNumberingMode = PageNumberingMode.Restart,
        };

        var manifest = _renderer.BuildExportManifest(source, [1], request);
        var html = manifest.Pages[0].HtmlContent;

        Assert.Equal(1, CountOccurrences(html, "<footer class=\"report-footer\">"));
        Assert.Contains($"""<span class="footer-title">{source.ReportTitle}</span>""", html);
        Assert.Contains($"""<span class="footer-id">{source.ReportId}</span>""", html);
        Assert.DoesNotContain($"""<span class="footer-title">{source.ReportId}</span>""", html);
        Assert.Contains("الصفحة 1 من 1", html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildExportManifest_UsesDefaultTitleFallbackWithoutUsingReportId(string? reportTitle)
    {
        var source = CreateSourceManifest(1);
        source.ReportTitle = reportTitle!;

        var manifest = _renderer.BuildExportManifest(source, [1], new ReportExportRequestDto());
        var html = manifest.Pages.Single().HtmlContent;

        Assert.Equal("تقرير المتابعة الإجرائية للمعاملات", manifest.ReportTitle);
        Assert.Contains("""<span class="footer-title">تقرير المتابعة الإجرائية للمعاملات</span>""", html);
        Assert.Contains($"""<span class="footer-id">{source.ReportId}</span>""", html);
        Assert.DoesNotContain($"""<span class="footer-title">{source.ReportId}</span>""", html);
    }

    [Fact]
    public void RenderedReportManifestDto_SerializesReportTitleAndReportIdSeparately()
    {
        var manifest = new RenderedReportManifestDto
        {
            ReportTitle = TestReportTitle,
            ReportId = TestReportId,
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        using var document = JsonDocument.Parse(json);
        Assert.Equal(TestReportTitle, document.RootElement.GetProperty("reportTitle").GetString());
        Assert.Equal(TestReportId, document.RootElement.GetProperty("reportId").GetString());
    }

    [Fact]
    public void RenderManifest_AssignsExpectedPdfProfilesBySection()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = _renderer.RenderManifest(model,
        [
            ReportSectionId.Cover,
            ReportSectionId.ExecutiveSummary,
            ReportSectionId.IndicatorsDashboard,
            ReportSectionId.DepartmentPerformance,
            ReportSectionId.OutstandingAndImprovedDepartments,
            ReportSectionId.TransactionDetails,
            ReportSectionId.ReportMetadata,
        ]);

        Assert.Equal("StandardPortrait", manifest.Pages.Single(p => p.SectionId == ReportSectionId.Cover).PdfProfileName);
        Assert.Equal("StandardPortrait", manifest.Pages.Single(p => p.SectionId == ReportSectionId.ExecutiveSummary).PdfProfileName);
        Assert.Equal("StandardLandscape", manifest.Pages.Single(p => p.SectionId == ReportSectionId.IndicatorsDashboard).PdfProfileName);
        Assert.Equal("WideLandscape", manifest.Pages.Single(p => p.SectionId == ReportSectionId.DepartmentPerformance).PdfProfileName);
        Assert.Equal("WideLandscape", manifest.Pages.Single(p => p.SectionId == ReportSectionId.OutstandingAndImprovedDepartments).PdfProfileName);
        Assert.All(
            manifest.Pages.Where(p => p.SectionId == ReportSectionId.TransactionDetails),
            page => Assert.Equal("ExtraWideLandscape", page.PdfProfileName));
        Assert.Equal("StandardPortrait", manifest.Pages.Single(p => p.SectionId == ReportSectionId.ReportMetadata).PdfProfileName);
    }

    [Fact]
    public void RenderManifest_RendersDepartmentRecognitionsAsIndependentSection()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.Analysis.DepartmentRecognitions =
        [
            new DepartmentRecognitionRowDto
            {
                DepartmentId = 1,
                DepartmentName = "إدارة التميز",
                RecognitionType = "متميزة",
                TransactionCount = 12,
                OnTimeCompletionRate = 91,
                OverdueCount = 0,
                AverageCompletionDays = 2.5,
                DataCompletenessRate = 98,
                Reason = "ارتفاع نسبة الإنجاز في الوقت",
                Score = 95,
                HasSufficientSample = true,
            },
        ];

        var manifest = _renderer.RenderManifest(model, [ReportSectionId.OutstandingAndImprovedDepartments]);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.Single(manifest.Pages);
        Assert.Equal(ReportSectionId.OutstandingAndImprovedDepartments, manifest.Pages[0].SectionId);
        Assert.Contains("الإدارات المتميزة والأكثر تحسنًا", html);
        Assert.Contains("إدارة التميز", html);
        Assert.Contains("ارتفاع نسبة الإنجاز في الوقت", html);
    }

    [Fact]
    public void RenderManifest_TransactionDetails_FitsMoreThanSixRowsPerPageOnExtraWideLandscape()
    {
        // Regression guard: general TransactionDetails pages used to hard-cap at 6 rows via a
        // fixed constant regardless of how much room ExtraWideLandscape actually has. Row
        // capacity must now come from real page geometry (ComputeRowsPerPage), same as the
        // DepartmentTransactions detail table already does.
        var model = InstitutionalReportVisualFixtures.CreateBaseModel(totalMatched: 13, exportedRows: 13);

        var manifest = _renderer.RenderManifest(model, [ReportSectionId.TransactionDetails]);

        var detailPages = manifest.Pages.Where(p => p.SectionId == ReportSectionId.TransactionDetails).ToList();
        Assert.NotEmpty(detailPages);

        var firstPageRowCount = CountTableBodyRows(detailPages[0].HtmlContent);
        Assert.True(firstPageRowCount > 6, $"Expected more than 6 rows on the first page, got {firstPageRowCount}.");

        Assert.All(detailPages, page => Assert.Equal("ExtraWideLandscape", page.PdfProfileName));
    }

    [Fact]
    public void RenderManifestWithMeasuredTransactionPages_RebuildsDetailsFromMeasuredChunks()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel(totalMatched: 7, exportedRows: 7);
        model.Transactions = InstitutionalReportVisualFixtures.CreateTransactions(7);
        var chunks = new IReadOnlyList<TransactionDetailRowDto>[]
        {
            model.Transactions.Take(3).ToList(),
            model.Transactions.Skip(3).Take(4).ToList(),
            Array.Empty<TransactionDetailRowDto>(),
        };

        var manifest = _renderer.RenderManifestWithMeasuredTransactionPages(
            model,
            [ReportSectionId.Cover, ReportSectionId.TransactionDetails, ReportSectionId.ReportMetadata],
            chunks);

        var detailPages = manifest.Pages.Where(p => p.SectionId == ReportSectionId.TransactionDetails).ToList();

        Assert.Equal(2, detailPages.Count);
        Assert.DoesNotContain(detailPages, page => CountTableBodyRows(page.HtmlContent) == 0);
        Assert.All(detailPages, page => Assert.Equal("ExtraWideLandscape", page.PdfProfileName));
        Assert.Equal(Enumerable.Range(1, manifest.TotalPages), manifest.Pages.Select(p => p.OriginalPageNumber));
        Assert.Equal(Enumerable.Range(1, manifest.TotalPages), manifest.Pages.Select(p => p.RenderedPageNumber));
        Assert.Contains("الصفحة 2 من 4", detailPages[0].HtmlContent);
        Assert.Contains("الصفحة 3 من 4", detailPages[1].HtmlContent);
    }

    [Fact]
    public void BuildExportManifest_MeasuredTransactionPages_DoesNotDuplicateFootersOrBreakSelection()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel(totalMatched: 5, exportedRows: 5);
        model.Transactions = InstitutionalReportVisualFixtures.CreateTransactions(5);
        var chunks = new IReadOnlyList<TransactionDetailRowDto>[]
        {
            model.Transactions.Take(2).ToList(),
            model.Transactions.Skip(2).ToList(),
        };
        var source = _renderer.RenderManifestWithMeasuredTransactionPages(
            model,
            [ReportSectionId.Cover, ReportSectionId.TransactionDetails, ReportSectionId.ReportMetadata],
            chunks);
        var selectedPages = source.Pages
            .Where(p => p.SectionId == ReportSectionId.TransactionDetails)
            .Select(p => p.OriginalPageNumber)
            .ToList();

        var exportManifest = _renderer.BuildExportManifest(source, selectedPages, new ReportExportRequestDto
        {
            IncludePartialCover = true,
            IncludePartialManifest = true,
            PageNumberingMode = PageNumberingMode.Restart,
        });

        Assert.Equal(4, exportManifest.Pages.Count);
        Assert.Equal(ReportSectionId.PartialCover, exportManifest.Pages[0].SectionId);
        Assert.Equal(ReportSectionId.PartialManifest, exportManifest.Pages[1].SectionId);
        Assert.Equal([1, 2, 3, 4], exportManifest.Pages.Select(p => p.RenderedPageNumber));
        Assert.All(exportManifest.Pages, page => Assert.Equal(1, CountOccurrences(page.HtmlContent, "<footer class=\"report-footer")));
        Assert.All(
            exportManifest.Pages.Where(p => p.SectionId == ReportSectionId.TransactionDetails),
            page => Assert.Equal("ExtraWideLandscape", page.PdfProfileName));
    }

    [Fact]
    public void RenderManifest_ReportMetadataShowsFinalTotalPages()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = _renderer.RenderManifest(model,
        [
            ReportSectionId.Cover,
            ReportSectionId.TransactionDetails,
            ReportSectionId.ReportMetadata,
        ]);

        var metadata = Assert.Single(manifest.Pages, p => p.SectionId == ReportSectionId.ReportMetadata);
        Assert.Equal(manifest.TotalPages, manifest.Pages.Count);
        Assert.Contains($"""<dd data-report-total-pages="true">{manifest.TotalPages:N0}</dd>""", metadata.HtmlContent);
    }

    [Fact]
    public void BuildExportManifest_ReportMetadataUsesSelectedExportPageCount()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var source = _renderer.RenderManifest(model,
        [
            ReportSectionId.Cover,
            ReportSectionId.TransactionDetails,
            ReportSectionId.ReportMetadata,
        ]);
        var metadataOriginalPage = source.Pages.Single(p => p.SectionId == ReportSectionId.ReportMetadata).OriginalPageNumber;

        var exportManifest = _renderer.BuildExportManifest(source, [metadataOriginalPage], new ReportExportRequestDto());

        var metadata = Assert.Single(exportManifest.Pages);
        Assert.Equal(1, exportManifest.TotalPages);
        Assert.Contains("""<dd data-report-total-pages="true">1</dd>""", metadata.HtmlContent);
        Assert.Contains("الصفحة 1 من 1", metadata.HtmlContent);
    }

    [Fact]
    public void BuildExportManifest_MetadataTotalPagesUpdateDoesNotDependOnLocalizedLabel()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var source = _renderer.RenderManifest(model,
        [
            ReportSectionId.Cover,
            ReportSectionId.ReportMetadata,
        ]);
        var metadataIndex = source.Pages.FindIndex(p => p.SectionId == ReportSectionId.ReportMetadata);
        source.Pages[metadataIndex] = source.Pages[metadataIndex] with
        {
            HtmlContent = source.Pages[metadataIndex].HtmlContent.Replace("إجمالي الصفحات", "عدد صفحات الملف", StringComparison.Ordinal),
        };

        var exportManifest = _renderer.BuildExportManifest(source, [source.Pages[metadataIndex].OriginalPageNumber], new ReportExportRequestDto());

        var metadata = Assert.Single(exportManifest.Pages);
        Assert.Contains("عدد صفحات الملف", metadata.HtmlContent);
        Assert.Contains("""<dd data-report-total-pages="true">1</dd>""", metadata.HtmlContent);
    }

    [Fact]
    public void RenderManifest_UsesTableSpecificClassesAndReadableDateOrder()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = _renderer.RenderManifest(model,
        [
            ReportSectionId.DepartmentPerformance,
            ReportSectionId.TransactionDetails,
            ReportSectionId.ReportMetadata,
        ]);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.Contains("report-table--departments", html);
        Assert.Contains("report-table--transactions", html);
        Assert.Contains("cell--id", html);
        Assert.Contains("cell--date", html);
        Assert.Contains("الفترة من 2026-01-01 إلى 2026-06-15", html);
        Assert.Contains("<dt>الفترة</dt><dd>من 2026-01-01 إلى 2026-06-15</dd>", html);
    }

    private static int CountTableBodyRows(string html)
    {
        var tbodyMatch = Regex.Match(
            html,
            "<tbody[^>]*>(.*?)</tbody>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(250));

        if (!tbodyMatch.Success)
            return 0;

        return Regex.Matches(
            tbodyMatch.Groups[1].Value,
            "<tr\\b",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(250)).Count;
    }

    private static DepartmentTimeSeriesPointDto DepartmentPoint(string departmentName, string periodLabel, DateTime periodStart) =>
        new()
        {
            DepartmentName = departmentName,
            PeriodLabel = periodLabel,
            PeriodStart = periodStart,
            IncomingCount = 1,
        };

    [Fact]
    public void RenderManifest_RendersAnalyticalSectionsFromUnifiedAnalysisModel()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = _renderer.RenderManifest(model,
        [
            ReportSectionId.KeyPerformanceIndicators,
            ReportSectionId.SignificantFindings,
            ReportSectionId.CriticalCases,
            ReportSectionId.TimeTrends,
            ReportSectionId.ExternalPartyAnalysis,
            ReportSectionId.ClassificationAndPriorityAnalysis,
            ReportSectionId.DelayAndBottleneckAnalysis,
            ReportSectionId.DataQuality,
            ReportSectionId.RecommendationsAndActionPlan,
            ReportSectionId.MethodologyAndDefinitions,
        ]);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.Equal(10, manifest.Pages.Count);
        Assert.Same(model.Analysis, manifest.Analysis);
        Assert.Contains("مؤشرات الأداء الرئيسية", html);
        Assert.Contains("ارتفاع نسبة التأخر", html);
        Assert.Contains("معاملة حرجة متأخرة", html);
        Assert.Contains("جهة حكومية", html);
        Assert.Contains("إفادة أو تكليف إدارة معلق", html);
        Assert.Contains("نسبة اكتمال البيانات", html);
        Assert.Contains("مراجعة المعاملات المتأخرة حسب الإدارات الأعلى أثرًا", html);
        Assert.Contains("متوسط ساعات أول إجراء", html);
    }

    [Fact]
    public void RenderManifest_DepartmentTimeSeriesOrdersRowsByDepartmentThenPeriod()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.Analysis.DepartmentTimeSeries =
        [
            DepartmentPoint("B Department", "2026-06", new DateTime(2026, 6, 1)),
            DepartmentPoint("A Department", "2026-07", new DateTime(2026, 7, 1)),
            DepartmentPoint("A Department", "2026-06", new DateTime(2026, 6, 1)),
            DepartmentPoint("B Department", "2026-05", new DateTime(2026, 5, 1)),
        ];

        var manifest = _renderer.RenderManifest(model, [ReportSectionId.TimeTrends]);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.True(
            html.IndexOf("<td>2026-06</td><td class=\"cell--department\">A Department</td>", StringComparison.Ordinal) <
            html.IndexOf("<td>2026-07</td><td class=\"cell--department\">A Department</td>", StringComparison.Ordinal));
        Assert.True(
            html.IndexOf("<td>2026-05</td><td class=\"cell--department\">B Department</td>", StringComparison.Ordinal) <
            html.IndexOf("<td>2026-06</td><td class=\"cell--department\">B Department</td>", StringComparison.Ordinal));
        Assert.True(
            html.IndexOf("<td>2026-07</td><td class=\"cell--department\">A Department</td>", StringComparison.Ordinal) <
            html.IndexOf("<td>2026-05</td><td class=\"cell--department\">B Department</td>", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderManifest_TransactionDetailsLabelsElapsedDaysAsTransactionAge()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel(totalMatched: 1, exportedRows: 1);

        var manifest = _renderer.RenderManifest(model, [ReportSectionId.TransactionDetails]);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.Contains("<th>عمر المعاملة</th>", html);
        Assert.DoesNotContain("<th>الأيام</th>", html);
    }

    [Fact]
    public void RenderManifest_LocalizesApprovalStatusAndDoesNotRenderQrPlaceholder()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.Analysis.Methodology.ApprovalStatus = "Draft";

        var manifest = _renderer.RenderManifest(model,
        [
            ReportSectionId.Cover,
            ReportSectionId.MethodologyAndDefinitions,
        ]);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.Contains("مسودة", html);
        Assert.DoesNotContain("Draft", html);
        Assert.DoesNotContain(">QR", html);
        Assert.DoesNotContain("qr-box", html);
        Assert.DoesNotContain("تقرير رسمي", html);
    }

    [Fact]
    public void RenderManifest_DoesNotExposeTechnicalCodesOrRawRecommendationEnums()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.Analysis.Recommendations.Add(new AnalyticalRecommendationDto
        {
            RecommendationId = "REC-CODES",
            SourceFindingCode = "ISSUE_QUALITY_DATA",
            Priority = "medium",
            RecommendationText = "RESPONSES_PENDING_EXTERNAL",
            ResponsibleScope = "إدارة المتابعة",
            SuggestedDueDays = 5,
            Status = "Proposed",
        });
        model.Analysis.Recommendations.Add(new AnalyticalRecommendationDto
        {
            RecommendationId = "REC-EXTERNAL-CODE",
            SourceFindingCode = "EXTERNAL_PENDING_RESPONSES",
            Priority = "medium",
            RecommendationText = "متابعة الردود المنتظرة.",
            ResponsibleScope = "إدارة المتابعة",
            SuggestedDueDays = 5,
            Status = "Proposed",
        });
        model.IntegrityWarnings.Add(new IntegrityWarningDto
        {
            Code = "CASES_CRITICAL",
            Message = "ISSUE_QUALITY_DATA",
        });

        var manifest = _renderer.RenderManifest(model,
        [
            ReportSectionId.RecommendationsAndActionPlan,
            ReportSectionId.ReportMetadata,
            ReportSectionId.MethodologyAndDefinitions,
        ]);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.Contains("متوسطة", html);
        Assert.Contains("مقترحة", html);
        Assert.Contains("جودة البيانات", html);
        Assert.Contains("متابعة الردود الخارجية", html);
        Assert.Contains("معاملات منتظرة من جهة خارجية", html);
        Assert.Contains("مراجعة الحالات الحرجة", html);
        Assert.DoesNotContain(">medium<", html);
        Assert.DoesNotContain(">high<", html);
        Assert.DoesNotContain("Proposed", html);
        Assert.DoesNotContain("ISSUE_QUALITY_DATA", html);
        Assert.DoesNotContain("RESPONSES_PENDING_EXTERNAL", html);
        Assert.DoesNotContain("EXTERNAL_PENDING_RESPONSES", html);
        Assert.DoesNotContain("CASES_CRITICAL", html);
    }

    [Fact]
    public void RenderManifest_RecommendationsClassifyUndefinedOwnerAsDataQuality()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.Recommendations =
        [
            new RecommendationRowDto
            {
                Priority = "medium",
                Observation = "توجد معاملات بلا إدارة",
                RequiredAction = "استكمال بيانات الإدارة المختصة.",
                ResponsibleDepartment = "—",
                SourceLabel = "DATA_QUALITY_ISSUE",
            },
        ];
        model.Analysis.Recommendations =
        [
            new AnalyticalRecommendationDto
            {
                Priority = "medium",
                SourceFindingCode = "DATA_QUALITY_ISSUE",
                RecommendationText = "استكمال بيانات الإدارة المختصة.",
                ResponsibleScope = "غير محدد",
                SuggestedDueDays = 10,
                Status = "proposed",
            },
        ];

        var manifest = _renderer.RenderManifest(model,
        [
            ReportSectionId.ExecutiveRecommendations,
            ReportSectionId.RecommendationsAndActionPlan,
        ]);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.Contains("ملاحظة جودة بيانات: معاملات بلا إدارة مختصة", html);
        Assert.Contains("<td>مالك البيانات</td>", html);
        Assert.DoesNotContain("الإدارة: غير محدد", html);
        Assert.DoesNotContain("<td>غير محدد</td>", html);
    }

    [Fact]
    public void RenderManifest_RisksIncludeCriticalCasesInsteadOfShowingCriticalEmptyState()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        model.Risks.Clear();

        var manifest = _renderer.RenderManifest(model, [ReportSectionId.CriticalCases, ReportSectionId.RisksAndAlerts]);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.Contains("حالة حرجة: معاملة حرجة متأخرة", html);
        Assert.Contains("""<div class="risk-group critical">""", html);
        Assert.Contains("""<table class="report-table"><thead><tr><th>م</th><th>التنبيه</th>""", html);
    }

    [Fact]
    public void RenderManifest_DepartmentPerformanceOmitsRatingColumnAndDataQualityNote()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();

        var manifest = _renderer.RenderManifest(model, [ReportSectionId.DepartmentPerformance]);
        var html = InstitutionalReportRenderer.RenderHtmlDocument(manifest);

        Assert.Contains("<td>الإجمالي</td>", html);
        Assert.DoesNotContain("<th>التقييم</th>", html);
        Assert.DoesNotContain("ملاحظة جودة بيانات", html);
        Assert.DoesNotContain("غير محدد", html);
    }

    [Fact]
    public void RenderManifest_DoesNotCreateTitleOnlyContentPages()
    {
        var model = InstitutionalReportVisualFixtures.CreateBaseModel();
        var manifest = InstitutionalReportVisualFixtures.RenderAllSections(model);

        foreach (var page in manifest.Pages.Where(p => p.SectionId != ReportSectionId.Cover))
        {
            Assert.Contains("<h2 class=\"section-title\"", page.HtmlContent);
            Assert.True(
                page.HtmlContent.Contains("<table", StringComparison.Ordinal)
                || page.HtmlContent.Contains("-card", StringComparison.Ordinal)
                || page.HtmlContent.Contains("empty-state", StringComparison.Ordinal)
                || page.HtmlContent.Contains("partial-note", StringComparison.Ordinal),
                $"Page {page.OriginalPageNumber} ({page.SectionName}) appears to contain only a title.");
        }
    }

    [Fact]
    public void PdfProfiles_DefineWideReadablePageSizes()
    {
        Assert.True(InstitutionalReportPdfProfiles.StandardLandscape.WidthMm > InstitutionalReportPdfProfiles.StandardPortrait.WidthMm);
        Assert.True(InstitutionalReportPdfProfiles.WideLandscape.WidthMm > InstitutionalReportPdfProfiles.StandardLandscape.WidthMm);
        Assert.True(InstitutionalReportPdfProfiles.ExtraWideLandscape.WidthMm >= InstitutionalReportPdfProfiles.WideLandscape.WidthMm);
        Assert.True(InstitutionalReportPdfProfiles.ExtraWideLandscape.TableFontSizePx >= 9);
    }

    [Fact]
    public void WidestProfile_SelectsLandscapeOverPortraitByWidth()
    {
        var profile = InstitutionalReportPdfProfiles.WidestProfile(
        [
            InstitutionalReportPdfProfiles.StandardPortrait.Name,
            InstitutionalReportPdfProfiles.StandardLandscape.Name,
        ]);

        Assert.Same(InstitutionalReportPdfProfiles.StandardLandscape, profile);
    }

    [Theory]
    [InlineData("WideLandscape", "ExtraWideLandscape")]
    [InlineData("ExtraWideLandscape", "WideLandscape")]
    public void WidestProfile_SelectsLargestWidthRegardlessOfInputOrder(string first, string second)
    {
        var profile = InstitutionalReportPdfProfiles.WidestProfile([first, second]);

        Assert.Same(InstitutionalReportPdfProfiles.ExtraWideLandscape, profile);
    }

    [Fact]
    public void WidestProfile_HandlesUnknownAndEmptyInputsDeterministically()
    {
        var unknown = InstitutionalReportPdfProfiles.WidestProfile(["does-not-exist"]);
        var empty = InstitutionalReportPdfProfiles.WidestProfile([]);
        var tiedFallbacks = InstitutionalReportPdfProfiles.WidestProfile(["unknown-a", "unknown-b"]);

        Assert.Same(InstitutionalReportPdfProfiles.StandardPortrait, unknown);
        Assert.Same(InstitutionalReportPdfProfiles.StandardPortrait, empty);
        Assert.Same(InstitutionalReportPdfProfiles.StandardPortrait, tiedFallbacks);
    }

    private static RenderedReportManifestDto CreateSourceManifest(int pageCount)
    {
        var pages = Enumerable.Range(1, pageCount)
            .Select(i => new RenderedReportPageDto
            {
                OriginalPageNumber = i,
                RenderedPageNumber = i,
                SectionId = ReportSectionId.ExecutiveSummary,
                SectionName = $"قسم {i}",
                PageTitle = $"صفحة {i}",
                HtmlContent = $"""
                    <section class="report-page" data-page="{i}">
                      <main>محتوى {i}</main>
                      <footer class="report-footer"><span>الصفحة {i} من {pageCount}</span><span>تقرير</span></footer>
                    </section>
                    """,
            })
            .ToList();

        return new RenderedReportManifestDto
        {
            ReportTitle = TestReportTitle,
            ReportId = "REP-TEST",
            TotalPages = pageCount,
            Pages = pages,
        };
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;
}
