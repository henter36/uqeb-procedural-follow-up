namespace Uqeb.Api.Configuration;

public class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "UqebApi";
    public string Audience { get; set; } = "UqebClient";
    public int ExpireMinutes { get; set; } = 480;
}
