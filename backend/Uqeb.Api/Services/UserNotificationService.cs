using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Models.Entities;

namespace Uqeb.Api.Services;

public interface IUserNotificationService
{
    Task<UserNotificationDto> CreateAsync(
        int userId,
        string type,
        string title,
        string body,
        string? link = null,
        CancellationToken cancellationToken = default);

    Task<List<UserNotificationDto>> ListForUserAsync(
        int userId,
        bool unreadOnly = false,
        DateTime? since = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<UserNotificationDto?> MarkReadAsync(int notificationId, int userId, CancellationToken cancellationToken = default);
}

public sealed class UserNotificationService : IUserNotificationService
{
    private readonly AppDbContext _db;

    public UserNotificationService(AppDbContext db) => _db = db;

    public async Task<UserNotificationDto> CreateAsync(
        int userId,
        string type,
        string title,
        string body,
        string? link = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new InvalidOperationException("نوع الإشعار مطلوب.");
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("عنوان الإشعار مطلوب.");

        var notification = new UserNotification
        {
            UserId = userId,
            Type = type.Trim(),
            Title = title.Trim(),
            Body = body.Trim(),
            Link = string.IsNullOrWhiteSpace(link) ? null : link.Trim(),
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
        };

        _db.UserNotifications.Add(notification);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(notification);
    }

    public async Task<List<UserNotificationDto>> ListForUserAsync(
        int userId,
        bool unreadOnly = false,
        DateTime? since = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _db.UserNotifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly)
            query = query.Where(n => !n.IsRead);
        if (since.HasValue)
            query = query.Where(n => n.CreatedAt >= since.Value);

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(MapExpr)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserNotificationDto?> MarkReadAsync(int notificationId, int userId, CancellationToken cancellationToken = default)
    {
        var notification = await _db.UserNotifications.FirstOrDefaultAsync(
            n => n.Id == notificationId && n.UserId == userId,
            cancellationToken);

        if (notification == null)
            return null;

        notification.IsRead = true;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(notification);
    }

    private static UserNotificationDto Map(UserNotification notification) => new()
    {
        Id = notification.Id,
        Type = notification.Type,
        Title = notification.Title,
        Body = notification.Body,
        Link = notification.Link,
        IsRead = notification.IsRead,
        CreatedAt = notification.CreatedAt,
    };

    private static readonly System.Linq.Expressions.Expression<Func<UserNotification, UserNotificationDto>> MapExpr = n => new UserNotificationDto
    {
        Id = n.Id,
        Type = n.Type,
        Title = n.Title,
        Body = n.Body,
        Link = n.Link,
        IsRead = n.IsRead,
        CreatedAt = n.CreatedAt,
    };
}
