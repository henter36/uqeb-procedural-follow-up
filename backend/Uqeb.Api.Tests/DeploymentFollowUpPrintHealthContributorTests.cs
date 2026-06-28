using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data;
using Uqeb.Api.HostedServices;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Uqeb.Api.Services.Health;
using Xunit;

namespace Uqeb.Api.Tests;

public class DeploymentFollowUpPrintHealthContributorTests
{
    [Fact]
    public async Task EvaluateAsync_WhenFollowUpPrintTablesAreMissing_ReturnsSchemaFailure()
    {
        await using var fixture = await FollowUpPrintHealthFixture.CreateAsync(createSchema: false, seedDefaultTemplate: false);

        var result = await fixture.Contributor.EvaluateAsync();

        Assert.False(result.IsReady);
        Assert.Contains(result.Checks, check => check.Name == "followUpPrintSchema" && check.Status == "fail");
    }

    [Fact]
    public async Task EvaluateAsync_WhenDefaultTemplateIsMissing_ReturnsTemplateFailure()
    {
        await using var fixture = await FollowUpPrintHealthFixture.CreateAsync(createSchema: true, seedDefaultTemplate: false);

        var result = await fixture.Contributor.EvaluateAsync();

        Assert.False(result.IsReady);
        Assert.Contains(result.Checks, check => check.Name == "followUpPrintSchema" && check.Status == "pass");
        Assert.Contains(result.Checks, check => check.Name == "followUpDefaultTemplate" && check.Status == "fail");
    }

    [Fact]
    public async Task EvaluateAsync_WhenSchemaTemplateOptionsAndProcessorAreReady_ReturnsPass()
    {
        await using var fixture = await FollowUpPrintHealthFixture.CreateAsync(createSchema: true, seedDefaultTemplate: true);

        var result = await fixture.Contributor.EvaluateAsync();

        Assert.True(result.IsReady);
        Assert.Contains(result.Checks, check => check.Name == "followUpPrintSchema" && check.Status == "pass");
        Assert.Contains(result.Checks, check => check.Name == "followUpDefaultTemplate" && check.Status == "pass");
        Assert.Contains(result.Checks, check => check.Name == "followUpPrintOptions" && check.Status == "pass");
        Assert.Contains(result.Checks, check => check.Name == "followUpPrintProcessor" && check.Status == "pass");
    }

    private sealed class FollowUpPrintHealthFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly SqliteConnection _connection;

        private FollowUpPrintHealthFixture(ServiceProvider provider, SqliteConnection connection)
        {
            _provider = provider;
            _connection = connection;
            Contributor = provider.GetRequiredService<IDeploymentFollowUpPrintHealthContributor>();
        }

        public IDeploymentFollowUpPrintHealthContributor Contributor { get; }

        public static async Task<FollowUpPrintHealthFixture> CreateAsync(bool createSchema, bool seedDefaultTemplate)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();
            services.Configure<FollowUpLettersOptions>(_ => { });
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
            services.AddHostedService<FollowUpPrintJobProcessorHostedService>();
            services.AddScoped<IDeploymentFollowUpPrintHealthContributor, DeploymentFollowUpPrintHealthContributor>();

            var provider = services.BuildServiceProvider();
            if (createSchema)
            {
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.EnsureCreatedAsync();

                if (seedDefaultTemplate)
                {
                    db.LetterTemplates.Add(new LetterTemplate
                    {
                        Code = LetterTemplateService.FollowUpTemplateCode,
                        Name = "خطاب تعقيب",
                        Content = LetterTemplateService.DefaultFollowUpContent,
                        TemplateType = LetterTemplateType.FollowUp,
                        IsActive = true,
                        IsDefault = true,
                    });
                    await db.SaveChangesAsync();
                }
            }

            return new FollowUpPrintHealthFixture(provider, connection);
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
