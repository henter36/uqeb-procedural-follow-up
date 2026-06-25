using Uqeb.Api.Helpers;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpLetterVariableRegistryTests
{
    [Theory]
    [InlineData("Subject")]
    [InlineData("{Subject}")]
    [InlineData("subject")]
    public void IsKnown_RecognizesRegisteredVariables(string token)
    {
        Assert.True(FollowUpLetterVariableRegistry.IsKnown(token));
    }

    [Theory]
    [InlineData("UnknownField")]
    [InlineData("{NotARealVariable}")]
    public void IsKnown_RejectsUnknownVariables(string token)
    {
        Assert.False(FollowUpLetterVariableRegistry.IsKnown(token));
    }

    [Fact]
    public void FindUnknownVariables_DetectsOnlyUnknownPlaceholders()
    {
        var content =
            "معاملة {IncomingNumber} بتاريخ {IncomingDate} و{FakeVariable} و{AnotherFake}";

        var unknown = FollowUpLetterVariableRegistry.FindUnknownVariables(content);

        Assert.Equal(2, unknown.Count);
        Assert.Contains("FakeVariable", unknown);
        Assert.Contains("AnotherFake", unknown);
        Assert.DoesNotContain("IncomingNumber", unknown);
        Assert.DoesNotContain("IncomingDate", unknown);
    }

    [Fact]
    public void FindUnknownVariables_ReturnsEmpty_ForBlankContent()
    {
        Assert.Empty(FollowUpLetterVariableRegistry.FindUnknownVariables(string.Empty));
        Assert.Empty(FollowUpLetterVariableRegistry.FindUnknownVariables("   "));
    }

    [Fact]
    public void FindUnknownVariables_IsCaseInsensitiveAndDistinct()
    {
        var content = "{UnknownOne} text {unknownone} {UNKNOWNONE}";

        var unknown = FollowUpLetterVariableRegistry.FindUnknownVariables(content);

        Assert.Single(unknown);
        Assert.Equal("UnknownOne", unknown[0]);
    }

    [Fact]
    public void All_ContainsCoreFollowUpVariables()
    {
        var names = FollowUpLetterVariableRegistry.All.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("FollowUpSequence", names);
        Assert.Contains("FollowUpSequenceText", names);
        Assert.Contains("ResponseDeadlineDays", names);
    }
}
