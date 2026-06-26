namespace Uqeb.Api.Models.Enums;

using System.Text.Json.Serialization;
using Uqeb.Api.Json;

[JsonConverter(typeof(LetterTemplateTypeJsonConverter))]
public enum LetterTemplateType
{
    FollowUp = 1,
    FirstFollowUp = 2,
    SecondFollowUp = 3,
    UrgentFollowUp = 4,
    FinalFollowUp = 5,
    LateReply = 6,
    CompletionRequest = 7,
    InternalFollowUp = 8,
    ExternalFollowUp = 9
}
