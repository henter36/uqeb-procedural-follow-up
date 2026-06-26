using System.Text.RegularExpressions;
using Uqeb.Api.Helpers;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpLetterVariableReplacerTests
{
    private static readonly Regex PlaceholderPattern = new(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    [Fact]
    public void Render_ReplacesKnownValuesCaseInsensitively()
    {
        var template =
            "إشارة إلى {IncomingNumber} بتاريخ {incomingdate} بشأن {SUBJECT} للجهة {TargetEntity}";

        var rendered = FollowUpLetterVariableReplacer.Render(template, new Dictionary<string, string?>
        {
            ["IncomingNumber"] = "456/2025",
            ["IncomingDate"] = "2025-06-01",
            ["Subject"] = "طلب إفادة",
            ["TargetEntity"] = "إدارة الشؤون",
        });

        Assert.Contains("456/2025", rendered);
        Assert.Contains("2025-06-01", rendered);
        Assert.Contains("طلب إفادة", rendered);
        Assert.Contains("إدارة الشؤون", rendered);
        Assert.DoesNotContain("{IncomingNumber}", rendered);
        Assert.Empty(PlaceholderPattern.Matches(rendered));
    }

    [Fact]
    public void Render_RejectsUnknownPlaceholders()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FollowUpLetterVariableReplacer.Render(
                "محتوى {Subject} {UnknownToken}",
                new Dictionary<string, string?> { ["Subject"] = "موضوع" }));

        Assert.Contains("UnknownToken", ex.Message);
    }

    [Fact]
    public void BuildValues_AndRender_LeaveNoLeftoverPlaceholders()
    {
        var ctx = new FollowUpLetterRenderContext
        {
            TransactionId = 42,
            IncomingNumber = "100/2025",
            IncomingDateLocal = new DateTime(2025, 6, 1),
            Subject = "موضوع",
            TargetEntity = "جهة",
            TargetEntities = "جهة 1، جهة 2",
            TargetDepartments = "إدارة المتابعة",
            AssignmentDateLocal = new DateTime(2025, 6, 5),
            DueDateLocal = new DateTime(2025, 6, 15),
            DaysOverdue = 3,
            Priority = "عادي",
            Category = "مراسلات",
            TodayLocal = new DateTime(2025, 6, 25),
            SenderDepartment = "إدارة المتابعة",
            PreparedBy = "أحمد",
            FollowUpNumber = "FU-001",
            FollowUpDateLocal = new DateTime(2025, 6, 25),
            FollowUpSequence = 2,
            FollowUpSequenceText = FollowUpSequenceCalculator.ToArabicText(2),
            ResponseDeadlineDays = 7,
        };

        var template = FollowUpLetterRenderService.DefaultFollowUpContent +
                       "\n{FollowUpSequenceText} {FollowUpSequence} {ResponseDeadlineDays}";

        var rendered = FollowUpLetterVariableReplacer.Render(
            template,
            FollowUpLetterVariableReplacer.BuildValues(ctx));

        Assert.Contains("التعقيب الثاني", rendered);
        Assert.DoesNotContain("{FollowUpSequenceText}", rendered);
        Assert.Empty(PlaceholderPattern.Matches(rendered));
    }

    [Fact]
    public void BuildValues_UsesIndependentTransactionNumber()
    {
        var values = FollowUpLetterVariableReplacer.BuildValues(new FollowUpLetterRenderContext
        {
            TransactionId = 42,
            TransactionNumber = "INT-2026-0001",
            IncomingNumber = "IN-1",
            Subject = "موضوع",
            TodayLocal = new DateTime(2026, 6, 26),
            FollowUpSequence = 1,
            FollowUpSequenceText = FollowUpSequenceCalculator.ToArabicText(1),
        });

        Assert.Equal("42", values["TransactionId"]);
        Assert.Equal("INT-2026-0001", values["TransactionNumber"]);
        Assert.Equal("IN-1", values["IncomingNumber"]);
    }

    [Fact]
    public void Render_ReplacesNullValuesWithEmptyString()
    {
        var rendered = FollowUpLetterVariableReplacer.Render(
            "الجهة: {TargetEntity}",
            new Dictionary<string, string?> { ["TargetEntity"] = null });

        Assert.Equal("الجهة:", rendered);
        Assert.Empty(PlaceholderPattern.Matches(rendered));
    }
}
