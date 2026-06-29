namespace Uqeb.Api.Helpers;

internal static class FileStoragePathResolver
{
    // On macOS/Linux, a Windows drive path like C:\Uqeb\attachments is treated as a relative path,
    // creating a literal directory named "C:\Uqeb\attachments" in the CWD. This breaks MSBuild
    // glob expansion (MSB3552/CS2001) because FileMatcher cannot enumerate that directory name.
    internal static string Resolve(string? configuredPath, string? subDir = null)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "Attachments")
            : configuredPath;

        if (!OperatingSystem.IsWindows() && IsWindowsDrivePath(path))
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".uqeb", "attachments");

        return string.IsNullOrEmpty(subDir) ? path : Path.Combine(path, subDir);
    }

    private static bool IsWindowsDrivePath(string path) =>
        path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
}
