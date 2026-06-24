using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Configuration;
using Uqeb.Api.Data.Provisioning;

namespace Uqeb.Api.Data;

public static class DatabaseStartupRunner
{
    public static async Task RunAsync(
        AppDbContext db,
        DatabaseStartupOptions options,
        IHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        options.Validate();

        if (!db.Database.IsRelational())
            await db.Database.EnsureCreatedAsync(cancellationToken);

        if (environment.IsProduction())
        {
            await MigrationProvisioner.ApplyAsync(db, options.RunMigrationsOnStartup, cancellationToken);
            await ReferenceDataProvisioner.ApplyAsync(db, options.RunReferenceSeedOnStartup, cancellationToken);
            await DefaultUsersProvisioner.ApplyAsync(db, options.RunDefaultUsersSeedOnStartup, cancellationToken);
            await DemoDataProvisioner.ApplyAsync(db, options.RunDemoSeedOnStartup, cancellationToken);
            return;
        }

        if (!environment.IsDevelopment())
            return;

        await MigrationProvisioner.ApplyAsync(db, options.RunMigrationsOnStartup, cancellationToken);
        await ReferenceDataProvisioner.ApplyAsync(db, options.RunReferenceSeedOnStartup, cancellationToken);
        await DefaultUsersProvisioner.ApplyAsync(db, options.RunDefaultUsersSeedOnStartup, cancellationToken);
        await DemoDataProvisioner.ApplyAsync(db, options.RunDemoSeedOnStartup, cancellationToken);
    }
}

public sealed class DatabaseProvisionRequest
{
    public bool ApplyMigrations { get; init; }
    public bool CreateReferenceData { get; init; }
    public bool CreateDefaultUsers { get; init; }
    public bool CreateDemoData { get; init; }
}

public static class DatabaseProvisionApplication
{
    public static async Task RunAsync(AppDbContext db, DatabaseProvisionRequest request, CancellationToken cancellationToken = default)
    {
        await MigrationProvisioner.ApplyAsync(db, request.ApplyMigrations, cancellationToken);
        await ReferenceDataProvisioner.ApplyAsync(db, request.CreateReferenceData, cancellationToken);
        await DefaultUsersProvisioner.ApplyAsync(db, request.CreateDefaultUsers, cancellationToken);
        await DemoDataProvisioner.ApplyAsync(db, request.CreateDemoData, cancellationToken);
    }
}
