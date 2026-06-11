namespace Uqeb.Api.Helpers;

public static class ReportPageSize
{
    public const int Default = 5;
    public const int Max = 50;
    private static readonly HashSet<int> Allowed = new() { 5, 10, 50 };

    public static bool IsAllowed(int pageSize) => Allowed.Contains(pageSize);

    public static int Normalize(int pageSize) => IsAllowed(pageSize) ? pageSize : Default;
}
