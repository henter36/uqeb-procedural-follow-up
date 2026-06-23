using Xunit;
using Uqeb.Api.Reporting.Helpers;

namespace Uqeb.Api.Tests.Reporting;

public class ReportPageRangeParserTests
{
    [Fact]
    public void ParsesSinglePages()
    {
        var result = ReportPageRangeParser.Parse("1,3,5", 10);
        Assert.True(result.IsValid);
        Assert.Equal([1, 3, 5], result.PageNumbers);
    }

    [Fact]
    public void ParsesRangeAndDedupes()
    {
        var result = ReportPageRangeParser.Parse("2-4,3,5", 10);
        Assert.True(result.IsValid);
        Assert.Equal([2, 3, 4, 5], result.PageNumbers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RejectsEmptyExpression(string? expression)
    {
        var result = ReportPageRangeParser.Parse(expression, 10);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ParsesWhitespaceAndDuplicateSeparators()
    {
        var result = ReportPageRangeParser.Parse(" 1 , 3 - 5 , 5 ", 10);
        Assert.True(result.IsValid);
        Assert.Equal([1, 3, 4, 5], result.PageNumbers);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1,a")]
    public void RejectsNonNumericTokens(string expression)
    {
        var result = ReportPageRangeParser.Parse(expression, 10);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void AcceptsDuplicatePagesAcrossTokens()
    {
        var result = ReportPageRangeParser.Parse("1,3-5,5", 10);
        Assert.True(result.IsValid);
        Assert.Equal([1, 3, 4, 5], result.PageNumbers);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("8-3")]
    [InlineData("12")]
    public void RejectsInvalidExpressions(string expression)
    {
        var result = ReportPageRangeParser.Parse(expression, 10);
        Assert.False(result.IsValid);
    }
}
