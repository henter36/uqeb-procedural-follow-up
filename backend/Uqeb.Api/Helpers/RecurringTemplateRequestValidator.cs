using Uqeb.Api.DTOs.RecurringTemplates;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Helpers;

public static class RecurringTemplateRequestValidator
{
    public static Dictionary<string, string> Validate(CreateRecurringTemplateRequest request)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(request.Title))
            errors[nameof(request.Title)] = "اسم القالب مطلوب.";

        if (string.IsNullOrWhiteSpace(request.SubjectTemplate))
            errors[nameof(request.SubjectTemplate)] = "موضوع المعاملة مطلوب.";

        if (string.IsNullOrWhiteSpace(request.RecurrenceType) ||
            !Enum.TryParse<RecurrenceType>(request.RecurrenceType, true, out _))
            errors[nameof(request.RecurrenceType)] = "نوع التكرار مطلوب ويجب أن يكون شهري أو ربع سنوي.";

        if (request.StartDate == default)
            errors[nameof(request.StartDate)] = "تاريخ بداية التكرار مطلوب.";

        if (request.EndDate.HasValue && request.StartDate != default && request.EndDate.Value.Date < request.StartDate.Date)
            errors[nameof(request.EndDate)] = "تاريخ نهاية التكرار لا يمكن أن يكون قبل تاريخ البداية.";

        if (!request.DueDaysAfterPeriodEnd.HasValue || request.DueDaysAfterPeriodEnd.Value < 0)
            errors[nameof(request.DueDaysAfterPeriodEnd)] = "عدد الأيام بعد نهاية الفترة مطلوب ويجب ألا يكون سالبًا.";

        if (string.IsNullOrWhiteSpace(request.IncomingSourceType) ||
            !Enum.TryParse<IncomingSourceType>(request.IncomingSourceType, true, out var sourceType))
        {
            errors[nameof(request.IncomingSourceType)] = "يجب اختيار نوع الجهة الوارد منها.";
        }
        else
        {
            var hasParty = request.IncomingFromPartyId.HasValue && request.IncomingFromPartyId > 0;
            var hasDept = request.IncomingFromDepartmentId.HasValue && request.IncomingFromDepartmentId > 0;

            if (sourceType == IncomingSourceType.External && !hasParty)
                errors[nameof(request.IncomingFromPartyId)] = "يجب اختيار الجهة الوارد منها.";
            if (sourceType == IncomingSourceType.Internal && !hasDept)
                errors[nameof(request.IncomingFromDepartmentId)] = "يجب اختيار الإدارة الوارد منها.";
        }

        if (!request.CategoryId.HasValue || request.CategoryId <= 0)
            errors[nameof(request.CategoryId)] = "التصنيف مطلوب.";

        if (string.IsNullOrWhiteSpace(request.Priority) || !Enum.TryParse<Priority>(request.Priority, true, out _))
            errors[nameof(request.Priority)] = "الأولوية مطلوبة.";

        if (string.IsNullOrWhiteSpace(request.ResponseType) || !Enum.TryParse<ResponseType>(request.ResponseType, true, out var responseType))
        {
            errors[nameof(request.ResponseType)] = "نوع الإفادة مطلوب.";
        }
        else if (request.RequiresResponse && responseType == ResponseType.None)
        {
            errors[nameof(request.ResponseType)] = "نوع الإفادة مطلوب عند طلب إفادة.";
        }

        if (string.IsNullOrWhiteSpace(request.DefaultRequiredAction))
            errors[nameof(request.DefaultRequiredAction)] = "الإجراء المطلوب الافتراضي مطلوب.";

        if (request.DepartmentIds == null || request.DepartmentIds.Count == 0)
            errors[nameof(request.DepartmentIds)] = "يجب تحديد إدارة واحدة على الأقل لتوجيه المعاملة إليها.";

        return errors;
    }
}
