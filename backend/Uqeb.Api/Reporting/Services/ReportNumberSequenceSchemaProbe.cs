using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;

namespace Uqeb.Api.Reporting.Services;

public interface IReportNumberSequenceSchemaProbe
{
    Task<bool> IsTableAvailableAsync(CancellationToken cancellationToken = default);
}

public sealed class ReportNumberSequenceSchemaProbe : IReportNumberSequenceSchemaProbe
{
    internal const string TableExistsSql = """
        SELECT
            CASE
                WHEN OBJECT_ID(N'dbo.ReportNumberSequences', N'U') IS NOT NULL THEN CAST(1 AS bit)
                ELSE CAST(0 AS bit)
            END AS [Value]
        """;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ReportNumberSequenceSchemaProbe(IDbContextFactory<AppDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task<bool> IsTableAvailableAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        if (!db.Database.IsSqlServer())
            return true;

        try
        {
            var exists = await db.Database
                .SqlQueryRaw<bool>(TableExistsSql)
                .SingleAsync(cancellationToken);

            return exists;
        }
        catch (SqlException)
        {
            return false;
        }
    }
}
