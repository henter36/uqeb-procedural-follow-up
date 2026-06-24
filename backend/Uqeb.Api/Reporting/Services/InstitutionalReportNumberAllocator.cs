using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

        DECLARE @Next TABLE ([Value] int NOT NULL);

        UPDATE dbo.ReportNumberSequences WITH (UPDLOCK, HOLDLOCK)
        SET LastNumber = LastNumber + 1
        OUTPUT inserted.LastNumber INTO @Next ([Value])
        WHERE [Year] = @year;

        IF NOT EXISTS (SELECT 1 FROM @Next)
        BEGIN
            BEGIN TRY
                INSERT INTO dbo.ReportNumberSequences ([Year], LastNumber)
                OUTPUT inserted.LastNumber INTO @Next ([Value])
                VALUES (@year, 1);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() IN (2601, 2627)
                BEGIN
                    UPDATE dbo.ReportNumberSequences WITH (UPDLOCK, HOLDLOCK)
                    SET LastNumber = LastNumber + 1
                    OUTPUT inserted.LastNumber INTO @Next ([Value])
                    WHERE [Year] = @year;
                END
                ELSE
                BEGIN
                    THROW;
                END
            END CATCH
        END

        SELECT TOP (1) [Value]
        FROM @Next;
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
                var nextNumber = await ExecuteAllocateNextNumberAsync(db, year, ct);
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

    private static async Task<int> ExecuteAllocateNextNumberAsync(
        AppDbContext db,
        int year,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException(
                "يتطلب تخصيص رقم التقرير اتصال SQL Server.");

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        var transaction =
            db.Database.CurrentTransaction?.GetDbTransaction()
            as SqlTransaction;

        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandType = CommandType.Text;
        command.CommandText = AllocateNextNumberSql;

        command.Parameters.Add(
            new SqlParameter("@year", SqlDbType.Int)
            {
                Direction = ParameterDirection.Input,
                Value = year,
            });

        var result = await command.ExecuteScalarAsync(ct);

        if (result is null or DBNull)
        {
            throw new InvalidOperationException(
                "تعذر قراءة رقم التقرير التالي من قاعدة البيانات.");
        }

        return Convert.ToInt32(
            result,
            CultureInfo.InvariantCulture);
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
