namespace Uqeb.Api.Configuration;

public class OrganizationBrandingOptions
{
    public const string SectionName = "OrganizationBranding";

    public string LogoPath { get; set; } = "Assets/Brand/organization-logo.png";
    public int LogoMaxWidth { get; set; } = 120;
    public int LogoMaxHeight { get; set; } = 60;
}
