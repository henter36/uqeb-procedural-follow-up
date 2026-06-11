namespace Uqeb.Api.Helpers;

public class DuplicateTrackingNumberException : Exception
{
    public DuplicateTrackingNumberException()
        : base("تعذر تخصيص رقم تتبع فريد للمعاملة. يرجى المحاولة مرة أخرى.") { }
}
