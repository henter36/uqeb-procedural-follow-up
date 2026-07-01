using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Helpers;

public sealed record TransactionSearchRow(
    int Id,
    string InternalTrackingNumber,
    string IncomingNumber,
    DateTime IncomingDate,
    string Subject,
    string? IncomingFrom,
    IncomingSourceType IncomingSourceType,
    string? IncomingFromPartyName,
    string? IncomingFromDepartmentName,
    string? OutgoingNumber,
    DateTime? OutgoingDate,
    TransactionStatus Status,
    Priority Priority,
    string? CategoryName,
    bool RequiresResponse,
    bool ResponseCompleted,
    DateTime? ResponseCompletedDate,
    int? ResponseDueDays,
    DateTime? ResponseDueDate,
    DateTime? ClosedAt,
    bool IsArchived,
    string CreatedByName,
    DateTime CreatedAt,
    bool HasPendingAssignments,
    bool IsResponseOverdue,
    bool IsOverdue);

public static class TransactionSearchPagination
{
    public const string OffsetMode = "offset";
    public const string CursorMode = "cursor";

    public static bool IsCursorMode(string? paginationMode) =>
        string.Equals(paginationMode, CursorMode, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeSortBy(string? sortBy) =>
        (sortBy ?? "incomingdate").Trim().ToLowerInvariant();

    public static void EnsureCursorMatchesRequest(TransactionSearchCursorPayload cursor, string sortBy, bool sortDesc)
    {
        if (!string.Equals(NormalizeSortBy(cursor.SortBy), sortBy, StringComparison.Ordinal))
            throw new InvalidTransactionSearchCursorException("Cursor لا يطابق خيارات الفرز.");

        if (cursor.SortDesc != sortDesc)
            throw new InvalidTransactionSearchCursorException("Cursor لا يطابق خيارات الفرز.");
    }

    public static IOrderedQueryable<Transaction> ApplySort(IQueryable<Transaction> query, string sortBy, bool sortDesc) =>
        sortBy switch
        {
            "incomingnumber" => sortDesc
                ? query.OrderByDescending(t => t.IncomingNumber).ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.IncomingNumber).ThenBy(t => t.Id),
            "incomingdate" => sortDesc
                ? query.OrderByDescending(t => t.IncomingDate).ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.IncomingDate).ThenBy(t => t.Id),
            "subject" => sortDesc
                ? query.OrderByDescending(t => t.Subject).ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.Subject).ThenBy(t => t.Id),
            "incomingfrom" => sortDesc
                ? query.OrderByDescending(t => t.IncomingFromParty != null ? t.IncomingFromParty.Name
                        : t.IncomingFromDepartment != null ? t.IncomingFromDepartment.Name
                        : t.IncomingFrom ?? "")
                    .ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.IncomingFromParty != null ? t.IncomingFromParty.Name
                        : t.IncomingFromDepartment != null ? t.IncomingFromDepartment.Name
                        : t.IncomingFrom ?? "")
                    .ThenBy(t => t.Id),
            "category" => sortDesc
                ? query.OrderByDescending(t => t.CategoryEntity != null ? t.CategoryEntity.Name : t.Category ?? "")
                    .ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.CategoryEntity != null ? t.CategoryEntity.Name : t.Category ?? "")
                    .ThenBy(t => t.Id),
            "priority" => sortDesc
                ? query.OrderByDescending(t => t.Priority).ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.Priority).ThenBy(t => t.Id),
            "status" => sortDesc
                ? query.OrderByDescending(t => t.Status).ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.Status).ThenBy(t => t.Id),
            "responseduedate" => sortDesc
                ? query.OrderByDescending(t => t.ResponseDueDate).ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.ResponseDueDate).ThenBy(t => t.Id),
            "createdat" => sortDesc
                ? query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id),
            _ => sortDesc
                ? query.OrderByDescending(t => t.IncomingDate).ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.IncomingDate).ThenBy(t => t.Id)
        };

    public static IQueryable<Transaction> ApplyKeysetFilter(
        IQueryable<Transaction> query,
        string sortBy,
        bool sortDesc,
        TransactionSearchCursorPayload cursor)
    {
        var primary = TransactionSearchCursorCodec.DeserializePrimary(cursor.Primary);
        var id = cursor.Id;

        return sortBy switch
        {
            "incomingnumber" => ApplyStringKeyset(query, primary ?? string.Empty, id, sortDesc, isIncomingNumber: true),
            "incomingdate" => ApplyIncomingDateKeyset(query, ParseDateTime(primary, sortBy), id, sortDesc),
            "subject" => ApplyStringKeyset(query, primary ?? string.Empty, id, sortDesc, isIncomingNumber: false),
            "incomingfrom" => ApplyIncomingFromKeyset(query, primary ?? string.Empty, id, sortDesc),
            "category" => ApplyCategoryKeyset(query, primary ?? string.Empty, id, sortDesc),
            "priority" => ApplyPriorityKeyset(query, ParseEnum<Priority>(primary, sortBy), id, sortDesc),
            "status" => ApplyStatusKeyset(query, ParseEnum<TransactionStatus>(primary, sortBy), id, sortDesc),
            "responseduedate" => ApplyNullableDateTimeKeyset(query, ParseNullableDateTime(primary), id, sortDesc),
            "createdat" => ApplyCreatedAtKeyset(query, ParseDateTime(primary, sortBy), id, sortDesc),
            _ => ApplyIncomingDateKeyset(query, ParseDateTime(primary, sortBy), id, sortDesc)
        };
    }

    public static TransactionSearchCursorPayload BuildCursorPayload(string sortBy, bool sortDesc, TransactionSearchRow row) =>
        new()
        {
            V = TransactionSearchCursorPayload.CurrentVersion,
            SortBy = sortBy,
            SortDesc = sortDesc,
            Primary = TransactionSearchCursorCodec.SerializePrimary(ExtractPrimaryValue(sortBy, row)),
            Id = row.Id
        };

    private static string? ExtractPrimaryValue(string sortBy, TransactionSearchRow row) =>
        sortBy switch
        {
            "incomingnumber" => row.IncomingNumber,
            "incomingdate" => row.IncomingDate.ToString("O"),
            "subject" => row.Subject,
            "incomingfrom" => ResolveIncomingFrom(row),
            "category" => row.CategoryName ?? string.Empty,
            "priority" => ((int)row.Priority).ToString(),
            "status" => ((int)row.Status).ToString(),
            "responseduedate" => row.ResponseDueDate?.ToString("O"),
            "createdat" => row.CreatedAt.ToString("O"),
            _ => row.IncomingDate.ToString("O")
        };

    private static string ResolveIncomingFrom(TransactionSearchRow row) =>
        row.IncomingSourceType switch
        {
            IncomingSourceType.Internal => row.IncomingFromDepartmentName ?? row.IncomingFrom ?? string.Empty,
            IncomingSourceType.External => row.IncomingFromPartyName ?? row.IncomingFrom ?? string.Empty,
            _ => row.IncomingFrom ?? string.Empty
        };

    private static IQueryable<Transaction> ApplyStringKeyset(
        IQueryable<Transaction> query,
        string primary,
        int id,
        bool sortDesc,
        bool isIncomingNumber)
    {
        if (isIncomingNumber)
        {
            return sortDesc
                ? query.Where(t => t.IncomingNumber.CompareTo(primary) < 0
                    || (t.IncomingNumber == primary && t.Id < id))
                : query.Where(t => t.IncomingNumber.CompareTo(primary) > 0
                    || (t.IncomingNumber == primary && t.Id > id));
        }

        return sortDesc
            ? query.Where(t => t.Subject.CompareTo(primary) < 0
                || (t.Subject == primary && t.Id < id))
            : query.Where(t => t.Subject.CompareTo(primary) > 0
                || (t.Subject == primary && t.Id > id));
    }

    private static IQueryable<Transaction> ApplyIncomingFromKeyset(
        IQueryable<Transaction> query,
        string primary,
        int id,
        bool sortDesc)
    {
        if (sortDesc)
        {
            return query.Where(t =>
                (t.IncomingFromParty != null ? t.IncomingFromParty.Name
                    : t.IncomingFromDepartment != null ? t.IncomingFromDepartment.Name
                    : t.IncomingFrom ?? "").CompareTo(primary) < 0
                || ((t.IncomingFromParty != null ? t.IncomingFromParty.Name
                        : t.IncomingFromDepartment != null ? t.IncomingFromDepartment.Name
                        : t.IncomingFrom ?? "") == primary
                    && t.Id < id));
        }

        return query.Where(t =>
            (t.IncomingFromParty != null ? t.IncomingFromParty.Name
                : t.IncomingFromDepartment != null ? t.IncomingFromDepartment.Name
                : t.IncomingFrom ?? "").CompareTo(primary) > 0
            || ((t.IncomingFromParty != null ? t.IncomingFromParty.Name
                    : t.IncomingFromDepartment != null ? t.IncomingFromDepartment.Name
                    : t.IncomingFrom ?? "") == primary
                && t.Id > id));
    }

    private static IQueryable<Transaction> ApplyCategoryKeyset(
        IQueryable<Transaction> query,
        string primary,
        int id,
        bool sortDesc)
    {
        if (sortDesc)
        {
            return query.Where(t =>
                (t.CategoryEntity != null ? t.CategoryEntity.Name : t.Category ?? "").CompareTo(primary) < 0
                || ((t.CategoryEntity != null ? t.CategoryEntity.Name : t.Category ?? "") == primary && t.Id < id));
        }

        return query.Where(t =>
            (t.CategoryEntity != null ? t.CategoryEntity.Name : t.Category ?? "").CompareTo(primary) > 0
            || ((t.CategoryEntity != null ? t.CategoryEntity.Name : t.Category ?? "") == primary && t.Id > id));
    }

    private static IQueryable<Transaction> ApplyIncomingDateKeyset(
        IQueryable<Transaction> query,
        DateTime primary,
        int id,
        bool sortDesc) =>
        sortDesc
            ? query.Where(t => t.IncomingDate < primary || (t.IncomingDate == primary && t.Id < id))
            : query.Where(t => t.IncomingDate > primary || (t.IncomingDate == primary && t.Id > id));

    private static IQueryable<Transaction> ApplyCreatedAtKeyset(
        IQueryable<Transaction> query,
        DateTime primary,
        int id,
        bool sortDesc) =>
        sortDesc
            ? query.Where(t => t.CreatedAt < primary || (t.CreatedAt == primary && t.Id < id))
            : query.Where(t => t.CreatedAt > primary || (t.CreatedAt == primary && t.Id > id));

    private static IQueryable<Transaction> ApplyPriorityKeyset(
        IQueryable<Transaction> query,
        Priority primary,
        int id,
        bool sortDesc) =>
        sortDesc
            ? query.Where(t => t.Priority < primary || (t.Priority == primary && t.Id < id))
            : query.Where(t => t.Priority > primary || (t.Priority == primary && t.Id > id));

    private static IQueryable<Transaction> ApplyStatusKeyset(
        IQueryable<Transaction> query,
        TransactionStatus primary,
        int id,
        bool sortDesc) =>
        sortDesc
            ? query.Where(t => t.Status < primary || (t.Status == primary && t.Id < id))
            : query.Where(t => t.Status > primary || (t.Status == primary && t.Id > id));

    private static IQueryable<Transaction> ApplyNullableDateTimeKeyset(
        IQueryable<Transaction> query,
        DateTime? primary,
        int id,
        bool sortDesc)
    {
        if (sortDesc)
        {
            if (primary == null)
                return query.Where(t => t.ResponseDueDate == null && t.Id < id);

            return query.Where(t =>
                t.ResponseDueDate == null
                || t.ResponseDueDate < primary
                || (t.ResponseDueDate == primary && t.Id < id));
        }

        if (primary == null)
            return query.Where(t => t.ResponseDueDate != null || (t.ResponseDueDate == null && t.Id > id));

        return query.Where(t =>
            t.ResponseDueDate > primary
            || (t.ResponseDueDate == primary && t.Id > id));
    }

    private static DateTime ParseDateTime(string? value, string sortBy)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new InvalidTransactionSearchCursorException($"Cursor غير صالح لحقل {sortBy}.");
        }

        return parsed;
    }

    private static DateTime? ParseNullableDateTime(string? value) =>
        value == null ? null : ParseDateTime(value, "responseduedate");

    private static TEnum ParseEnum<TEnum>(string? value, string sortBy)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) || !int.TryParse(value, out var numeric))
            throw new InvalidTransactionSearchCursorException($"Cursor غير صالح لحقل {sortBy}.");

        return (TEnum)Enum.ToObject(typeof(TEnum), numeric);
    }
}
