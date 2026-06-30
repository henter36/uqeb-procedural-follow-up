namespace Uqeb.Api.DTOs.System;

public sealed record SystemVersionDto(
    string BackendVersion,
    string BackendCommitSha,
    DateTimeOffset? BackendBuildTimeUtc,
    string Environment);
