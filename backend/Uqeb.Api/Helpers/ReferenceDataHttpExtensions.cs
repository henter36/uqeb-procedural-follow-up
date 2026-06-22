namespace Uqeb.Api.Helpers;

public static class ReferenceDataHttpExtensions
{
    public static bool IsPagedReferenceDataRequest(this HttpRequest request) =>
        request.Query.ContainsKey("page");
}
