using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Configuration;
using Uqeb.Api.Reporting.DTOs;
using Uqeb.Api.Reporting.Enums;

namespace Uqeb.Api.Reporting.Services;

internal static class InstitutionalReportRequestValidator
{
    internal static void ValidateBuildRequest(ReportBuildRequestDto? request, ReportingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.IsDefined(request.ReportType))
            throw InvalidEnum("reportType", "نوع التقرير غير صالح.");

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

        NormalizeFilterLists(request.Filters);
        ValidateDepartmentTransactionsRequiresDepartments(request.ReportType, request.Filters);
        ValidatePriorityAndStatusValues(request.Filters);
        ValidateFilterDateRange(request.Filters);
        ValidateAnalyticalOptions(request, options?.Analysis);
    }

    internal static void ValidateDepartmentTransactionsRequiresDepartments(
        InstitutionalReportType reportType, ReportFiltersDto filters, string fieldKey = "filters.departmentIds")
    {
        if (reportType == InstitutionalReportType.DepartmentTransactions && filters.DepartmentIds.Count == 0)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                [fieldKey] = "يجب تحديد إدارة واحدة على الأقل لتقرير معاملات إدارة.",
            });
        }
    }

    private static void ValidatePriorityAndStatusValues(ReportFiltersDto filters)
    {
        foreach (var value in filters.Priorities)
        {
            if (!Enum.TryParse<Priority>(value, ignoreCase: true, out _))
            {
                throw new FieldValidationException(new Dictionary<string, string>
                {
                    ["filters.priorities"] = $"قيمة أولوية غير صالحة: {value}",
                });
            }
        }

        foreach (var value in filters.Statuses)
        {
            if (!Enum.TryParse<TransactionStatus>(value, ignoreCase: true, out _))
            {
                throw new FieldValidationException(new Dictionary<string, string>
                {
                    ["filters.statuses"] = $"قيمة حالة غير صالحة: {value}",
                });
            }
        }
    }

    internal static List<ReportSectionId> ResolveSections(ReportBuildRequestDto request)
    {
        ValidateBuildRequest(request);
        return request.SectionIds.Distinct().ToList();
    }

    private static void NormalizeFilterLists(ReportFiltersDto filters)
    {
        filters.DepartmentIds ??= [];
        filters.PartyIds ??= [];
        filters.CategoryIds ??= [];
        filters.Priorities ??= [];
        filters.Statuses ??= [];
    }

    private static void ValidateAnalyticalOptions(ReportBuildRequestDto request, ReportingAnalysisOptions? analysisOptions)
    {
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

        var analysis = analysisOptions ?? new ReportingAnalysisOptions();
        ValidatePositiveLimit(request.MaxCriticalCases, "maxCriticalCases", analysis.MaxExecutiveCriticalCases);
        ValidatePositiveLimit(request.MaxFindings, "maxFindings", analysis.MaxExecutiveFindings);
        ValidatePositiveLimit(request.MaxRecommendations, "maxRecommendations", analysis.MaxRecommendations);
    }

    private static void ValidateFilterDateRange(ReportFiltersDto filters)
    {
        if (filters.DateFrom is DateTime from
            && filters.DateTo is DateTime to
            && from.Date > to.Date)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                ["filters.dateFrom"] = "تاريخ البداية يجب أن يسبق أو يساوي تاريخ النهاية.",
                ["filters.dateTo"] = "تاريخ النهاية يجب أن يلي أو يساوي تاريخ البداية.",
            });
        }
    }

    private static FieldValidationException InvalidEnum(string field, string message) =>
        new(new Dictionary<string, string> { [field] = message });

    private static void ValidatePositiveLimit(int? value, string field, int configuredMaximum)
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

        if (value > configuredMaximum)
        {
            throw new FieldValidationException(new Dictionary<string, string>
            {
                [field] = $"الحد الأقصى المسموح هو {configuredMaximum}.",
            });
        }
    }
}
