using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Uqeb.Api.Helpers;

public sealed class TransactionSearchCursorPayload
{
    public const int CurrentVersion = 1;

    public int V { get; set; } = CurrentVersion;
    public string SortBy { get; set; } = string.Empty;
    public bool SortDesc { get; set; }
    public string? Primary { get; set; }
    public int Id { get; set; }
}

public static class TransactionSearchCursorCodec
{
    private const string NullToken = "__null__";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Encode(TransactionSearchCursorPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return $"v{TransactionSearchCursorPayload.CurrentVersion}.{WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(json))}";
    }

    public static TransactionSearchCursorPayload Decode(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            throw new InvalidTransactionSearchCursorException("Cursor غير صالح.");

        var separatorIndex = cursor.IndexOf('.');
        if (separatorIndex <= 1 || cursor[0] != 'v')
            throw new InvalidTransactionSearchCursorException("Cursor غير صالح.");

        if (!int.TryParse(cursor.AsSpan(1, separatorIndex - 1), out var version)
            || version != TransactionSearchCursorPayload.CurrentVersion)
        {
            throw new InvalidTransactionSearchCursorException("Cursor غير مدعوم.");
        }

        byte[] payloadBytes;
        try
        {
            payloadBytes = WebEncoders.Base64UrlDecode(cursor[(separatorIndex + 1)..]);
        }
        catch (FormatException)
        {
            throw new InvalidTransactionSearchCursorException("Cursor غير صالح.");
        }

        TransactionSearchCursorPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TransactionSearchCursorPayload>(payloadBytes, JsonOptions);
        }
        catch (JsonException)
        {
            throw new InvalidTransactionSearchCursorException("Cursor غير صالح.");
        }

        if (payload == null || payload.Id <= 0 || string.IsNullOrWhiteSpace(payload.SortBy))
            throw new InvalidTransactionSearchCursorException("Cursor غير صالح.");

        if (payload.V != TransactionSearchCursorPayload.CurrentVersion)
            throw new InvalidTransactionSearchCursorException("Cursor غير مدعوم.");

        return payload;
    }

    public static string SerializePrimary(string? value) =>
        value ?? NullToken;

    public static string? DeserializePrimary(string? value) =>
        value == NullToken ? null : value;
}
