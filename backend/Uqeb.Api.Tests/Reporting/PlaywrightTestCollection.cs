using Xunit;

namespace Uqeb.Api.Tests.Reporting;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PlaywrightTestCollection
{
    public const string Name = "Playwright";
}
