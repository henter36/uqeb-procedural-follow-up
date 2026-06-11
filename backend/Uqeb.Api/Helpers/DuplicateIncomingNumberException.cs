namespace Uqeb.Api.Helpers;

public class DuplicateIncomingNumberException : Exception
{
    public DuplicateIncomingNumberException()
        : base("رقم الوارد موجود مسبقاً، يرجى استخدام رقم مختلف.") { }
}
