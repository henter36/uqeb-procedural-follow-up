namespace Uqeb.Api.Reporting.Helpers;

using System.Security.Cryptography;
using System.Text;

public static class InstitutionalReportFingerprint
{
    public static string Compute(byte[] content)
    {
        var hash = SHA256.HashData(content);
        return Convert.ToHexString(hash)[..16];
    }

    public static string Compute(string content) =>
        Compute(Encoding.UTF8.GetBytes(content));
}
