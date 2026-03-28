using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ImageMagick;

namespace PowerForge.Web;

internal static class WebImageDimensions
{
    private static readonly Regex FileNameDimensionRegex = new(
        "(?:^|[-_])(?<width>\\d{1,5})x(?<height>\\d{1,5})(?=\\.[^.]+$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly ConcurrentDictionary<string, ImageDimensionHint> ImageDimensionCache = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryResolve(string? src, string? sourcePath, string? siteRoot, out ImageDimensionHint dimensions)
    {
        dimensions = default;
        if (string.IsNullOrWhiteSpace(src))
            return false;

        var normalized = StripQueryAndFragment(src.Trim());
        if (string.IsNullOrWhiteSpace(normalized) || IsExternalImageReference(normalized) || IsSvgImageSource(normalized))
            return false;

        if (TryExtractFileNameDimensions(normalized, out dimensions))
            return true;

        if (!TryResolveLocalImagePath(normalized, sourcePath, siteRoot, out var localPath))
            return false;

        if (ImageDimensionCache.TryGetValue(localPath, out dimensions))
            return dimensions.Width > 0 && dimensions.Height > 0;

        try
        {
            var info = new MagickImageInfo(localPath);
            dimensions = new ImageDimensionHint((int)info.Width, (int)info.Height);
        }
        catch
        {
            dimensions = default;
        }

        ImageDimensionCache[localPath] = dimensions;
        return dimensions.Width > 0 && dimensions.Height > 0;
    }

    private static bool TryResolveLocalImagePath(string src, string? sourcePath, string? siteRoot, out string localPath)
    {
        localPath = string.Empty;
        var relative = src.Replace('/', Path.DirectorySeparatorChar);

        if (!string.IsNullOrWhiteSpace(sourcePath) &&
            !Path.IsPathRooted(src) &&
            !src.StartsWith("/", StringComparison.Ordinal))
        {
            var sourceDirectory = Path.GetDirectoryName(sourcePath);
            if (!string.IsNullOrWhiteSpace(sourceDirectory))
            {
                var sourceCandidate = Path.Combine(sourceDirectory, relative);
                if (File.Exists(sourceCandidate))
                {
                    localPath = sourceCandidate;
                    return true;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(siteRoot))
            return false;

        var rootedRelative = src.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        var candidates = new[]
        {
            Path.Combine(siteRoot, rootedRelative),
            Path.Combine(siteRoot, "static", rootedRelative)
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
                continue;

            localPath = candidate;
            return true;
        }

        return false;
    }

    private static bool TryExtractFileNameDimensions(string src, out ImageDimensionHint dimensions)
    {
        dimensions = default;
        if (string.IsNullOrWhiteSpace(src))
            return false;

        var fileName = Path.GetFileName(src.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var match = FileNameDimensionRegex.Match(fileName);
        if (!match.Success ||
            !int.TryParse(match.Groups["width"].Value, out var width) ||
            !int.TryParse(match.Groups["height"].Value, out var height) ||
            width <= 0 ||
            height <= 0)
        {
            return false;
        }

        dimensions = new ImageDimensionHint(width, height);
        return true;
    }

    private static string StripQueryAndFragment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        var queryIndex = trimmed.IndexOf('?');
        if (queryIndex >= 0)
            trimmed = trimmed[..queryIndex];
        var hashIndex = trimmed.IndexOf('#');
        if (hashIndex >= 0)
            trimmed = trimmed[..hashIndex];
        return trimmed;
    }

    private static bool IsExternalImageReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("//", StringComparison.Ordinal) ||
               value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("#", StringComparison.Ordinal);
    }

    private static bool IsSvgImageSource(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".svgz", StringComparison.OrdinalIgnoreCase);
    }
}

internal readonly record struct ImageDimensionHint(int Width, int Height);
