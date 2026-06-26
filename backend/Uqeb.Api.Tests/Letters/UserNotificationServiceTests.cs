using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class UserNotificationServiceTests
{
    [Fact]
    public async Task CreateAsync_AllowsNullBody()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateAsync_AllowsNullBody));
        await LettersTestInfrastructure.SeedUserAsync(db);
        var service = new UserNotificationService(db);

        var created = await service.CreateAsync(1, "follow-up", "عنوان", null!);

        Assert.Equal(string.Empty, created.Body);
    }
}
