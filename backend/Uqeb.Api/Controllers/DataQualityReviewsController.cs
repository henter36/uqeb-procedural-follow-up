using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Authorization;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.DataQuality;
using Uqeb.Api.Services;

namespace Uqeb.Api.Controllers;

[ApiController]
[Route("api/data-quality/reviews")]
[Authorize]
public sealed class DataQualityReviewsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;

    public DataQualityReviewsController(
        AppDbContext db,
        ICurrentUserService currentUser,
        IAuditService audit)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
    }

    [HttpPut]
    [RequirePermission(PermissionCode.DataQualityReview)]
    public async Task<IActionResult> MarkReviewed(
        [FromBody] MarkDataQualityReviewRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { message = "Review request is required." });

        if (string.IsNullOrWhiteSpace(request.IssueKey))
            return BadRequest(new { message = "IssueKey is required." });

        if (string.IsNullOrWhiteSpace(request.RuleCode))
            return BadRequest(new { message = "RuleCode is required." });

        var now = DateTime.UtcNow;
        var issueKey = request.IssueKey.Trim();
        var review = await _db.DataQualityReviews.SingleOrDefaultAsync(x => x.IssueKey == issueKey, ct);
        var isNew = review is null;
        var oldValue = review is null ? null : ReviewAuditValue(review);

        if (review is null)
        {
            review = new DataQualityReview
            {
                IssueKey = issueKey,
                TransactionId = request.TransactionId,
                RuleCode = request.RuleCode.Trim(),
            };
            _db.DataQualityReviews.Add(review);
        }

        review.IsReviewed = true;
        review.TransactionId = request.TransactionId;
        review.RuleCode = request.RuleCode.Trim();
        review.ReviewNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        review.ReviewedAtUtc = now;
        review.ReviewedByUserId = _currentUser.UserId;

        if (isNew)
            await _db.SaveChangesAsync(ct);

        _audit.TrackLog(
            _currentUser.UserId,
            AuditAction.MarkDataQualityIssueReviewed,
            "DataQualityReview",
            review.Id,
            request.TransactionId,
            oldValue,
            ReviewAuditValue(review));

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete]
    [RequirePermission(PermissionCode.DataQualityReview)]
    public async Task<IActionResult> UnmarkReviewed(
        [FromQuery] string? issueKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(issueKey))
            return BadRequest(new { message = "IssueKey is required." });

        issueKey = issueKey.Trim();
        var review = await _db.DataQualityReviews.SingleOrDefaultAsync(x => x.IssueKey == issueKey, ct);
        if (review is null)
            return NoContent();

        var oldValue = ReviewAuditValue(review);
        review.IsReviewed = false;
        review.ReviewedAtUtc = DateTime.UtcNow;
        review.ReviewedByUserId = _currentUser.UserId;

        _audit.TrackLog(
            _currentUser.UserId,
            AuditAction.UnmarkDataQualityIssueReviewed,
            "DataQualityReview",
            review.Id,
            review.TransactionId,
            oldValue,
            ReviewAuditValue(review));

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string ReviewAuditValue(DataQualityReview review) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            review.IssueKey,
            review.TransactionId,
            review.RuleCode,
            review.IsReviewed,
            review.ReviewNote,
            review.ReviewedAtUtc,
            review.ReviewedByUserId
        });
}
