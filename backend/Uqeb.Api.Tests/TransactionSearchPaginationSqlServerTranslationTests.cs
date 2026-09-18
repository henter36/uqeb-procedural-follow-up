using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Helpers;
using Xunit;

namespace Uqeb.Api.Tests;

// Proves the SQL Server provider can translate the query shapes ApplySort/ApplyKeysetFilter build,
// specifically for the provider-sensitive expressions the refactor touched: nullable-navigation
// coalescing (IncomingFrom/Category), string CompareTo keyset comparisons, and nullable-date keyset
// comparisons. TransactionSearchPaginationRegressionTests exercises the EF Core InMemory provider,
// which evaluates a different translation path and would not catch a SQL-Server-only translation
// failure - ToQueryString() forces EF Core to run the real relational query compiler without needing
// a live connection, so this runs unconditionally in every environment (no UQEB_TRANSACTION_TEST_
// CONNECTION required).
public class TransactionSearchPaginationSqlServerTranslationTests
{
    // Never opened - ToQueryString() only needs a provider and model, not a live connection.
    private const string TranslationOnlyConnectionString =
        "Server=localhost;Database=UqebTranslationCheck;TrustServerCertificate=True;Connect Timeout=1";

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(TranslationOnlyConnectionString).Options);

    public static IEnumerable<object[]> ProviderSensitiveSortModes()
    {
        foreach (var sortBy in new[] { "incomingfrom", "category", "incomingnumber", "subject", "responseduedate" })
        foreach (var sortDesc in new[] { true, false })
            yield return new object[] { sortBy, sortDesc };
    }

    [Theory]
    [MemberData(nameof(ProviderSensitiveSortModes))]
    public void ApplySort_TranslatesToSqlServerSql_ForEveryProviderSensitiveSortMode(string sortBy, bool sortDesc)
    {
        using var db = CreateDb();

        var sql = TransactionSearchPagination.ApplySort(db.Transactions.AsNoTracking(), sortBy, sortDesc)
            .ToQueryString();

        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(ProviderSensitiveSortModes))]
    public void ApplyKeysetFilter_TranslatesToSqlServerSql_ForEveryProviderSensitiveSortMode(string sortBy, bool sortDesc)
    {
        using var db = CreateDb();
        var primary = sortBy == "responseduedate" ? "2026-01-01T00:00:00.0000000" : "Sample Value";
        var cursor = new TransactionSearchCursorPayload
        {
            SortBy = sortBy,
            SortDesc = sortDesc,
            Primary = primary,
            Id = 1
        };

        var ordered = TransactionSearchPagination.ApplySort(db.Transactions.AsNoTracking(), sortBy, sortDesc);
        var sql = TransactionSearchPagination.ApplyKeysetFilter(ordered, sortBy, sortDesc, cursor).ToQueryString();

        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplyKeysetFilter_ResponseDueDate_NullCursorPrimary_TranslatesToSqlServerSql(bool sortDesc)
    {
        using var db = CreateDb();
        var cursor = new TransactionSearchCursorPayload
        {
            SortBy = "responseduedate",
            SortDesc = sortDesc,
            Primary = null,
            Id = 1
        };

        var ordered = TransactionSearchPagination.ApplySort(db.Transactions.AsNoTracking(), "responseduedate", sortDesc);
        var sql = TransactionSearchPagination.ApplyKeysetFilter(ordered, "responseduedate", sortDesc, cursor)
            .ToQueryString();

        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
    }
}
