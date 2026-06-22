namespace Uqeb.Api.Helpers;

public class LastActiveAdminException : Exception
{
    public LastActiveAdminException()
        : base("لا يمكن تعطيل آخر مدير نظام فعال في النظام") { }
}
