using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.FollowUpPrint;
using Uqeb.Api.Exceptions;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public interface IFollowUpPrintAccessService
{
    Task EnsureCanViewJobAsync(int jobId, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task EnsureCanMutateJobAsync(int jobId, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task EnsureCanViewPartAsync(int jobId, int partNumber, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task EnsureCanPrintPartAsync(int jobId, int partNumber, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    Task EnsureCanViewPrintRecordAsync(int recordId, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
    IQueryable<Models.Entities.FollowUpPrintJob> ApplyJobListScope(IQueryable<Models.Entities.FollowUpPrintJob> query, ICurrentUserService currentUser);
}

public sealed class FollowUpPrintAccessService : IFollowUpPrintAccessService
{
    private readonly AppDbContext _db;

    public FollowUpPrintAccessService(AppDbContext db) => _db = db;

    public async Task EnsureCanViewJobAsync(int jobId, ICurrentUserService currentUser, CancellationToken cancellationToken = default)
    {
        if (await CanViewJobAsync(jobId, currentUser, cancellationToken))
            return;

        throw new FollowUpPrintForbiddenException();
    }

    public async Task EnsureCanMutateJobAsync(int jobId, ICurrentUserService currentUser, CancellationToken cancellationToken = default)
    {
        if (await CanMutateJobAsync(jobId, currentUser, cancellationToken))
            return;

        throw new FollowUpPrintForbiddenException();
    }

    public async Task EnsureCanViewPartAsync(int jobId, int partNumber, ICurrentUserService currentUser, CancellationToken cancellationToken = default)
    {
        await EnsureCanViewJobAsync(jobId, currentUser, cancellationToken);

        var exists = await _db.FollowUpPrintJobParts.AsNoTracking()
            .AnyAsync(p => p.JobId == jobId && p.PartNumber == partNumber, cancellationToken);

        if (!exists)
            throw new FollowUpPrintForbiddenException();
    }

    public async Task EnsureCanPrintPartAsync(int jobId, int partNumber, ICurrentUserService currentUser, CancellationToken cancellationToken = default)
    {
        await EnsureCanViewPartAsync(jobId, partNumber, currentUser, cancellationToken);

        if (currentUser.Role is UserRole.Admin or UserRole.Supervisor or UserRole.DataEntry)
            return;

        throw new FollowUpPrintForbiddenException();
    }

    public async Task EnsureCanViewPrintRecordAsync(int recordId, ICurrentUserService currentUser, CancellationToken cancellationToken = default)
    {
        var record = await _db.FollowUpLetterPrintRecords.AsNoTracking()
            .Select(r => new { r.Id, r.BatchJobId, r.PrintRequestedById })
            .FirstOrDefaultAsync(r => r.Id == recordId, cancellationToken)
            ?? throw new FollowUpPrintForbiddenException();

        if (record.BatchJobId.HasValue)
        {
            if (await CanViewJobAsync(record.BatchJobId.Value, currentUser, cancellationToken))
                return;
        }
        else if (currentUser.Role is UserRole.Admin or UserRole.Supervisor ||
                 record.PrintRequestedById == currentUser.UserId)
        {
            return;
        }

        throw new FollowUpPrintForbiddenException();
    }

    public IQueryable<Models.Entities.FollowUpPrintJob> ApplyJobListScope(
        IQueryable<Models.Entities.FollowUpPrintJob> query,
        ICurrentUserService currentUser)
    {
        if (currentUser.Role is UserRole.Admin or UserRole.Supervisor)
            return query;

        if (currentUser.Role == UserRole.DataEntry)
            return query.Where(j => j.RequestedById == currentUser.UserId);

        if (currentUser.Role == UserRole.DepartmentUser && currentUser.DepartmentId.HasValue)
        {
            var departmentId = currentUser.DepartmentId.Value;
            return query.Where(j =>
                j.RequestedById == currentUser.UserId ||
                j.ScopeDepartmentId == departmentId);
        }

        return query.Where(j => j.RequestedById == currentUser.UserId);
    }

    private async Task<bool> CanViewJobAsync(int jobId, ICurrentUserService currentUser, CancellationToken cancellationToken)
    {
        if (currentUser.Role is UserRole.Admin or UserRole.Supervisor)
            return true;

        var job = await _db.FollowUpPrintJobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new { j.RequestedById, j.ScopeDepartmentId })
            .FirstOrDefaultAsync(cancellationToken);

        if (job == null)
            return false;

        if (currentUser.Role == UserRole.DataEntry)
            return job.RequestedById == currentUser.UserId;

        if (currentUser.Role == UserRole.DepartmentUser && currentUser.DepartmentId.HasValue)
        {
            return job.RequestedById == currentUser.UserId ||
                   job.ScopeDepartmentId == currentUser.DepartmentId;
        }

        return job.RequestedById == currentUser.UserId;
    }

    private async Task<bool> CanMutateJobAsync(int jobId, ICurrentUserService currentUser, CancellationToken cancellationToken)
    {
        if (currentUser.Role is UserRole.Admin or UserRole.Supervisor)
            return true;

        if (currentUser.Role == UserRole.DataEntry)
        {
            return await _db.FollowUpPrintJobs.AsNoTracking()
                .AnyAsync(j => j.Id == jobId && j.RequestedById == currentUser.UserId, cancellationToken);
        }

        return false;
    }
}

public static class FollowUpPrintRequestHash
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static string Compute(CreateFollowUpPrintJobRequest request, int batchSize)
    {
        var canonical = new
        {
            filter = new
            {
                request.Filter.DaysSinceLastFollowUp,
                request.Filter.ExcludeRecentlyPrinted,
                request.Filter.PrintedLetterExclusionDays,
                request.Filter.DepartmentId,
                request.Filter.CategoryId,
                search = request.Filter.Search?.Trim(),
            },
            templateId = request.TemplateId,
            responseDeadlineDays = request.ResponseDeadlineDays,
            batchSize,
        };

        var json = JsonSerializer.Serialize(canonical, CanonicalJsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash);
    }
}
