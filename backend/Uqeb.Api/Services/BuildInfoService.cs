using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;
using Uqeb.Api.DTOs.System;

namespace Uqeb.Api.Services;

public interface IBuildInfoService
{
    SystemVersionDto GetVersion();
}

public sealed partial class BuildInfoService(
    IOptions<BuildInfoOptions> options,
    IHostEnvironment environment) : IBuildInfoService
{
    private const string LocalValue = "local";

    public SystemVersionDto GetVersion()
    {
        var value = options.Value;

        return new SystemVersionDto(
            BackendVersion: NormalizeVersion(value.Version) ?? GetAssemblyVersion(),
            BackendCommitSha: NormalizeCommitSha(value.CommitSha),
            BackendBuildTimeUtc: ParseBuildTime(value.BuildTimeUtc),
            Environment: NormalizeEnvironment(environment.EnvironmentName));
    }

    private static string GetAssemblyVersion()
    {
        var assembly = typeof(BuildInfoService).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var normalized = NormalizeVersion(informationalVersion?.Split('+', 2)[0]);
        return normalized ?? assembly.GetName().Version?.ToString() ?? LocalValue;
    }

    private static string? NormalizeVersion(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        return SafeVersionRegex().IsMatch(trimmed) ? trimmed : null;
    }

    private static string NormalizeCommitSha(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Equals(LocalValue, StringComparison.OrdinalIgnoreCase))
            return LocalValue;

        return CommitShaRegex().IsMatch(trimmed)
            ? trimmed[..Math.Min(trimmed.Length, 7)].ToLowerInvariant()
            : LocalValue;
    }

    private static DateTimeOffset? ParseBuildTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTimeOffset.TryParse(
            value.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string NormalizeEnvironment(string? value) =>
        value switch
        {
            "Development" => "Development",
            "Staging" => "Staging",
            "Production" => "Production",
            _ => "Unknown"
        };

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}$")]
    private static partial Regex SafeVersionRegex();

    [GeneratedRegex("^[a-fA-F0-9]{7,40}$")]
    private static partial Regex CommitShaRegex();
}
