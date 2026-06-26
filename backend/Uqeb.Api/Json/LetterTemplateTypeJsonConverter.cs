using System.Text.Json;
using System.Text.Json.Serialization;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Json;

public sealed class LetterTemplateTypeJsonConverter : JsonConverter<LetterTemplateType>
{
    public override LetterTemplateType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            throw new JsonException("نوع القالب مطلوب ولا يمكن أن يكون فارغًا.");

        if (reader.TokenType == JsonTokenType.Number &&
            reader.TryGetInt32(out var numericValue) &&
            Enum.IsDefined(typeof(LetterTemplateType), numericValue))
        {
            return (LetterTemplateType)numericValue;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (string.IsNullOrWhiteSpace(value))
                throw new JsonException("نوع القالب مطلوب ولا يمكن أن يكون نصًا فارغًا.");

            if (Enum.TryParse<LetterTemplateType>(value, ignoreCase: true, out var parsed) &&
                Enum.IsDefined(parsed))
            {
                return parsed;
            }

            throw new JsonException($"نوع القالب '{value}' غير معروف. استخدم إحدى القيم المعتمدة مثل: FollowUp أو FirstFollowUp أو SecondFollowUp.");
        }

        throw new JsonException("نوع القالب غير صالح. استخدم إحدى القيم النصية المعتمدة مثل FollowUp أو FirstFollowUp.");
    }

    public override void Write(Utf8JsonWriter writer, LetterTemplateType value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
