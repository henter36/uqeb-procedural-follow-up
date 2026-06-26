using Uqeb.Api.Configuration;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpLetterDocumentBuilderTests
{
    [Theory]
    [InlineData("2026-06-25T20:59:59Z", "2026-06-26")]
    [InlineData("2026-06-25T21:00:00Z", "2026-06-26")]
    [InlineData("2026-06-25T23:59:59Z", "2026-06-26")]
    [InlineData("2026-06-26T00:00:01Z", "2026-06-26")]
    public void Build_ConvertsUtcIncomingDateToRiyadhBusinessDate(string utcIncoming, string expectedDate)
    {
        var timeZone = new FollowUpLetterTimeZone(
            Microsoft.Extensions.Options.Options.Create(new FollowUpLettersOptions { DisplayTimeZoneId = "Asia/Riyadh" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FollowUpLetterTimeZone>.Instance);
        var builder = new FollowUpLetterDocumentBuilder(timeZone);
        var today = new DateTime(2026, 6, 26);

        var document = builder.Build(new FollowUpLetterDocumentBuildRequest
        {
            Transaction = new Models.Entities.Transaction
            {
                Id = 1,
                IncomingNumber = "IN-1",
                IncomingDate = DateTime.Parse(utcIncoming).ToUniversalTime(),
                Subject = "اختبار",
                Priority = Models.Enums.Priority.Normal,
            },
            Template = new Models.Entities.LetterTemplate
            {
                Id = 1,
                Content = "محتوى {IncomingDateGregorian}",
                Name = "test",
            },
            Target = new FollowUpLetterTargetEntity("جهة"),
            TodayLocal = today,
        });

        Assert.Contains(expectedDate.Split('-')[2], document.Body);
    }

    [Fact]
    public void Build_UsesBrandingApiUrl_NotFilePath()
    {
        var builder = new FollowUpLetterDocumentBuilder(new FixedTimeZone(new DateTime(2026, 6, 26)));
        var document = builder.Build(new FollowUpLetterDocumentBuildRequest
        {
            Transaction = new Models.Entities.Transaction
            {
                Id = 1,
                IncomingNumber = "IN-1",
                IncomingDate = new DateTime(2026, 6, 1),
                Subject = "اختبار",
                Priority = Models.Enums.Priority.Normal,
            },
            Template = new Models.Entities.LetterTemplate { Id = 1, Content = "test", Name = "test" },
            Target = new FollowUpLetterTargetEntity("جهة"),
            LogoPath = "Assets/Brand/organization-logo.png",
            TodayLocal = new DateTime(2026, 6, 26),
        });

        Assert.Equal(OrganizationBrandingPaths.LogoApiUrl, document.LogoPath);
    }

    [Fact]
    public void Build_PreservesBodyOverrideWhitespaceExactly()
    {
        var builder = new FollowUpLetterDocumentBuilder(new FixedTimeZone(new DateTime(2026, 6, 26)));
        var overrideBody = "  بداية النص\n\nفقرة ثانية\n  نهاية النص  ";

        var document = builder.Build(new FollowUpLetterDocumentBuildRequest
        {
            Transaction = new Models.Entities.Transaction
            {
                Id = 1,
                InternalTrackingNumber = "INT-1",
                IncomingNumber = "IN-1",
                IncomingDate = new DateTime(2026, 6, 1),
                Subject = "اختبار",
                Priority = Models.Enums.Priority.Normal,
            },
            Template = new Models.Entities.LetterTemplate { Id = 1, Content = "test", Name = "test" },
            Target = new FollowUpLetterTargetEntity("جهة"),
            BodyOverride = overrideBody,
            TodayLocal = new DateTime(2026, 6, 26),
        });

        Assert.Equal(overrideBody, document.Body);
    }

    [Fact]
    public void RenderPrintView_IncludesLogoWithObjectFitContain()
    {
        var html = FollowUpLetterPrintViewRenderer.Render([
            new FollowUpLetterDocumentModel
            {
                Title = "خطاب",
                Body = "نص",
                Recipient = "جهة",
                Subject = "موضوع",
                LetterNumber = "1",
                GregorianDate = "26/06/2026",
                HijriDate = "هجري",
                FollowUpSequenceText = "التعقيب الأول",
            },
        ]);

        Assert.Contains(OrganizationBrandingPaths.LogoApiUrl, html);
        Assert.Contains("object-fit: contain", html);
    }
}
