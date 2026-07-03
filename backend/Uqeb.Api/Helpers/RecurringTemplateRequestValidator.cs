using Uqeb.Api.DTOs.RecurringTemplates;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Helpers;

public static class RecurringTemplateRequestValidator
{
    private const string FutureEventDateMessage = "لا يمكن أن يكون التاريخ بعد تاريخ اليوم.";
    private const int MaxDueDaysAfterPeriodEnd = 365;

    public static Dictionary<string, string> Validate(CreateRecurringTemplateRequest request)
    {
        var errors = new Dictionary<string, string>();

        ValidateRequiredTextFields(request, errors);
        ValidateRecurrenceFields(request, errors);
        ValidateDateRange(request, errors);
        ValidateSourceFields(request, errors);
        ValidateResponseFields(request, errors);
        ValidateDepartmentFields(request, errors);

        return errors;
    }

    public static Dictionary<string, string> ValidateGenerateRequestDates(GenerateRecurringTransactionRequest request)
    {
        var errors = new Dictionary<string, string>();

        if (!request.IncomingDate.HasValue)
            errors[nameof(request.IncomingDate)] = "تاريخ المعاملة مطلوب.";
        else if (IsFutureEventDate(request.IncomingDate.Value))
            errors[nameof(request.IncomingDate)] = FutureEventDateMessage;

        if (!request.ReferralDate.HasValue)
            errors[nameof(request.ReferralDate)] = "تاريخ الإحالة مطلوب.";
        else if (IsFutureEventDate(request.ReferralDate.Value))
            errors[nameof(request.ReferralDate)] = FutureEventDateMessage;

        return errors;
    }

    private static void ValidateRequiredTextFields(CreateRecurringTemplateRequest request, Dictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            errors[nameof(request.Title)] = "اسم القالب مطلوب.";

        if (string.IsNullOrWhiteSpace(request.SubjectTemplate))
            errors[nameof(request.SubjectTemplate)] = "موضوع المعاملة مطلوب.";

        if (string.IsNullOrWhiteSpace(request.DefaultRequiredAction))
            errors[nameof(request.DefaultRequiredAction)] = "الإجراء المطلوب الافتراضي مطلوب.";
    }

    private static void ValidateRecurrenceFields(CreateRecurringTemplateRequest request, Dictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(request.RecurrenceType) ||
            !Enum.TryParse<RecurrenceType>(request.RecurrenceType, true, out _))
            errors[nameof(request.RecurrenceType)] = "نوع التكرار مطلوب ويجب أن يكون شهري أو ربع سنوي أو نصف سنوي أو سنوي.";

        if (!request.DueDaysAfterPeriodEnd.HasValue ||
            request.DueDaysAfterPeriodEnd.Value < 0 ||
            request.DueDaysAfterPeriodEnd.Value > MaxDueDaysAfterPeriodEnd)
            errors[nameof(request.DueDaysAfterPeriodEnd)] = "عدد الأيام بعد نهاية الفترة مطلوب ويجب أن يكون بين 0 و365.";

        if (request.DefaultReplyDueDays.HasValue &&
            (request.DefaultReplyDueDays.Value < 0 || request.DefaultReplyDueDays.Value > MaxDueDaysAfterPeriodEnd))
            errors[nameof(request.DefaultReplyDueDays)] = "عدد أيام الرد يجب أن يكون بين 0 و365.";

        if (!string.IsNullOrWhiteSpace(request.NextTransactionCreationMethod) &&
            !Enum.TryParse<RecurringNextTransactionCreationMethod>(request.NextTransactionCreationMethod, true, out _))
            errors[nameof(request.NextTransactionCreationMethod)] = "طريقة إنشاء المعاملة التالية غير صحيحة.";
    }

    private static void ValidateDateRange(CreateRecurringTemplateRequest request, Dictionary<string, string> errors)
    {
        if (!request.StartDate.HasValue)
        {
            errors[nameof(request.StartDate)] = "تاريخ بداية التكرار مطلوب.";
            return;
        }

        if (request.EndDate.HasValue && request.EndDate.Value.Date < request.StartDate.Value.Date)
            errors[nameof(request.EndDate)] = "تاريخ نهاية التكرار لا يمكن أن يكون قبل تاريخ البداية.";
    }

    private static void ValidateSourceFields(CreateRecurringTemplateRequest request, Dictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(request.IncomingSourceType) ||
            !Enum.TryParse<IncomingSourceType>(request.IncomingSourceType, true, out var sourceType))
        {
            errors[nameof(request.IncomingSourceType)] = "يجب اختيار نوع الجهة الوارد منها.";
            return;
        }

        var hasParty = request.IncomingFromPartyId.HasValue && request.IncomingFromPartyId > 0;
        var hasDept = request.IncomingFromDepartmentId.HasValue && request.IncomingFromDepartmentId > 0;

        if (sourceType == IncomingSourceType.External && !hasParty)
            errors[nameof(request.IncomingFromPartyId)] = "يجب اختيار الجهة الوارد منها.";
        if (sourceType == IncomingSourceType.Internal && !hasDept)
            errors[nameof(request.IncomingFromDepartmentId)] = "يجب اختيار الإدارة الوارد منها.";
    }

    private static void ValidateResponseFields(CreateRecurringTemplateRequest request, Dictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(request.Priority) || !Enum.TryParse<Priority>(request.Priority, true, out _))
            errors[nameof(request.Priority)] = "الأولوية مطلوبة.";

        if (string.IsNullOrWhiteSpace(request.ResponseType) || !Enum.TryParse<ResponseType>(request.ResponseType, true, out var responseType))
        {
            errors[nameof(request.ResponseType)] = "نوع الإفادة مطلوب.";
            return;
        }

        if ((request.RequiresResponse ?? false) && responseType == ResponseType.None)
            errors[nameof(request.ResponseType)] = "نوع الإفادة مطلوب عند طلب إفادة.";
    }

    private static void ValidateDepartmentFields(CreateRecurringTemplateRequest request, Dictionary<string, string> errors)
    {
        if (!request.CategoryId.HasValue || request.CategoryId <= 0)
            errors[nameof(request.CategoryId)] = "التصنيف مطلوب.";

        if (request.DepartmentIds == null || request.DepartmentIds.Count == 0)
            errors[nameof(request.DepartmentIds)] = "يجب تحديد إدارة واحدة على الأقل لتوجيه المعاملة إليها.";
    }

    private static DateTime GetSaudiToday() => DateTime.UtcNow.AddHours(3).Date;

    private static bool IsFutureEventDate(DateTime value) => value.Date > GetSaudiToday();
}
