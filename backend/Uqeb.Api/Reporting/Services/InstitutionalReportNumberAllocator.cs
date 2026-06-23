using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;

namespace Uqeb.Api.Reporting.Services;

public interface IInstitutionalReportNumberAllocator
{
    Task<string> AllocateAsync(CancellationToken ct = default);
}

/// <summary>
/// Allocates monotonic report numbers per calendar year using a dedicated sequence row.
/// Uses a process-wide gate plus optimistic retry to avoid duplicate numbers under concurrency.
/// </summary>
public sealed class InstitutionalReportNumberAllocator : IInstitutionalReportNumberAllocator
{
    private const int MaxAttempts = 5;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public InstitutionalReportNumberAllocator(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<string> AllocateAsync(CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;

        await Gate.WaitAsync(ct);
        try
        {
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                var sequence = await db.ReportNumberSequences.FirstOrDefaultAsync(s => s.Year == year, ct);
                if (sequence == null)
                {
                    sequence = new ReportNumberSequence { Year = year, LastNumber = 0 };
                    db.ReportNumberSequences.Add(sequence);
                }

                sequence.LastNumber++;
                try
                {
                    await db.SaveChangesAsync(ct);
                    return $"REP-{year}-{sequence.LastNumber:D6}";
                }
                catch (DbUpdateException) when (attempt < MaxAttempts - 1)
                {
                    // Retry when the year row is created concurrently.
                }
            }
        }
        finally
        {
            Gate.Release();
        }

        throw new InvalidOperationException("تعذر تخصيص رقم تقرير فريد.");
    }
}
