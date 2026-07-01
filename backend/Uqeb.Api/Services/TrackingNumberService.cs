using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Uqeb.Api.Data;

namespace Uqeb.Api.Services;

public class TrackingNumberService : ITrackingNumberService
{
    private const string NextSequenceValueSql = "SELECT NEXT VALUE FOR [TransactionTrackingSequence]";
    private readonly AppDbContext _db;

    public TrackingNumberService(AppDbContext db) => _db = db;

    public async Task<string> GenerateNextAsync(CancellationToken cancellationToken = default)
    {
        var sequenceValue = await GetNextSequenceValueAsync(cancellationToken);
        var year = DateTime.UtcNow.Year;
        return $"UQEB-{year}-{sequenceValue:D5}";
    }

    private async Task<long> GetNextSequenceValueAsync(CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await _db.Database.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = NextSequenceValueSql;

        var transaction = _db.Database.CurrentTransaction;
        if (transaction != null)
            command.Transaction = transaction.GetDbTransaction();

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }
}
