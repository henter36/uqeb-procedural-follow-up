namespace Uqeb.Api.Configuration;

public sealed class BuildInfoOptions
{
    public const string SectionName = "BuildInfo";

    public string? Version { get; set; }

    public string? CommitSha { get; set; }

    public string? BuildTimeUtc { get; set; }
}
