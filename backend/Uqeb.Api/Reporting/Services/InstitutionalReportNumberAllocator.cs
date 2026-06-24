using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Reporting.Operations;

namespace Uqeb.Api.Reporting.Services;

public interface IInstitutionalReportNumberAllocator
{
    Task<string> AllocateAsync(CancellationToken ct = default);
}

/// <summary>
/// Allocates monotonic report numbers per calendar year for export only.
/// SQL Server uses an atomic increment; non-SQL providers use EF Core for tests.
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

    public InstitutionalReportNumberAllocator(IDbContextFactory<AppDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task<string> AllocateAsync(CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (db.Database.IsSqlServer())
            return await AllocateWithSqlServerAsync(db, year, ct);

        return await AllocateWithEfCoreAsync(db, year, ct);
    }

    private static async Task<string> AllocateWithSqlServerAsync(AppDbContext db, int year, CancellationToken ct)
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
            catch (Exception ex) when (SqlExceptionHelper.IsMissingReportNumberSequenceSchema(ex))
            {
                throw new ReportingConfigurationException(
                    ReportingErrorCodes.ReportNumberSequenceSchemaMissing,
                    "مخطط قاعدة بيانات التقارير غير مكتمل.");
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
        Exception? lastRetryable = null;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                if (db.Database.IsRelational())
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                    var number = await AllocateWithEfCoreCoreAsync(db, year, ct);
                    await transaction.CommitAsync(ct);
                    return number;
                }

                return await AllocateWithEfCoreCoreAsync(db, year, ct);
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

    private static async Task<string> AllocateWithEfCoreCoreAsync(AppDbContext db, int year, CancellationToken ct)
    {
        var sequence = await db.ReportNumberSequences
            .FirstOrDefaultAsync(s => s.Year == year, ct);

        if (sequence is null)
        {
            sequence = new ReportNumberSequence { Year = year, LastNumber = 1 };
            db.ReportNumberSequences.Add(sequence);
        }
        else
        {
            sequence.LastNumber++;
        }

        await db.SaveChangesAsync(ct);
        return $"REP-{year}-{sequence.LastNumber:D6}";
    }
}
