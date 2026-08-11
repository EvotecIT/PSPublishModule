namespace PowerForge;

/// <summary>Applies set-relative screenshot filter semantics consistently across validation, approval, and upload.</summary>
internal static class AppStoreConnectScreenshotFileSelector
{
    internal static string[] Select(string folder, string filter, int maxCount)
        => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = Path.GetFullPath(path),
                RelativePath = GetRelativePath(folder, path)
            })
            .Where(static item => item.RelativePath is not null)
            .Where(item => MatchesFilter(item.RelativePath!, filter))
            .OrderBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .Select(static item => item.Path)
            .ToArray();

    internal static string? GetRelativePath(string folder, string screenshotPath)
    {
        var relative = FrameworkCompatibility.GetRelativePath(folder, Path.GetFullPath(screenshotPath));
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }
        return relative.Replace('\\', '/');
    }

    internal static bool MatchesFilter(string relativePath, string filter)
    {
        var normalizedFilter = filter.Replace('\\', '/');
        var expression = "^" + System.Text.RegularExpressions.Regex.Escape(normalizedFilter)
            .Replace("\\*", "[^/]*")
            .Replace("\\?", "[^/]") + "$";
        var options = System.Text.RegularExpressions.RegexOptions.CultureInvariant;
        if (Path.DirectorySeparatorChar == '\\')
            options |= System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        return System.Text.RegularExpressions.Regex.IsMatch(relativePath, expression, options);
    }
}
