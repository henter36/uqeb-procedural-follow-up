using Uqeb.Api.Helpers;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpSequenceCalculatorTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(5, 6)]
    public void CalculateExpectedSequence_ReturnsCountPlusOne(int registeredFollowUpCount, int expectedSequence)
    {
        Assert.Equal(expectedSequence, FollowUpSequenceCalculator.CalculateExpectedSequence(registeredFollowUpCount));
    }

    [Theory]
    [InlineData(1, "التعقيب الأول")]
    [InlineData(2, "التعقيب الثاني")]
    [InlineData(3, "التعقيب الثالث")]
    [InlineData(4, "التعقيب رقم 4")]
    public void ToArabicText_ReturnsExpectedLabels(int sequence, string expectedText)
    {
        Assert.Equal(expectedText, FollowUpSequenceCalculator.ToArabicText(sequence));
    }
}
