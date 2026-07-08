using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;
using Uqeb.Api.Reporting.Services;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

[Collection("CultureSensitive")]
public class InstitutionalReportRendererCultureTests
{
    private readonly InstitutionalReportRenderer _renderer = new();

    private static readonly DateTime PeriodFrom = new(2026, 6, 1);
    private static readonly DateTime PeriodTo = new(2026, 6, 24);
    private static readonly DateTime IssueDate = new(2026, 6, 24);
    private static readonly DateTime GeneratedAt = new(2026, 6, 24, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void RenderManifest_UnderArabicCulture_UsesGregorianCoverDates()
    {
        var html = RenderCoverHtmlUnderArabicCulture();

        Assert.Contains("2026-06-01", html);
        Assert.Contains("2026-06-24", html);
        Assert.DoesNotContain("2026-06-24 09:30", html);
        Assert.DoesNotContain("1447", html);
    }

    [Fact]
    public void RenderManifest_UnderArabicCulture_UsesGregorianMetadataDates()
    {
        var html = RenderMetadataHtmlUnderArabicCulture();

        Assert.Contains("من 2026-06-01 إلى 2026-06-24", html);
        Assert.Contains("2026-06-24 09:30", html);
        Assert.DoesNotContain("1447", html);
    }

    private string RenderCoverHtmlUnderArabicCulture() =>
        RunUnderArabicCulture(() =>
        {
            var manifest = _renderer.RenderManifest(BuildModel(), [ReportSectionId.Cover]);
            return Assert.Single(manifest.Pages).HtmlContent;
        });

    private string RenderMetadataHtmlUnderArabicCulture() =>
        RunUnderArabicCulture(() =>
        {
            var manifest = _renderer.RenderManifest(BuildModel(), [ReportSectionId.ReportMetadata]);
            return Assert.Single(manifest.Pages).HtmlContent;
        });

    private static T RunUnderArabicCulture<T>(Func<T> action)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var arabicCulture = new CultureInfo("ar-SA");
            CultureInfo.CurrentCulture = arabicCulture;
            CultureInfo.CurrentUICulture = arabicCulture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static InstitutionalReportModel BuildModel() => new()
    {
        Metadata = new ReportMetadataDto
        {
            ReportNumber = "REP-2026-000125",
            ReportTypeName = "تقرير",
            Title = "تقرير اختبار",
            IssueDate = IssueDate,
            PeriodFrom = PeriodFrom,
            PeriodTo = PeriodTo,
            VerificationId = "VERIFY",
            GeneratedAt = GeneratedAt,
        },
        Summary = new ExecutiveSummaryDto
        {
            KpiCards = [new KpiCardDto { Key = "total", Title = "إجمالي", Value = "1" }],
            ExecutiveNarrative = "نص",
        },
    };
}

[CollectionDefinition("CultureSensitive", DisableParallelization = true)]
public sealed class CultureSensitiveCollection;
