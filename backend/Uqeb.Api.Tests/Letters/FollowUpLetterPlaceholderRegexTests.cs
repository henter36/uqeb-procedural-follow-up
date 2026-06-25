using System.Text;
using System.Text.RegularExpressions;
using Uqeb.Api.Helpers;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class FollowUpLetterPlaceholderRegexTests
{
    [Fact]
    public void MatchAll_FindsRegisteredPlaceholders()
    {
        var content = "معاملة {IncomingNumber} بتاريخ {IncomingDate}";

        var matches = FollowUpLetterPlaceholderRegex.MatchAll(content);

        Assert.Equal(2, matches.Count);
        Assert.Equal("IncomingNumber", matches[0].Groups[1].Value);
        Assert.Equal("IncomingDate", matches[1].Groups[1].Value);
    }

    [Fact]
    public void MatchAll_IgnoresInvalidPlaceholderSyntax()
    {
        var content = "{Bad-Name} {Not Valid} {ValidName}";

        var matches = FollowUpLetterPlaceholderRegex.MatchAll(content);

        Assert.Single(matches);
        Assert.Equal("ValidName", matches[0].Groups[1].Value);
    }

    [Fact]
    public void RemoveRemaining_StripsUnknownPlaceholders()
    {
        var content = "نص {UnknownVar} آخر {Subject}";

        var result = FollowUpLetterPlaceholderRegex.RemoveRemaining(content);

        Assert.Equal("نص  آخر ", result);
    }

    [Fact]
    public void MatchAll_CompletesWithinTimeout_ForLargeInput()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 5_000; i++)
            builder.Append("{Subject} ");

        var matches = FollowUpLetterPlaceholderRegex.MatchAll(builder.ToString());

        Assert.Equal(5_000, matches.Count);
    }
}
