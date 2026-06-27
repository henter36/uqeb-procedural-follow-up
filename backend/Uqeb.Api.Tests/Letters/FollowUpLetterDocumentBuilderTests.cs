using System.Globalization;
using System.Text.Json;
using Uqeb.Api.Configuration;
using Uqeb.Api.DTOs.LetterTemplates;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpLetterDocumentBuilderTests
{
    // Riyadh is UTC+3. The boundary is at 21:00 UTC (= 00:00 Riyadh next day).
    // IncomingDateGregorian is formatted as dd/MM/yyyy.
    [Theory]
    [InlineData("2026-06-25T20:59:59Z", "25/06/2026")]  // 23:59:59 Riyadh → June 25
    [InlineData("2026-06-25T21:00:00Z", "26/06/2026")]  // 00:00:00 Riyadh June 26 → June 26
    [InlineData("2026-06-25T21:00:01Z", "26/06/2026")]  // one second past midnight Riyadh → June 26
    [InlineData("2026-06-25T23:59:59Z", "26/06/2026")]  // 02:59:59 Riyadh June 26 → June 26
    [InlineData("2026-06-26T00:00:01Z", "26/06/2026")]  // 03:00:01 Riyadh → June 26
    public void Build_ConvertsUtcIncomingDateToRiyadhBusinessDate(string utcIncoming, string expectedFormatted)
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
                IncomingDate = DateTime.Parse(utcIncoming, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
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

        Assert.Contains(expectedFormatted, document.Body);
    }

    [Fact]
    public void Build_UnspecifiedIncomingDate_KeepsCalendarDay()
    {
        var builder = new FollowUpLetterDocumentBuilder(new FixedTimeZone(new DateTime(2026, 6, 26)));

        var document = builder.Build(new FollowUpLetterDocumentBuildRequest
        {
            Transaction = new Models.Entities.Transaction
            {
                Id = 1,
                IncomingNumber = "IN-1",
                IncomingDate = new DateTime(2026, 6, 25, 23, 59, 59, DateTimeKind.Unspecified),
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
            TodayLocal = new DateTime(2026, 6, 26),
        });

        // Unspecified dates are treated as local business dates — no zone conversion.
        Assert.Contains("25/06/2026", document.Body);
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
    public void Build_DoesNotInjectStaticFollowUpLetterTitle()
    {
        var builder = new FollowUpLetterDocumentBuilder(new FixedTimeZone(new DateTime(2026, 6, 26)));

        var document = builder.Build(new FollowUpLetterDocumentBuildRequest
        {
            Transaction = new Models.Entities.Transaction
            {
                Id = 1,
                InternalTrackingNumber = "INT-1",
                IncomingNumber = "IN-1",
                IncomingDate = new DateTime(2026, 6, 1),
                Subject = "اختبار",
                Priority = Priority.Normal,
            },
            Template = new Models.Entities.LetterTemplate { Id = 1, Content = "نص القالب فقط", Name = "test" },
            Target = new FollowUpLetterTargetEntity("جهة"),
            TodayLocal = new DateTime(2026, 6, 26),
        });

        Assert.Equal(string.Empty, document.Title);
        Assert.DoesNotContain("خطاب تعقيب", FollowUpLetterPrintViewRenderer.Render([document]));
    }

    [Fact]
    public void LetterTemplateTypeJson_AcceptsStringAndWritesCanonicalString()
    {
        // ASP.NET Core uses PropertyNameCaseInsensitive = true; mirror that here so camelCase JSON keys resolve to PascalCase properties.
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var request = JsonSerializer.Deserialize<CreateLetterTemplateRequest>(
            """{"name":"n","content":"c","templateType":"FollowUp"}""", opts);

        Assert.Equal(LetterTemplateType.FollowUp, request!.TemplateType);
        Assert.Contains(
            """"templateType":"FollowUp"""",
            JsonSerializer.Serialize(
                new LetterTemplateDto { TemplateType = LetterTemplateType.FollowUp },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    [Fact]
    public void LetterTemplateTypeJson_AcceptsLegacyNumber()
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var request = JsonSerializer.Deserialize<CreateLetterTemplateRequest>(
            """{"name":"n","content":"c","templateType":1}""", opts);

        Assert.Equal(LetterTemplateType.FollowUp, request!.TemplateType);
    }

    [Fact]
    public void LetterTemplateTypeJson_AcceptsLowerCaseString()
    {
        // ignoreCase: true allows lowercase variants (e.g. from legacy or mixed-case clients).
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var request = JsonSerializer.Deserialize<CreateLetterTemplateRequest>(
            """{"name":"n","content":"c","templateType":"followup"}""", opts);

        Assert.Equal(LetterTemplateType.FollowUp, request!.TemplateType);
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

    [Fact]
    public void RenderPrintView_AllowsNullBody()
    {
        var html = FollowUpLetterPrintViewRenderer.Render([
            new FollowUpLetterDocumentModel
            {
                Title = "خطاب",
                Body = null!,
                Recipient = "جهة",
                Subject = "موضوع",
                LetterNumber = "1",
                GregorianDate = "26/06/2026",
                HijriDate = "هجري",
                FollowUpSequenceText = "التعقيب الأول",
            },
        ]);

        Assert.Contains("خطاب", html);
    }

    [Fact]
    public void RenderPrintView_NoHardcodedSalutation()
    {
        var html = FollowUpLetterPrintViewRenderer.Render([
            new FollowUpLetterDocumentModel
            {
                Body = "نص الخطاب فقط",
                Recipient = "الجهة المستلمة",
                Subject = "موضوع اختبار",
                LetterNumber = "1",
                GregorianDate = "26/06/2026",
                HijriDate = "هجري",
            },
        ]);

        Assert.DoesNotContain("سعادة/ الجهة المستلمة", html);
        Assert.DoesNotContain("السلام عليكم ورحمة الله وبركاته،،", html);
    }

    [Fact]
    public void RenderPrintView_NoHardcodedSubjectLine()
    {
        var html = FollowUpLetterPrintViewRenderer.Render([
            new FollowUpLetterDocumentModel
            {
                Body = "نص الخطاب فقط",
                Recipient = "جهة",
                Subject = "موضوع اختبار",
                LetterNumber = "1",
                GregorianDate = "26/06/2026",
                HijriDate = "هجري",
            },
        ]);

        Assert.DoesNotContain("الموضوع: موضوع اختبار", html);
    }

    [Fact]
    public void RenderPrintView_BodyContentPassedThrough()
    {
        const string bodyContent = "محتوى التجربة الفريد لهذا الاختبار";

        var html = FollowUpLetterPrintViewRenderer.Render([
            new FollowUpLetterDocumentModel
            {
                Body = bodyContent,
                Recipient = "جهة",
                Subject = "موضوع",
                LetterNumber = "1",
                GregorianDate = "26/06/2026",
                HijriDate = "هجري",
            },
        ]);

        Assert.Contains(bodyContent, html);
    }

    [Fact]
    public void Build_SignatoryNotFilledFromPreparedByWhenEmpty()
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
            PreparedBy = "اسم معد الخطاب",
            SignatoryNameOverride = null,
            TodayLocal = new DateTime(2026, 6, 26),
        });

        Assert.Equal(string.Empty, document.SignatoryName);
    }

    [Fact]
    public void Build_SignatoryNotFilledFromSenderDepartmentWhenEmpty()
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
            SenderDepartment = "المتابعة الإجرائية",
            SignatoryPosition = null,
            TodayLocal = new DateTime(2026, 6, 26),
        });

        Assert.Equal(string.Empty, document.SignatoryTitle);
    }

    [Fact]
    public void Build_ExplicitSignatoryValuesAreUsed()
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
            SignatoryNameOverride = "  أحمد محمد  ",
            SignatoryPosition = "  مدير الإدارة  ",
            SignatoryRank = "  عميد  ",
            TodayLocal = new DateTime(2026, 6, 26),
        });

        Assert.Equal("أحمد محمد", document.SignatoryName);
        Assert.Equal("مدير الإدارة", document.SignatoryTitle);
        Assert.Equal("عميد", document.SignatoryRank);
    }
}
