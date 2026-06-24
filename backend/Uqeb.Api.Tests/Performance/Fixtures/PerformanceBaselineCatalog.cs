namespace Uqeb.Api.Tests.Performance.Fixtures;

public static class PerformanceBaselineCatalog
{
    public const string SchemaRelativePath = "docs/performance_baseline/schema.json";
    public const string RecordsTemplateRelativePath = "docs/performance_baseline/records/baseline-records.template.json";
    public const string ArtifactsRelativeDirectory = "artifacts/performance-baseline";

    public static string ResolveRepositoryRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    public static string ResolveFromRoot(string relativePath) =>
        Path.Combine(ResolveRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
}
