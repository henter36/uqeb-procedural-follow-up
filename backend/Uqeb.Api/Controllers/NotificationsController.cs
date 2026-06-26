using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IUserNotificationService _notifications;
    private readonly ICurrentUserService _currentUser;

    public NotificationsController(IUserNotificationService notifications, ICurrentUserService currentUser)
    {
        _notifications = notifications;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool unreadOnly = false, [FromQuery] DateTime? since = null, CancellationToken cancellationToken = default)
    {
        return Ok(await _notifications.ListForUserAsync(_currentUser.UserId, unreadOnly, since, cancellationToken: cancellationToken));
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken cancellationToken)
    {
        var result = await _notifications.MarkReadAsync(id, _currentUser.UserId, cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }
}
