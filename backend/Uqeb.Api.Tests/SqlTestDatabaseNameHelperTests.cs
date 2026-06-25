using Microsoft.Data.SqlClient;
using Xunit;

namespace Uqeb.Api.Tests;

public class SqlTestDatabaseNameHelperTests
{
    [Theory]
    [InlineData("Uqeb_RefDataTest_foo;DROP")]
    [InlineData("Uqeb_RefDataTest_foo]bar")]
    [InlineData("Uqeb_RefDataTest_foo'bar")]
    [InlineData("Uqeb_RefDataTest_has space")]
    public void ValidateAndQuoteDatabaseName_RejectsUnsafeNames(string databaseName)
    {
        using var connection = new SqlConnection();
        Assert.Throws<ArgumentException>(() =>
            SqlTestDatabaseNameHelper.ValidateAndQuoteDatabaseName(connection, databaseName));
    }

    [Fact]
    public void ValidateAndQuoteDatabaseName_QuotesValidName()
    {
        using var connection = new SqlConnection();
        var quoted = SqlTestDatabaseNameHelper.ValidateAndQuoteDatabaseName(
            connection,
            "Uqeb_RefDataTest_ConcurrentDepartmentCreate_OnlyOneSucceeds_ab12cd34");

        Assert.Equal("[Uqeb_RefDataTest_ConcurrentDepartmentCreate_OnlyOneSucceeds_ab12cd34]", quoted);
    }

    [Fact]
    public void ValidateAndQuoteDatabaseName_QuotesTransactionRetryName()
    {
        using var connection = new SqlConnection();
        var quoted = SqlTestDatabaseNameHelper.ValidateAndQuoteDatabaseName(
            connection,
            "Uqeb_TransactionRetry_ab12cd34ef56");

        Assert.Equal("[Uqeb_TransactionRetry_ab12cd34ef56]", quoted);
    }
}
