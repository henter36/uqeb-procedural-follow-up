using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Uqeb.Api.Configuration;

namespace Uqeb.Api.Services;

public interface IOrganizationBrandLogoProvider
{
    byte[]? TryGetLogoBytes(string? logoPath = null);

    // Loads exactly the named file from Assets/Brand/ with no fallback to the configured LogoPath.
    byte[]? TryGetExactLogoBytes(string fileName);
}

public sealed class OrganizationBrandLogoProvider : IOrganizationBrandLogoProvider
{
    private readonly string _brandRoot;
    private readonly OrganizationBrandingOptions _branding;
    private readonly ILogger<OrganizationBrandLogoProvider> _logger;
    private readonly ConcurrentDictionary<LogoCacheKey, byte[]> _cache = new();

    public OrganizationBrandLogoProvider(
        IWebHostEnvironment environment,
        IOptions<OrganizationBrandingOptions> branding,
        ILogger<OrganizationBrandLogoProvider> logger)
    {
        _brandRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "Assets", "Brand"));
        _branding = branding.Value;
        _logger = logger;
    }

    public byte[]? TryGetLogoBytes(string? logoPath = null)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(logoPath))
            candidates.Add(logoPath);
        if (!string.IsNullOrWhiteSpace(_branding.LogoPath))
            candidates.Add(_branding.LogoPath);

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var resolvedPath = TryResolveBrandPath(candidate);
            if (resolvedPath == null)
                continue;

            var bytes = TryLoadCached(resolvedPath);
            if (bytes != null)
                return bytes;
        }

        if (candidates.Count > 0)
            _logger.LogWarning("لم يتم العثور على شعار المؤسسة في المسارات المحددة ضمن Assets/Brand.");

        return null;
    }

    public byte[]? TryGetExactLogoBytes(string fileName)
    {
        var resolvedPath = TryResolveBrandPath(fileName);
        return resolvedPath is null ? null : TryLoadCached(resolvedPath);
    }

    private string? TryResolveBrandPath(string candidate)
    {
        // Data URIs are not file paths; skip silently without logging a warning.
        if (candidate.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        var normalized = candidate
            .Trim()
            .Replace('\\', '/')
            .TrimStart('/');

        const string prefix = "Assets/Brand/";
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[prefix.Length..];

        if (normalized.Contains('/', StringComparison.Ordinal) &&
            normalized.Split('/').Any(segment => segment is "." or ".."))
        {
            _logger.LogWarning("تم تجاهل مسار الشعار لمحاولة تجاوز المجلد المسموح: {LogoPath}", candidate);
            return null;
        }

        if (normalized is "." or ".." || normalized.Contains("..", StringComparison.Ordinal))
        {
            _logger.LogWarning("تم تجاهل مسار الشعار لمحاولة تجاوز المجلد المسموح: {LogoPath}", candidate);
            return null;
        }

        if (Path.IsPathRooted(candidate.Replace('/', Path.DirectorySeparatorChar)))
        {
            _logger.LogWarning("تم تجاهل مسار الشعار المطلق لأنه خارج Assets/Brand: {LogoPath}", candidate);
            return null;
        }

        var combined = Path.GetFullPath(Path.Combine(_brandRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInsideBrandRoot(combined))
        {
            _logger.LogWarning("تم تجاهل مسار الشعار لمحاولة تجاوز المجلد المسموح: {LogoPath}", candidate);
            return null;
        }

        return combined;
    }

    private bool IsInsideBrandRoot(string candidate)
    {
        var relative = Path.GetRelativePath(_brandRoot, candidate);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return !Path.IsPathRooted(relative)
            && !string.Equals(relative, "..", comparison)
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", comparison)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", comparison);
    }

    private byte[]? TryLoadCached(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("ملف الشعار غير موجود: {LogoPath}", fullPath);
            return null;
        }

        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "تعذر قراءة معلومات ملف الشعار: {LogoPath}", fullPath);
            return null;
        }

        var cacheKey = new LogoCacheKey(fullPath, fileInfo.LastWriteTimeUtc, fileInfo.Length);
        if (_cache.TryGetValue(cacheKey, out var cached))
            return (byte[])cached.Clone();

        try
        {
            var bytes = File.ReadAllBytes(fullPath);
            _cache[cacheKey] = (byte[])bytes.Clone();
            return bytes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "تعذر تحميل شعار المؤسسة من {LogoPath}", fullPath);
            return null;
        }
    }

    private readonly record struct LogoCacheKey(string FullPath, DateTime LastWriteTimeUtc, long Length);
}
