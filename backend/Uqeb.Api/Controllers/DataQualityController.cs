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
[Route("api/data-quality")]
[Authorize]
public sealed class DataQualityController : ControllerBase
{
    private readonly IDataQualityService _service;
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;

    public DataQualityController(
        IDataQualityService service,
        AppDbContext db,
        ICurrentUserService currentUser,
        IAuditService audit)
    {
        _service = service;
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
    }

    [HttpGet("summary")]
    [RequirePermission(PermissionCode.DataQualityView)]
    public async Task<ActionResult<DataQualitySummaryDto>> GetSummary(
        [FromQuery] DataQualityQueryDto query,
        CancellationToken ct)
    {
        var validation = ValidateQuery(query);
        if (validation is not null)
            return validation;

        return Ok(await _service.GetSummaryAsync(query, ct));
    }

    [HttpPut("reviews")]
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

    [HttpDelete("reviews")]
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

    private static BadRequestObjectResult? ValidateQuery(DataQualityQueryDto? query)
    {
        if (query is null)
            return new BadRequestObjectResult(new { message = "Query is required." });

        if (query.Limit is < 1 or > 1000)
            return new BadRequestObjectResult(new { message = "Limit must be between 1 and 1000." });

        if (query.OverdueMoreThanDays is < 0)
            return new BadRequestObjectResult(new { message = "OverdueMoreThanDays must be greater than or equal to 0." });

        if (query.ResponsePeriodLessThanDays is < 0)
            return new BadRequestObjectResult(new { message = "ResponsePeriodLessThanDays must be greater than or equal to 0." });

        return null;
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
