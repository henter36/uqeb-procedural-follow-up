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

public sealed class FollowUpPrintValidationException : FollowUpPrintException
{
    public FollowUpPrintValidationException(string message)
        : base(message) { }
}

public sealed class FollowUpPrintNotFoundException : FollowUpPrintException
{
    public FollowUpPrintNotFoundException(string message)
        : base(message) { }
}

public sealed class FollowUpPrintLeaseExpiredException : FollowUpPrintException
{
    public int JobId { get; }

    public FollowUpPrintLeaseExpiredException(int jobId)
        : base($"انتهت صلاحية الـlease للمهمة {jobId}. تم الإيقاف لتجنب التعارض مع عامل آخر.")
    {
        JobId = jobId;
    }
}
