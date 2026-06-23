using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Helpers;

namespace Uqeb.Api.Reporting.Services;

public interface IInstitutionalReportNumberAllocator
{
    Task<string> AllocateAsync(CancellationToken ct = default);
}

/// <summary>
/// Allocates monotonic report numbers per calendar year using an atomic SQL Server increment.
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

    public InstitutionalReportNumberAllocator(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<string> AllocateAsync(CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        Exception? lastRetryable = null;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
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
}
