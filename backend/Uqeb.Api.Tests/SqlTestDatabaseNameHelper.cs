using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Uqeb.Api.Tests;

internal static class SqlTestDatabaseNameHelper
{
    private static readonly Regex AllowedNamePattern = new(
        @"^Uqeb_RefDataTest_[A-Za-z0-9_]+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string ValidateAndQuoteDatabaseName(SqlConnection connection, string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        if (!AllowedNamePattern.IsMatch(databaseName))
            throw new ArgumentException($"Invalid test database name: {databaseName}", nameof(databaseName));

        _ = connection;
        return new SqlCommandBuilder().QuoteIdentifier(databaseName);
    }
}
