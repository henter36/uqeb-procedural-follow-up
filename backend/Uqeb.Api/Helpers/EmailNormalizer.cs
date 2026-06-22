namespace Uqeb.Api.Helpers;

public static class EmailNormalizer
{
    public static string? Normalize(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}
