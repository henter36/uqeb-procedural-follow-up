using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data.Provisioning;

namespace Uqeb.Api.Data;

/// <summary>
/// Legacy entry point retained for tests and tooling that still call DbSeeder.SeedAsync.
/// Production startup no longer invokes this path unconditionally.
/// </summary>
public static class DbSeeder
{
    public static Task SeedAsync(AppDbContext db) =>
        DatabaseProvisionApplication.RunAsync(
            db,
            new DatabaseProvisionRequest
            {
                ApplyMigrations = true,
                CreateReferenceData = true,
                CreateDefaultUsers = true,
                CreateDemoData = true,
            });
}
