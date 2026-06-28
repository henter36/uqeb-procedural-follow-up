using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.HostedServices;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;

namespace Uqeb.Api.Services.Health;

public interface IDeploymentFollowUpPrintHealthContributor
{
    Task<DeploymentReportingHealthResult> EvaluateAsync(CancellationToken cancellationToken = default);
}

public sealed class DeploymentFollowUpPrintHealthContributor : IDeploymentFollowUpPrintHealthContributor
{
    private const string StatusPass = "pass";
    private const string StatusFail = "fail";

    private static readonly string[] RequiredTables =
    [
        "FollowUpPrintJobs",
        "FollowUpPrintJobParts",
        "FollowUpLetterPrintRecords",
        "FollowUpPrintIdempotencyKeys",
    ];

    private readonly AppDbContext _db;
    private readonly IOptions<FollowUpLettersOptions> _options;
    private readonly IEnumerable<IHostedService> _hostedServices;

    public DeploymentFollowUpPrintHealthContributor(
        AppDbContext db,
        IOptions<FollowUpLettersOptions> options,
        IEnumerable<IHostedService> hostedServices)
    {
        _db = db;
        _options = options;
        _hostedServices = hostedServices;
    }

    public async Task<DeploymentReportingHealthResult> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var schemaReady = await HasRequiredSchemaAsync(cancellationToken);
        var optionsReady = ValidateOptions();
        var processorReady = _hostedServices.Any(service => service is FollowUpPrintJobProcessorHostedService);
        var defaultTemplateReady = schemaReady && await HasDefaultTemplateAsync(cancellationToken);

        var checks = new List<DeploymentReportingHealthCheck>
        {
            new(
                "followUpPrintSchema",
                schemaReady ? StatusPass : StatusFail,
                schemaReady ? null : "Required FollowUp Print tables are missing."),
            new(
                "followUpDefaultTemplate",
                defaultTemplateReady ? StatusPass : StatusFail,
                defaultTemplateReady ? null : "Active default follow-up letter template is missing."),
            new(
                "followUpPrintOptions",
                optionsReady ? StatusPass : StatusFail,
                optionsReady ? null : "FollowUpLetters options are invalid."),
            new(
                "followUpPrintProcessor",
                processorReady ? StatusPass : StatusFail,
                processorReady ? null : "FollowUpPrintJobProcessorHostedService is not registered."),
        };

        return new DeploymentReportingHealthResult(
            FeatureEnabled: true,
            IsReady: checks.All(check => check.Status == StatusPass),
            Checks: checks);
    }

    private bool ValidateOptions()
    {
        try
        {
            _options.Value.Validate();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<bool> HasDefaultTemplateAsync(CancellationToken cancellationToken)
    {
        return await _db.LetterTemplates.AsNoTracking().AnyAsync(
            template =>
                template.Code == LetterTemplateService.FollowUpTemplateCode &&
                template.IsActive &&
                template.IsDefault &&
                template.TemplateType == LetterTemplateType.FollowUp,
            cancellationToken);
    }

    private async Task<bool> HasRequiredSchemaAsync(CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
        {
            return true;
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                return await CountExistingTablesAsync(
                    connection,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN (" + BuildSqlStringList(RequiredTables) + ")",
                    cancellationToken) == RequiredTables.Length;
            }

            return await CountExistingTablesAsync(
                connection,
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN (" + BuildSqlStringList(RequiredTables) + ")",
                cancellationToken) == RequiredTables.Length;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<int> CountExistingTablesAsync(
        DbConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private static string BuildSqlStringList(IEnumerable<string> values) =>
        string.Join(", ", values.Select(value => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'"));
}
