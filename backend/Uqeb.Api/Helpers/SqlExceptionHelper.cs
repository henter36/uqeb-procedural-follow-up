using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Uqeb.Api.Helpers;

public static class SqlExceptionHelper
{
    public static bool IsDuplicateKey(DbUpdateException ex) =>
        TryGetSqlException(ex, out var sql) && sql.Number is 2601 or 2627;

    public static bool IsDuplicateKey(DbUpdateException ex, string indexOrConstraintName)
    {
        if (!TryGetSqlException(ex, out var sql))
            return false;

        if (sql.Number is not (2601 or 2627))
            return false;

        return sql.Message.Contains(indexOrConstraintName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDeadlock(Exception ex) =>
        TryGetSqlException(ex, out var sql) && sql.Number == 1205;

    public static bool ShouldRetryReportNumberAllocation(Exception ex) =>
        ex is DbUpdateConcurrencyException
        || (ex is DbUpdateException dbUpdate && IsDuplicateKey(dbUpdate))
        || IsDeadlock(ex)
        || (ex is SqlException sql && sql.Number is 2601 or 2627 or 1205);

    public static bool IsMissingReportNumberSequenceSchema(Exception ex) =>
        TryGetSqlException(ex, out var sql) && sql.Number is 208 or 2812;

    private static bool TryGetSqlException(Exception ex, out SqlException sql)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException)
            {
                sql = sqlException;
                return true;
            }
        }

        sql = null!;
        return false;
    }
}
