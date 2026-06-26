using Microsoft.AspNetCore.Mvc;

namespace Uqeb.Api.Configuration;

public static class UqebApiBehaviorOptions
{
    private const string InvalidTemplateTypeMessage =
        "نوع القالب غير معروف. استخدم إحدى القيم النصية المعتمدة مثل FollowUp أو FirstFollowUp.";

    private const string FallbackValidationMessage =
        "البيانات المرسلة غير صحيحة. راجع الحقول وحاول مرة أخرى.";

    public static void ConfigureUqebApiBehavior(this ApiBehaviorOptions options)
    {
        options.InvalidModelStateResponseFactory = CreateInvalidModelStateResponse;
    }

    private static IActionResult CreateInvalidModelStateResponse(ActionContext context)
    {
        var messages = context.ModelState
            .SelectMany(entry => entry.Value?.Errors ?? [])
            .Select(error => error.ErrorMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(NormalizeValidationMessage)
            .Where(ShouldExposeValidationMessage)
            .Distinct()
            .ToList();

        if (messages.Count == 0)
            messages.Add(FallbackValidationMessage);

        return new BadRequestObjectResult(new { message = messages[0], errors = messages });
    }

    private static string NormalizeValidationMessage(string message) =>
        message.Contains("LetterTemplateType", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("نوع القالب غير معروف", StringComparison.OrdinalIgnoreCase)
            ? InvalidTemplateTypeMessage
            : message;

    private static bool ShouldExposeValidationMessage(string message) =>
        !message.Contains("The request field is required", StringComparison.OrdinalIgnoreCase) &&
        !message.Contains("JSON value could not be converted", StringComparison.OrdinalIgnoreCase) &&
        !message.Contains("$.", StringComparison.OrdinalIgnoreCase);
}
