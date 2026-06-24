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
    private const string TableExistsSql = """
        SELECT CASE WHEN OBJECT_ID(N'dbo.ReportNumberSequences', N'U') IS NOT NULL THEN 1 ELSE 0 END
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
                .SqlQueryRaw<int>(TableExistsSql)
                .SingleAsync(cancellationToken);

            return exists == 1;
        }
        catch (SqlException)
        {
            return false;
        }
    }
}
