using Microsoft.EntityFrameworkCore;

namespace Uqeb.Api.Data.Provisioning;

public static class MigrationProvisioner
{
    public static async Task<int> ApplyAsync(AppDbContext db, bool enabled, CancellationToken cancellationToken = default)
    {
        if (!enabled)
            return 0;

        if (db.Database.IsRelational())
            await db.Database.MigrateAsync(cancellationToken);
        else
            await db.Database.EnsureCreatedAsync(cancellationToken);

        return 1;
    }
}
