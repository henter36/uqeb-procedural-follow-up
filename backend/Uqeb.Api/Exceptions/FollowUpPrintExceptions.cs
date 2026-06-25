namespace Uqeb.Api.Exceptions;

public abstract class FollowUpPrintException : Exception
{
    protected FollowUpPrintException(string message) : base(message) { }
}

public sealed class FollowUpPrintForbiddenException : FollowUpPrintException
{
    public FollowUpPrintForbiddenException(string message = "غير مصرح بالوصول إلى مورد طباعة التعقيب.")
        : base(message) { }
}

public sealed class FollowUpPrintConflictException : FollowUpPrintException
{
    public FollowUpPrintConflictException(string message = "تعارض في مفتاح idempotency لمهمة الطباعة.")
        : base(message) { }
}
