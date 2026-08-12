namespace PowerForge;

/// <summary>Applies set-relative screenshot filter semantics consistently across validation, approval, and upload.</summary>
internal static class AppStoreConnectScreenshotFileSelector
{
    internal static string[] Select(string folder, string filter, int maxCount)
    {
        var comparison = FrameworkCompatibility.GetPathStringComparisonForPath(folder);
        var comparer = comparison == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = Path.GetFullPath(path),
                RelativePath = GetRelativePath(folder, path)
            })
            .Where(static item => item.RelativePath is not null)
            .Where(item => MatchesFilter(item.RelativePath!, filter, comparison))
            .OrderBy(static item => item.Path, comparer)
            .Take(maxCount)
            .Select(static item => item.Path)
            .ToArray();
    }

    internal static string? GetRelativePath(string folder, string screenshotPath)
    {
        var root = Path.GetFullPath(folder)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var volumeRoot = Path.GetPathRoot(root);
        if (!string.Equals(root, volumeRoot, StringComparison.Ordinal))
            root = root.TrimEnd(Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(screenshotPath)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var comparison = FrameworkCompatibility.GetPathStringComparisonForPath(root);
        var prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, comparison))
            return null;
        var relative = fullPath.Substring(prefix.Length);
        return relative.Replace('\\', '/');
    }

    internal static bool MatchesFilter(string relativePath, string filter)
        => MatchesFilter(relativePath, filter, FrameworkCompatibility.PathStringComparison());

    internal static bool MatchesFilter(string relativePath, string filter, StringComparison comparison)
    {
        var normalizedFilter = filter.Replace('\\', '/');
        var expression = "^" + System.Text.RegularExpressions.Regex.Escape(normalizedFilter)
            .Replace("\\*", "[^/]*")
            .Replace("\\?", "[^/]") + "$";
        var options = System.Text.RegularExpressions.RegexOptions.CultureInvariant;
        if (comparison == StringComparison.OrdinalIgnoreCase)
            options |= System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        return System.Text.RegularExpressions.Regex.IsMatch(relativePath, expression, options);
    }
}
