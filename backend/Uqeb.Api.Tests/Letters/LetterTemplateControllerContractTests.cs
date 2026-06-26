using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Uqeb.Api.Authorization;
using Uqeb.Api.Controllers;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests.Letters;

public class LetterTemplateControllerContractTests
{
    [Fact]
    public void AdminController_KeepsLifecycleDependenciesOnly()
    {
        var constructor = typeof(LetterTemplateAdminController).GetConstructors().Single();
        var dependencies = constructor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Contains(typeof(ILetterTemplateAdminService), dependencies);
        Assert.Contains(typeof(ICurrentUserService), dependencies);
        Assert.DoesNotContain(typeof(IFollowUpLetterRenderService), dependencies);
    }

    [Theory]
    [InlineData(nameof(LetterTemplatePreviewController.Validate), "validate")]
    [InlineData(nameof(LetterTemplatePreviewController.Preview), "preview")]
    public void PreviewController_PreservesLetterTemplateRoutesAndPolicy(string actionName, string template)
    {
        Assert.Equal("api/letter-templates", GetRouteTemplate<LetterTemplatePreviewController>());

        var method = typeof(LetterTemplatePreviewController)
            .GetMethod(actionName)
            ?? throw new InvalidOperationException($"Action {actionName} was not found.");

        var post = method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false)
            .Cast<HttpPostAttribute>()
            .Single();
        var authorize = method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(template, post.Template);
        Assert.Equal(Policies.ManageLetterTemplates, authorize.Policy);
    }

    private static string GetRouteTemplate<TController>() =>
        typeof(TController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .Single()
            .Template
        ?? throw new InvalidOperationException("Controller route template is missing.");
}
