using Uqeb.Api.Reporting.Assets;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class InstitutionalReportFontAssetsTests
{
    [Fact]
    public void BuildFontFaceCss_ContainsNonEmptyEmbeddedFontDataUris()
    {
        var css = InstitutionalReportFontAssets.BuildFontFaceCss();

        Assert.Contains("font-family: 'Uqeb Report Arabic'", css);
        Assert.Contains("data:font/truetype;base64,", css);
        Assert.Contains("format('truetype')", css);
        Assert.DoesNotContain("url('')", css);
    }

    [Fact]
    public void FontFiles_ExistInBuildOutputDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        var fontsDir = Path.Combine(baseDir, "Reporting", "Assets", "Fonts");

        Assert.True(File.Exists(Path.Combine(fontsDir, "NotoSansArabic-Regular.ttf")));
        Assert.True(File.Exists(Path.Combine(fontsDir, "NotoSansArabic-Bold.ttf")));
    }
}
