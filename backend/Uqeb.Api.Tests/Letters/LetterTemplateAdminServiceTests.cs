using Uqeb.Api.DTOs.LetterTemplates;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Entities;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class LetterTemplateAdminServiceTests
{
    [Fact]
    public async Task CreateAsync_PersistsTemplateWithGeneratedCode()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CreateAsync_PersistsTemplateWithGeneratedCode));
        var service = new LetterTemplateAdminService(db);

        var created = await service.CreateAsync(new CreateLetterTemplateRequest
        {
            Name = "قالب اختبار",
            Content = "محتوى {Subject}",
            TemplateType = LetterTemplateType.FollowUp,
            IsActive = true,
        }, actorUserId: 1);

        Assert.True(created.Id > 0);
        Assert.Equal("قالب اختبار", created.Name);
        Assert.Equal("محتوى {Subject}", created.Content);
        Assert.False(string.IsNullOrWhiteSpace(created.Code));
    }

    [Fact]
    public void ValidateContent_FlagsUnknownVariables()
    {
        var service = new LetterTemplateAdminService(LettersTestInfrastructure.CreateDb(nameof(ValidateContent_FlagsUnknownVariables)));

        var result = service.ValidateContent("مرحبًا {Subject} و{BadVariable}");

        Assert.False(result.IsValid);
        Assert.Contains("BadVariable", result.UnknownVariables);
    }

    [Fact]
    public async Task SetDefaultAsync_ClearsPreviousDefaultForSameType()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(SetDefaultAsync_ClearsPreviousDefaultForSameType));
        var service = new LetterTemplateAdminService(db);

        var first = await service.CreateAsync(new CreateLetterTemplateRequest
        {
            Name = "أول",
            Content = "محتوى 1",
            TemplateType = LetterTemplateType.FollowUp,
            IsDefault = true,
        }, 1);

        var second = await service.CreateAsync(new CreateLetterTemplateRequest
        {
            Name = "ثاني",
            Content = "محتوى 2",
            TemplateType = LetterTemplateType.FollowUp,
        }, 1);

        var updated = await service.SetDefaultAsync(second.Id, 1);

        Assert.NotNull(updated);
        Assert.True(updated!.IsDefault);

        var firstReloaded = await service.GetByIdAsync(first.Id);
        Assert.NotNull(firstReloaded);
        Assert.False(firstReloaded!.IsDefault);
    }

    [Fact]
    public async Task SetActiveAsync_RejectsDeactivatingDefaultTemplate()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(SetActiveAsync_RejectsDeactivatingDefaultTemplate));
        var service = new LetterTemplateAdminService(db);

        var template = await service.CreateAsync(new CreateLetterTemplateRequest
        {
            Name = "افتراضي",
            Content = "محتوى",
            TemplateType = LetterTemplateType.FollowUp,
            IsDefault = true,
        }, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetActiveAsync(template.Id, isActive: false, actorUserId: 1));
    }

    [Fact]
    public async Task CopyAsync_CreatesIndependentDuplicate()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(CopyAsync_CreatesIndependentDuplicate));
        var service = new LetterTemplateAdminService(db);

        var source = await service.CreateAsync(new CreateLetterTemplateRequest
        {
            Name = "أصل",
            Description = "وصف",
            Content = "محتوى {Subject}",
            TemplateType = LetterTemplateType.FollowUp,
        }, 1);

        var copy = await service.CopyAsync(source.Id, 1);

        Assert.NotEqual(source.Id, copy.Id);
        Assert.NotEqual(source.Code, copy.Code);
        Assert.Contains("نسخة", copy.Name);
        Assert.Equal(source.Content, copy.Content);
        Assert.False(copy.IsDefault);
    }

    [Fact]
    public async Task UpdateDefaultFollowUpTemplateAsync_UpdatesExistingDefaultContent()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(UpdateDefaultFollowUpTemplateAsync_UpdatesExistingDefaultContent));
        var service = new LetterTemplateAdminService(db);

        await service.CreateAsync(new CreateLetterTemplateRequest
        {
            Name = "افتراضي",
            Content = "قديم",
            TemplateType = LetterTemplateType.FollowUp,
            IsDefault = true,
        }, 1);

        var updated = await service.UpdateDefaultFollowUpTemplateAsync("جديد {Subject}", actorUserId: 1);

        Assert.NotNull(updated);
        Assert.Equal("جديد {Subject}", updated!.Content);
        Assert.True(updated.IsDefault);
    }

    [Fact]
    public async Task ReorderAsync_UpdatesSortOrder()
    {
        await using var db = LettersTestInfrastructure.CreateDb(nameof(ReorderAsync_UpdatesSortOrder));
        var service = new LetterTemplateAdminService(db);

        var first = await service.CreateAsync(new CreateLetterTemplateRequest
        {
            Name = "أ",
            Content = "1",
            TemplateType = LetterTemplateType.FollowUp,
        }, 1);

        var second = await service.CreateAsync(new CreateLetterTemplateRequest
        {
            Name = "ب",
            Content = "2",
            TemplateType = LetterTemplateType.FollowUp,
        }, 1);

        await service.ReorderAsync(new ReorderLetterTemplatesRequest
        {
            Items =
            [
                new LetterTemplateOrderItem { Id = first.Id, SortOrder = 20 },
                new LetterTemplateOrderItem { Id = second.Id, SortOrder = 10 },
            ],
        }, 1);

        var items = await service.ListAsync(new LetterTemplateListRequest { Type = LetterTemplateType.FollowUp });
        Assert.Equal(second.Id, items[0].Id);
        Assert.Equal(first.Id, items[1].Id);
    }
}
