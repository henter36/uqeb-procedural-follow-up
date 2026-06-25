using Uqeb.Api.Services;

namespace Uqeb.Api.Models.Letters;

public sealed record FollowUpLetterBuildRequest
{
    public required int TransactionId { get; init; }
    public required FollowUpLetterTargetEntity Target { get; init; }
    public required ICurrentUserService CurrentUser { get; init; }
    public int? TemplateId { get; init; }
    public string? BodyOverride { get; init; }
    public int? FollowUpSequenceOverride { get; init; }
    public int? ResponseDeadlineDays { get; init; }
    public CancellationToken CancellationToken { get; init; } = default;
}
