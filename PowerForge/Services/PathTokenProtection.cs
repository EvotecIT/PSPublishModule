using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Preserves PowerForge path tokens while paths are normalized by framework APIs
/// that reject angle brackets on .NET Framework.
/// </summary>
internal static class PathTokenProtection
{
    internal static string GetFullPath(string basePath, string path)
    {
        var protectedPath = Protect(path, out var placeholders);
        var fullPath = Path.GetFullPath(Path.IsPathRooted(protectedPath)
            ? protectedPath
            : Path.Combine(basePath, protectedPath));
        return Restore(fullPath, placeholders);
    }

    internal static string GetRelativePath(string relativeTo, string path)
    {
        var protectedPath = Protect(path, out var placeholders);
        var relativePath = FrameworkCompatibility.GetRelativePath(relativeTo, protectedPath);
        return Restore(relativePath, placeholders);
    }

    internal static bool IsPathRooted(string path)
        => Path.IsPathRooted(Protect(path, out _));

    internal static string Combine(string first, string second)
    {
        var protectedSecond = Protect(second, out var placeholders);
        return Restore(Path.Combine(first, protectedSecond), placeholders);
    }

    private static string Protect(string path, out Dictionary<string, string> placeholders)
    {
        var tokenMatches = Regex.Matches(path, @"<[^<>]+>|\{[^{}]+\}")
            .Cast<Match>()
            .Select(static match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var protectedPath = path;
        placeholders = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < tokenMatches.Length; index++)
        {
            var placeholder = $"__POWERFORGE_PATH_TOKEN_{index}__";
            placeholders[placeholder] = tokenMatches[index];
            protectedPath = protectedPath.Replace(tokenMatches[index], placeholder);
        }

        return protectedPath;
    }

    private static string Restore(string path, IReadOnlyDictionary<string, string> placeholders)
    {
        foreach (var entry in placeholders)
            path = path.Replace(entry.Key, entry.Value);

        return path;
    }
}
