using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Helpers;

public static class TransactionRequestValidator
{
    private const string OutgoingPartialMessage =
        "عند إدخال أي بيان من بيانات الصادر يجب إكمال رقم الصادر وتاريخ الصادر والإدارة الصادر لها.";
    private const string OutgoingDateRequiredWithNumberMessage = "تاريخ الصادر مطلوب عند إدخال رقم الصادر.";
    private const string IncomingDateFutureMessage = "تاريخ المعاملة لا يمكن أن يكون بعد تاريخ اليوم.";
    private const string OutgoingDateFutureMessage = "تاريخ الصادر لا يمكن أن يكون بعد تاريخ اليوم.";
    private const string OutgoingDateBeforeIncomingMessage = "تاريخ الصادر لا يمكن أن يكون قبل تاريخ المعاملة.";

    public static bool HasAnyOutgoingData(CreateTransactionRequest request) =>
        !string.IsNullOrWhiteSpace(request.OutgoingNumber)
        || request.OutgoingDate.HasValue
        || (request.OutgoingDepartmentIds?.Count > 0);

    public static Dictionary<string, string> ValidateIncomingSource(
        string? incomingSourceType,
        int? incomingFromPartyId,
        int? incomingFromDepartmentId)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(incomingSourceType)
            || !Enum.TryParse<IncomingSourceType>(incomingSourceType, true, out var sourceType))
        {
            errors[nameof(CreateTransactionRequest.IncomingSourceType)] = "يجب اختيار نوع الجهة الوارد منها.";
            return errors;
        }

        var hasParty = incomingFromPartyId.HasValue && incomingFromPartyId > 0;
        var hasDept = incomingFromDepartmentId.HasValue && incomingFromDepartmentId > 0;

        if (hasParty && hasDept)
        {
            errors[nameof(CreateTransactionRequest.IncomingSourceType)] =
                "لا يمكن اختيار جهة خارجية وإدارة داخلية في نفس الوقت.";
            return errors;
        }

        if (sourceType == IncomingSourceType.External)
        {
            if (!hasParty)
                errors[nameof(CreateTransactionRequest.IncomingFromPartyId)] =
                    "يجب اختيار جهة خارجية عند اختيار النوع خارجي.";
            if (hasDept)
                errors[nameof(CreateTransactionRequest.IncomingFromDepartmentId)] =
                    "لا يمكن اختيار جهة خارجية وإدارة داخلية في نفس الوقت.";
        }
        else if (sourceType == IncomingSourceType.Internal)
        {
            if (!hasDept)
                errors[nameof(CreateTransactionRequest.IncomingFromDepartmentId)] =
                    "يجب اختيار إدارة عند اختيار النوع داخلي.";
            if (hasParty)
                errors[nameof(CreateTransactionRequest.IncomingFromPartyId)] =
                    "لا يمكن اختيار جهة خارجية وإدارة داخلية في نفس الوقت.";
        }

        return errors;
    }

    public static Dictionary<string, string> ValidateCreate(CreateTransactionRequest request)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(request.IncomingNumber))
            errors[nameof(request.IncomingNumber)] = "رقم المعاملة مطلوب";
        if (request.IncomingDate == default)
            errors[nameof(request.IncomingDate)] = "تاريخ المعاملة مطلوب";
        if (string.IsNullOrWhiteSpace(request.Subject))
            errors[nameof(request.Subject)] = "الموضوع مطلوب";

        AddDateValidationErrors(errors, request.IncomingDate, request.OutgoingDate, request.OutgoingNumber);

        foreach (var (key, msg) in ValidateIncomingSource(
            request.IncomingSourceType, request.IncomingFromPartyId, request.IncomingFromDepartmentId))
            errors[key] = msg;

        if (!request.CategoryId.HasValue || request.CategoryId <= 0)
            errors[nameof(request.CategoryId)] = "التصنيف مطلوب";
        if (string.IsNullOrWhiteSpace(request.Priority) || !Enum.TryParse<Priority>(request.Priority, true, out _))
            errors[nameof(request.Priority)] = "الأولوية مطلوبة";

        if (HasAnyOutgoingData(request))
        {
            if (string.IsNullOrWhiteSpace(request.OutgoingNumber))
                errors[nameof(request.OutgoingNumber)] = OutgoingPartialMessage;
            if (!request.OutgoingDate.HasValue)
                errors[nameof(request.OutgoingDate)] = string.IsNullOrWhiteSpace(request.OutgoingNumber)
                    ? OutgoingPartialMessage
                    : OutgoingDateRequiredWithNumberMessage;
            if (request.OutgoingDepartmentIds == null || request.OutgoingDepartmentIds.Count == 0)
                errors[nameof(request.OutgoingDepartmentIds)] = OutgoingPartialMessage;
        }

        var responseType = EnumHelper.ParseResponseType(request.ResponseType ?? "External");
        if (responseType == ResponseType.None)
            errors[nameof(request.ResponseType)] = "نوع الإفادة مطلوب";
        if (!request.ResponseDueDays.HasValue || request.ResponseDueDays <= 0)
            errors[nameof(request.ResponseDueDays)] = "عدد أيام الرد مطلوب";

        return errors;
    }

    public static Dictionary<string, string> ValidateUpdate(
        UpdateTransactionRequest request,
        DateTime currentIncomingDate,
        DateTime? currentOutgoingDate,
        string? currentOutgoingNumber)
    {
        var errors = new Dictionary<string, string>();
        var incomingDate = request.IncomingDate ?? currentIncomingDate;
        var outgoingDate = request.OutgoingDate ?? currentOutgoingDate;
        var outgoingNumber = request.OutgoingNumber ?? currentOutgoingNumber;

        AddDateValidationErrors(errors, incomingDate, outgoingDate, outgoingNumber);
        return errors;
    }

    private static void AddDateValidationErrors(
        Dictionary<string, string> errors,
        DateTime incomingDate,
        DateTime? outgoingDate,
        string? outgoingNumber)
    {
        var today = DateTime.Today;

        if (incomingDate != default && incomingDate.Date > today)
            errors[nameof(CreateTransactionRequest.IncomingDate)] = IncomingDateFutureMessage;
        if (outgoingDate.HasValue && outgoingDate.Value.Date > today)
            errors[nameof(CreateTransactionRequest.OutgoingDate)] = OutgoingDateFutureMessage;
        if (outgoingDate.HasValue && incomingDate != default && outgoingDate.Value.Date < incomingDate.Date)
            errors[nameof(CreateTransactionRequest.OutgoingDate)] = OutgoingDateBeforeIncomingMessage;
        if (!string.IsNullOrWhiteSpace(outgoingNumber) && !outgoingDate.HasValue)
            errors[nameof(CreateTransactionRequest.OutgoingDate)] = OutgoingDateRequiredWithNumberMessage;
    }
}
