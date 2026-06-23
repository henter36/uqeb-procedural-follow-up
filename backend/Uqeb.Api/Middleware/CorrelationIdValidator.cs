namespace Uqeb.Api.Middleware;

public static class CorrelationIdValidator
{
    public const int MaxLength = 64;

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
            return false;

        foreach (var character in value)
        {
            var valid =
                character is >= 'a' and <= 'z' ||
                character is >= 'A' and <= 'Z' ||
                character is >= '0' and <= '9' ||
                character is '-' or '_' or '.';

            if (!valid)
                return false;
        }

        return true;
    }
}
