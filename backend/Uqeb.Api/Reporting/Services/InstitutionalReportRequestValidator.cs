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
    }

    internal static List<ReportSectionId> ResolveSections(ReportBuildRequestDto request)
    {
        ValidateBuildRequest(request);
        return request.SectionIds.Distinct().ToList();
    }
}
