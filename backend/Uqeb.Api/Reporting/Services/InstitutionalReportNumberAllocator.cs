using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;

namespace Uqeb.Api.Reporting.Services;

public interface IInstitutionalReportNumberAllocator
{
    Task<string> AllocateAsync(CancellationToken ct = default);
}

/// <summary>
/// Allocates monotonic report numbers per calendar year using an atomic SQL Server increment,
/// with an EF Core fallback for non-SQL providers and missing sequence tables during development.
/// </summary>
public sealed class InstitutionalReportNumberAllocator : IInstitutionalReportNumberAllocator
{
    private const int MaxAttempts = 5;

    private const string AllocateNextNumberSql = """
        SET NOCOUNT ON;
        DECLARE @Next TABLE (NextNumber INT);

        UPDATE r WITH (UPDLOCK, ROWLOCK)
        SET LastNumber = LastNumber + 1
        OUTPUT INSERTED.LastNumber INTO @Next(NextNumber)
        FROM ReportNumberSequences r
        WHERE r.Year = @year;

        IF NOT EXISTS (SELECT 1 FROM @Next)
        BEGIN
            INSERT INTO ReportNumberSequences (Year, LastNumber) VALUES (@year, 1);
            INSERT INTO @Next VALUES (1);
        END

        SELECT NextNumber FROM @Next;
        """;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<InstitutionalReportNumberAllocator> _logger;

    public InstitutionalReportNumberAllocator(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<InstitutionalReportNumberAllocator> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<string> AllocateAsync(CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (db.Database.IsSqlServer())
        {
            try
            {
                return await AllocateWithSqlServerAsync(db, year, ct);
            }
            catch (Exception ex) when (SqlExceptionHelper.ShouldFallbackReportNumberAllocationToEf(ex))
            {
                _logger.LogWarning(
                    ex,
                    "SQL Server report number allocation unavailable; falling back to EF Core for year {Year}.",
                    year);
            }
        }

        return await AllocateWithEfCoreAsync(db, year, ct);
    }

    private async Task<string> AllocateWithSqlServerAsync(AppDbContext db, int year, CancellationToken ct)
    {
        Exception? lastRetryable = null;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);

                var nextNumber = await db.Database
                    .SqlQueryRaw<int>(AllocateNextNumberSql, new SqlParameter("@year", year))
                    .SingleAsync(ct);

                await transaction.CommitAsync(ct);
                return $"REP-{year}-{nextNumber:D6}";
            }
            catch (Exception ex) when (attempt < MaxAttempts - 1 && SqlExceptionHelper.ShouldRetryReportNumberAllocation(ex))
            {
                lastRetryable = ex;
            }
        }

        throw new InvalidOperationException(
            "تعذر تخصيص رقم تقرير فريد.",
            lastRetryable);
    }

    private static async Task<string> AllocateWithEfCoreAsync(AppDbContext db, int year, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var number = await AllocateWithEfCoreCoreAsync(db, year, ct);
            await transaction.CommitAsync(ct);
            return number;
        }

        return await AllocateWithEfCoreCoreAsync(db, year, ct);
    }

    private static async Task<string> AllocateWithEfCoreCoreAsync(AppDbContext db, int year, CancellationToken ct)
    {
        var sequence = await db.ReportNumberSequences
            .FirstOrDefaultAsync(s => s.Year == year, ct);

        if (sequence is null)
        {
            sequence = new ReportNumberSequence { Year = year, LastNumber = 0 };
            db.ReportNumberSequences.Add(sequence);
            await db.SaveChangesAsync(ct);
        }

        sequence.LastNumber++;
        await db.SaveChangesAsync(ct);

        return $"REP-{year}-{sequence.LastNumber:D6}";
    }
}
