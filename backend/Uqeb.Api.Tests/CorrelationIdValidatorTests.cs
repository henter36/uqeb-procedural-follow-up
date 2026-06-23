using Uqeb.Api.Middleware;
using Xunit;

namespace Uqeb.Api.Tests;

public class CorrelationIdValidatorTests
{
    [Fact]
    public void IsValid_accepts_alphanumeric_dash_underscore_dot()
    {
        Assert.True(CorrelationIdValidator.IsValid("abc-123_X.y"));
    }

    [Fact]
    public void IsValid_rejects_empty()
    {
        Assert.False(CorrelationIdValidator.IsValid(""));
        Assert.False(CorrelationIdValidator.IsValid("   "));
    }

    [Fact]
    public void IsValid_rejects_longer_than_64_characters()
    {
        Assert.False(CorrelationIdValidator.IsValid(new string('a', 65)));
    }

    [Fact]
    public void IsValid_rejects_spaces()
    {
        Assert.False(CorrelationIdValidator.IsValid("abc def"));
    }

    [Fact]
    public void IsValid_rejects_cr_lf_and_control_characters()
    {
        Assert.False(CorrelationIdValidator.IsValid("abc\rdef"));
        Assert.False(CorrelationIdValidator.IsValid("abc\ndef"));
        Assert.False(CorrelationIdValidator.IsValid("abc\u0007def"));
    }

    [Fact]
    public void IsValid_rejects_unicode()
    {
        Assert.False(CorrelationIdValidator.IsValid("abcمرحبا"));
    }
}
