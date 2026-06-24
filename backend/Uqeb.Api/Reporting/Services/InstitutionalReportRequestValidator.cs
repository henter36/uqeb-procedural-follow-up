using Uqeb.Api.Helpers;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Reporting.Services;

internal static class InstitutionalReportRequestValidator
{
    internal static void ValidateBuildRequest(ReportBuildRequestDto? request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SectionIds is null || request.SectionIds.Count == 0)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["sectionIds"] = "يجب تحديد قسم واحد على الأقل في التقرير.",
            });
        }

        if (request.Filters is null)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["filters"] = "الفلاتر مطلوبة.",
            });
        }

        if (request.Filters.DateFrom is DateTime from
            && request.Filters.DateTo is DateTime to
            && from.Date > to.Date)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["filters.dateFrom"] = "تاريخ البداية يجب أن يسبق أو يساوي تاريخ النهاية.",
                ["filters.dateTo"] = "تاريخ النهاية يجب أن يلي أو يساوي تاريخ البداية.",
            });
        }

        if (request.ContentLevel.HasValue && !Enum.IsDefined(request.ContentLevel.Value))
            throw InvalidEnum("contentLevel", "مستوى المحتوى غير صالح.");

        if (request.ComparisonMode.HasValue && !Enum.IsDefined(request.ComparisonMode.Value))
            throw InvalidEnum("comparisonMode", "نمط المقارنة غير صالح.");

        if (request.TimeGrouping.HasValue && !Enum.IsDefined(request.TimeGrouping.Value))
            throw InvalidEnum("timeGrouping", "تجميع الزمن غير صالح.");

        if (request.ComparisonMode == ReportComparisonMode.Custom
            && (!request.ComparisonDateFrom.HasValue || !request.ComparisonDateTo.HasValue))
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["comparisonDateFrom"] = "بداية فترة المقارنة مطلوبة عند اختيار مقارنة مخصصة.",
                ["comparisonDateTo"] = "نهاية فترة المقارنة مطلوبة عند اختيار مقارنة مخصصة.",
            });
        }

        if (request.ComparisonDateFrom is DateTime comparisonFrom
            && request.ComparisonDateTo is DateTime comparisonTo
            && comparisonFrom.Date > comparisonTo.Date)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["comparisonDateFrom"] = "تاريخ بداية المقارنة يجب أن يسبق أو يساوي تاريخ النهاية.",
                ["comparisonDateTo"] = "تاريخ نهاية المقارنة يجب أن يلي أو يساوي تاريخ البداية.",
            });
        }

        ValidatePositiveLimit(request.MaxCriticalCases, "maxCriticalCases");
        ValidatePositiveLimit(request.MaxFindings, "maxFindings");
        ValidatePositiveLimit(request.MaxRecommendations, "maxRecommendations");
    }

    internal static List<ReportSectionId> ResolveSections(ReportBuildRequestDto request)
    {
        ValidateBuildRequest(request);
        return request.SectionIds.Distinct().ToList();
    }

    private static FieldValidationException InvalidEnum(string field, string message) =>
        new(new Dictionary<string, string> { [field] = message });

    private static void ValidatePositiveLimit(int? value, string field)
    {
        if (value is null)
            return;

        if (value <= 0)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                [field] = "يجب أن تكون القيمة أكبر من صفر.",
            });
        }
    }
}
