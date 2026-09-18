using System.Globalization;
using System.Linq.Expressions;
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
    bool IsOverdue,
    int? RecurringTemplateId,
    string? RecurringPeriodLabel,
    RecurrenceType? RecurringRecurrenceType);

public static class TransactionSearchPagination
{
    public const string OffsetMode = "offset";
    public const string CursorMode = "cursor";

    // Sort field keys accepted by SortBy/cursor payloads. Shared across ApplySort, ApplyKeysetFilter
    // and ExtractPrimaryValue so the three stay in sync by construction instead of by copy-pasted literals.
    private const string IncomingNumberSortKey = "incomingnumber";
    private const string IncomingDateSortKey = "incomingdate";
    private const string SubjectSortKey = "subject";
    private const string IncomingFromSortKey = "incomingfrom";
    private const string CategorySortKey = "category";
    private const string PrioritySortKey = "priority";
    private const string StatusSortKey = "status";
    private const string ResponseDueDateSortKey = "responseduedate";
    private const string CreatedAtSortKey = "createdat";

    // Same "prefer the linked record's name, fall back to the free-text field" rule the row projection
    // uses (see ResolveIncomingFrom below), expressed once so ApplySort's ORDER BY can reuse it directly.
    // EF translates an optional navigation's property access to NULL when unmatched, so this is
    // equivalent to the old `x != null ? x.Name : ...` ternary, just without the redundant null check.
    private static readonly Expression<Func<Transaction, string>> IncomingFromDisplaySelector = t =>
        t.IncomingFromParty!.Name ?? t.IncomingFromDepartment!.Name ?? t.IncomingFrom ?? string.Empty;

    private static readonly Expression<Func<Transaction, string>> CategoryDisplaySelector = t =>
        t.CategoryEntity!.Name ?? t.Category ?? string.Empty;

    public static bool IsCursorMode(string? paginationMode) =>
        string.Equals(paginationMode, CursorMode, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeSortBy(string? sortBy) =>
        (sortBy ?? IncomingDateSortKey).Trim().ToLowerInvariant();

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
            IncomingNumberSortKey => OrderBySelector(query, t => t.IncomingNumber, sortDesc),
            IncomingDateSortKey => OrderBySelector(query, t => t.IncomingDate, sortDesc),
            SubjectSortKey => OrderBySelector(query, t => t.Subject, sortDesc),
            IncomingFromSortKey => OrderBySelector(query, IncomingFromDisplaySelector, sortDesc),
            CategorySortKey => OrderBySelector(query, CategoryDisplaySelector, sortDesc),
            PrioritySortKey => OrderBySelector(query, t => t.Priority, sortDesc),
            StatusSortKey => OrderBySelector(query, t => t.Status, sortDesc),
            ResponseDueDateSortKey => OrderBySelector(query, t => t.ResponseDueDate, sortDesc),
            CreatedAtSortKey => OrderBySelector(query, t => t.CreatedAt, sortDesc),
            _ => OrderBySelector(query, t => t.IncomingDate, sortDesc)
        };

    // Every sortable field uses the same "Id DESC/ASC" tie-breaker so pagination stays deterministic
    // when many rows share the same primary sort value.
    private static IOrderedQueryable<Transaction> OrderBySelector<TKey>(
        IQueryable<Transaction> query,
        Expression<Func<Transaction, TKey>> keySelector,
        bool sortDesc) =>
        sortDesc
            ? query.OrderByDescending(keySelector).ThenByDescending(t => t.Id)
            : query.OrderBy(keySelector).ThenBy(t => t.Id);

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
            IncomingNumberSortKey => ApplyStringKeyset(query, primary ?? string.Empty, id, sortDesc, isIncomingNumber: true),
            IncomingDateSortKey => ApplyIncomingDateKeyset(query, ParseDateTime(primary, sortBy), id, sortDesc),
            SubjectSortKey => ApplyStringKeyset(query, primary ?? string.Empty, id, sortDesc, isIncomingNumber: false),
            IncomingFromSortKey => ApplyIncomingFromKeyset(query, primary ?? string.Empty, id, sortDesc),
            CategorySortKey => ApplyCategoryKeyset(query, primary ?? string.Empty, id, sortDesc),
            PrioritySortKey => ApplyPriorityKeyset(query, ParseEnum<Priority>(primary, sortBy), id, sortDesc),
            StatusSortKey => ApplyStatusKeyset(query, ParseEnum<TransactionStatus>(primary, sortBy), id, sortDesc),
            ResponseDueDateSortKey => ApplyNullableDateTimeKeyset(query, ParseNullableDateTime(primary), id, sortDesc),
            CreatedAtSortKey => ApplyCreatedAtKeyset(query, ParseDateTime(primary, sortBy), id, sortDesc),
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
            IncomingNumberSortKey => row.IncomingNumber,
            IncomingDateSortKey => row.IncomingDate.ToString("O", CultureInfo.InvariantCulture),
            SubjectSortKey => row.Subject,
            IncomingFromSortKey => ResolveIncomingFrom(row),
            CategorySortKey => row.CategoryName ?? string.Empty,
            PrioritySortKey => ((int)row.Priority).ToString(),
            StatusSortKey => ((int)row.Status).ToString(),
            ResponseDueDateSortKey => row.ResponseDueDate?.ToString("O", CultureInfo.InvariantCulture),
            CreatedAtSortKey => row.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            _ => row.IncomingDate.ToString("O", CultureInfo.InvariantCulture)
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
        bool sortDesc) =>
        sortDesc
            ? query.Where(t => (t.IncomingFromParty!.Name ?? t.IncomingFromDepartment!.Name ?? t.IncomingFrom ?? "").CompareTo(primary) < 0
                || ((t.IncomingFromParty!.Name ?? t.IncomingFromDepartment!.Name ?? t.IncomingFrom ?? "") == primary && t.Id < id))
            : query.Where(t => (t.IncomingFromParty!.Name ?? t.IncomingFromDepartment!.Name ?? t.IncomingFrom ?? "").CompareTo(primary) > 0
                || ((t.IncomingFromParty!.Name ?? t.IncomingFromDepartment!.Name ?? t.IncomingFrom ?? "") == primary && t.Id > id));

    private static IQueryable<Transaction> ApplyCategoryKeyset(
        IQueryable<Transaction> query,
        string primary,
        int id,
        bool sortDesc) =>
        sortDesc
            ? query.Where(t => (t.CategoryEntity!.Name ?? t.Category ?? "").CompareTo(primary) < 0
                || ((t.CategoryEntity!.Name ?? t.Category ?? "") == primary && t.Id < id))
            : query.Where(t => (t.CategoryEntity!.Name ?? t.Category ?? "").CompareTo(primary) > 0
                || ((t.CategoryEntity!.Name ?? t.Category ?? "") == primary && t.Id > id));

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
        if (sortDesc && primary == null)
            return query.Where(t => t.ResponseDueDate == null && t.Id < id);

        if (sortDesc)
            return query.Where(t =>
                t.ResponseDueDate == null
                || t.ResponseDueDate < primary
                || (t.ResponseDueDate == primary && t.Id < id));

        if (primary == null)
            return query.Where(t => t.ResponseDueDate != null || (t.ResponseDueDate == null && t.Id > id));

        return query.Where(t =>
            t.ResponseDueDate > primary
            || (t.ResponseDueDate == primary && t.Id > id));
    }

    private static DateTime ParseDateTime(string? value, string sortBy)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new InvalidTransactionSearchCursorException($"Cursor غير صالح لحقل {sortBy}.");
        }

        return parsed;
    }

    private static DateTime? ParseNullableDateTime(string? value) =>
        value == null ? null : ParseDateTime(value, ResponseDueDateSortKey);

    private static TEnum ParseEnum<TEnum>(string? value, string sortBy)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) || !int.TryParse(value, out var numeric))
            throw new InvalidTransactionSearchCursorException($"Cursor غير صالح لحقل {sortBy}.");

        return (TEnum)Enum.ToObject(typeof(TEnum), numeric);
    }
}
