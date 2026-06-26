using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.LetterTemplates;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Models.Letters;
using Uqeb.Api.Services;

namespace Uqeb.Api.Tests.Letters;

internal static class LettersTestInfrastructure
{
    internal static AppDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    internal static IOptions<FollowUpLettersOptions> CreateOptions(FollowUpLettersOptions? options = null) =>
        Options.Create(options ?? new FollowUpLettersOptions());

    internal static async Task<User> SeedUserAsync(AppDbContext db, int userId = 1)
    {
        var user = new User
        {
            Id = userId,
            Username = $"user-{userId}",
            PasswordHash = "hash",
            FullName = "Test User",
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    internal static async Task<LetterTemplate> SeedTemplateAsync(AppDbContext db, int templateId = 1)
    {
        var template = new LetterTemplate
        {
            Id = templateId,
            Code = FollowUpLetterRenderService.FollowUpTemplateCode,
            Name = "قالب التعقيب",
            TemplateType = LetterTemplateType.FollowUp,
            Content = FollowUpLetterRenderService.DefaultFollowUpContent,
            IsActive = true,
            IsDefault = true,
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
        };
        db.LetterTemplates.Add(template);
        await db.SaveChangesAsync();
        return template;
    }
}

internal sealed class TestCurrentUser(int userId, UserRole role = UserRole.Admin, int? departmentId = null)
    : ICurrentUserService
{
    public int UserId { get; } = userId;
    public string Username => "tester";
    public UserRole Role { get; } = role;
    public int? DepartmentId { get; } = departmentId;
    public bool IsAuthenticated => true;
}

internal sealed class FixedTimeZone(DateTime todayDisplayDate) : IFollowUpLetterTimeZone
{
    public TimeZoneInfo TimeZone { get; } = TimeZoneInfo.Utc;
    public DateTime TodayDisplayDate { get; } = todayDisplayDate.Date;

    public DateTime ToDisplayTime(DateTime utc) =>
        utc.Kind switch
        {
            DateTimeKind.Utc => utc,
            DateTimeKind.Local => utc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utc, DateTimeKind.Utc),
        };

    public DateTime? ToDisplayTime(DateTime? utc) => utc.HasValue ? ToDisplayTime(utc.Value) : null;
}

internal class NoOpAuditService : IAuditService
{
    public AuditLog TrackLog(int userId, AuditAction action, string? entityName, int? entityId, int? transactionId, string? oldValue, string? newValue) => new();
    public Task LogAsync(int userId, AuditAction action, string? entityName, int? entityId, int? transactionId, string? oldValue, string? newValue) => Task.CompletedTask;
}

internal sealed class CapturingAuditService : IAuditService
{
    public List<CapturedAuditEntry> Entries { get; } = [];

    public AuditLog TrackLog(
        int userId,
        AuditAction action,
        string? entityName,
        int? entityId,
        int? transactionId,
        string? oldValue,
        string? newValue)
    {
        Entries.Add(new CapturedAuditEntry(userId, action, entityName, entityId, transactionId, oldValue, newValue));
        return new AuditLog();
    }

    public Task LogAsync(
        int userId,
        AuditAction action,
        string? entityName,
        int? entityId,
        int? transactionId,
        string? oldValue,
        string? newValue)
    {
        Entries.Add(new CapturedAuditEntry(userId, action, entityName, entityId, transactionId, oldValue, newValue));
        return Task.CompletedTask;
    }

    public CapturedAuditEntry? LastEntry => Entries.Count > 0 ? Entries[^1] : null;

    public bool Contains(AuditAction action) => Entries.Any(e => e.Action == action);
}

internal sealed record CapturedAuditEntry(
    int UserId,
    AuditAction Action,
    string? EntityName,
    int? EntityId,
    int? TransactionId,
    string? OldValue,
    string? NewValue);

internal class StubRenderService(params FollowUpLetterTargetEntity[] targets) : IFollowUpLetterRenderService
{
    private readonly IReadOnlyList<FollowUpLetterTargetEntity> _targets =
        targets.Length > 0 ? targets : [new FollowUpLetterTargetEntity("جهة افتراضية", 1, null)];

    public virtual Task<IReadOnlyList<FollowUpLetterTargetEntity>> ResolveTargetEntitiesAsync(
        int transactionId,
        string? targetEntityHint = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_targets);

    public virtual Task<Dictionary<int, IReadOnlyList<FollowUpLetterTargetEntity>>> ResolveTargetEntitiesBulkAsync(
        IReadOnlyList<int> transactionIds,
        CancellationToken cancellationToken = default)
    {
        var result = transactionIds
            .Distinct()
            .ToDictionary(id => id, _ => (IReadOnlyList<FollowUpLetterTargetEntity>)_targets);
        return Task.FromResult(result);
    }

    public Task<FollowUpLetterPreviewDto?> RenderFollowUpLetterAsync(
        int transactionId,
        string? targetEntity,
        string? optionalEditedContent,
        ICurrentUserService currentUser,
        int? templateId = null,
        int? responseDeadlineDays = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<FollowUpLetterPreviewDto?>(new FollowUpLetterPreviewDto());

    public virtual Task<FollowUpLetterDocumentModel?> BuildDocumentAsync(FollowUpLetterBuildRequest request) =>
        Task.FromResult<FollowUpLetterDocumentModel?>(new FollowUpLetterDocumentModel());

    public Task<byte[]?> GeneratePdfAsync(
        int transactionId,
        string? targetEntity,
        string? optionalEditedContent,
        ICurrentUserService currentUser,
        int? templateId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>([]);

    public Task<string?> GeneratePrintViewHtmlAsync(
        int transactionId,
        string? targetEntity,
        string? optionalEditedContent,
        ICurrentUserService currentUser,
        int? templateId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<string> GenerateTemplatePreviewHtmlAsync(
        LetterTemplatePreviewRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(FollowUpLetterPrintViewRenderer.Render([
            new FollowUpLetterDocumentModel
            {
                Body = request.Content,
                Recipient = "جهة",
                Subject = "موضوع",
                LetterNumber = "1",
                GregorianDate = "26/06/2026",
                HijriDate = "هجري",
            },
        ]));

    public Task<bool> CanAccessTransactionAsync(
        int transactionId,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<LetterTemplate> GetOrCreateDefaultTemplateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new LetterTemplate
        {
            Id = 1,
            Code = FollowUpLetterRenderService.FollowUpTemplateCode,
            Name = "قالب افتراضي",
            Content = FollowUpLetterRenderService.DefaultFollowUpContent,
            IsActive = true,
            IsDefault = true,
        });
}
