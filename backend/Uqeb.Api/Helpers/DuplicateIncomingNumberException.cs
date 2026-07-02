namespace Uqeb.Api.Helpers;

public class DuplicateIncomingNumberException : Exception
{
    public DuplicateIncomingNumberException()
        : base("رقم المعاملة موجود مسبقاً، يرجى استخدام رقم مختلف.") { }
}
