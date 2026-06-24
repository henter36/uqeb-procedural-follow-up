using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Reporting.Enums;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class ReportExportTemplateFkSqlServerIntegrationTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("UQEB_REFERENCE_DATA_TEST_CONNECTION");

    private static bool IsSqlServerAvailable()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            return false;

        try
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task SaveTemplate_RejectsMissingUser_OnSqlServer()
    {
        if (!IsSqlServerAvailable())
            return;

        var databaseName = $"Uqeb_TemplateFk_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(ConnectionString!) { InitialCatalog = databaseName };
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options);

        await db.Database.EnsureCreatedAsync();

        var template = new ReportExportTemplate
        {
            Name = "قالب FK",
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIdsJson = "[]",
            DefaultFiltersJson = "{}",
            DefaultFormat = ExportFormat.Pdf,
            PageNumberingMode = PageNumberingMode.Restart,
            CreatedById = 9_999_999,
            CreatedAt = DateTime.UtcNow,
        };

        db.ReportExportTemplates.Add(template);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveTemplate_SucceedsWithExistingUser_AndDeleteUserDoesNotCascade()
    {
        if (!IsSqlServerAvailable())
            return;

        var databaseName = $"Uqeb_TemplateFk_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(ConnectionString!) { InitialCatalog = databaseName };
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options);

        await db.Database.EnsureCreatedAsync();

        var user = new User
        {
            Username = $"tpl-user-{Guid.NewGuid():N}",
            PasswordHash = "x",
            FullName = "Template Owner",
            Role = UserRole.Admin,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.ReportExportTemplates.Add(new ReportExportTemplate
        {
            Name = "قالب صالح",
            ReportType = InstitutionalReportType.ExecutiveComprehensive,
            SectionIdsJson = "[]",
            DefaultFiltersJson = "{}",
            DefaultFormat = ExportFormat.Pdf,
            PageNumberingMode = PageNumberingMode.Restart,
            CreatedById = user.Id,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        db.Users.Remove(user);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
