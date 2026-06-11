using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Uqeb.Api.Helpers;

public static class SqlExceptionHelper
{
    public static bool IsDuplicateKey(DbUpdateException ex, string indexOrConstraintName)
    {
        if (ex.InnerException is not SqlException sql)
            return false;

        if (sql.Number is not (2601 or 2627))
            return false;

        return sql.Message.Contains(indexOrConstraintName, StringComparison.OrdinalIgnoreCase);
    }
}
