using Uqeb.Api.Services;
using Uqeb.Api.HostedServices;
using Uqeb.Api.Helpers;

namespace Uqeb.Api.Configuration;

public static class FollowUpLetterServiceCollectionExtensions
{
    public static IServiceCollection AddFollowUpLetterOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<FollowUpLettersOptions>()
            .Bind(configuration.GetSection(FollowUpLettersOptions.SectionName))
            .Validate(o =>
            {
                o.Validate();
                return true;
            }, "FollowUpLetters configuration is invalid.")
            .ValidateOnStart();
        services.Configure<OrganizationBrandingOptions>(
            configuration.GetSection(OrganizationBrandingOptions.SectionName));

        return services;
    }

    public static IServiceCollection AddFollowUpLetterServices(this IServiceCollection services)
    {
        services.AddScoped<ILetterTemplateService, LetterTemplateService>();
        services.AddScoped<ILetterTemplateAdminService, LetterTemplateAdminService>();
        services.AddSingleton<IOrganizationBrandLogoProvider, OrganizationBrandLogoProvider>();
        services.AddScoped<IFollowUpLetterTimeZone, FollowUpLetterTimeZone>();
        services.AddScoped<IFollowUpLetterDocumentBuilder, FollowUpLetterDocumentBuilder>();
        services.AddScoped<IFollowUpLetterRenderService, FollowUpLetterRenderService>();
        services.AddScoped<IFollowUpPrintEligibilityService, FollowUpPrintEligibilityService>();
        services.AddScoped<IFollowUpPrintAccessService, FollowUpPrintAccessService>();
        services.AddScoped<IFollowUpPrintJobService, FollowUpPrintJobService>();
        services.AddScoped<IFollowUpLetterPrintRecordService, FollowUpLetterPrintRecordService>();
        services.AddSingleton<IFollowUpLetterPdfExporter, FollowUpLetterPdfExporter>();
        services.AddHostedService<FollowUpPrintJobProcessorHostedService>();

        return services;
    }
}
