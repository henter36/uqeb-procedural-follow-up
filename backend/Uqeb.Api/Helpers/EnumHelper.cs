using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Helpers;

public static class EnumHelper
{
    public static TransactionStatus ParseTransactionStatus(string value) =>
        Enum.TryParse<TransactionStatus>(value, true, out var s) ? s : TransactionStatus.New;

    public static Priority ParsePriority(string value) =>
        Enum.TryParse<Priority>(value, true, out var p) ? p : Priority.Normal;

    public static ResponseType ParseResponseType(string value) =>
        Enum.TryParse<ResponseType>(value, true, out var r) ? r : ResponseType.None;

    public static UserRole ParseUserRole(string value) =>
        Enum.TryParse<UserRole>(value, true, out var r) ? r : UserRole.Reader;

    public static ReplyStatus ParseReplyStatus(string value) =>
        Enum.TryParse<ReplyStatus>(value, true, out var r) ? r : ReplyStatus.Pending;

    public static IncomingSourceType ParseIncomingSourceType(string value) =>
        Enum.TryParse<IncomingSourceType>(value, true, out var s) ? s : IncomingSourceType.External;
}
