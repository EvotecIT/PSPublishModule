using System.Text.RegularExpressions;

namespace PowerForge.Web;

internal static class IncludePreprocessor
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex IncludeRegex = new Regex(@"\{\{<\s*include\s+path=""(?<path>[^""]+)""\s*>\}\}", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    public static string Apply(string markdown, string rootPath, int maxDepth = 5)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
        if (maxDepth <= 0) return markdown;

        return IncludeRegex.Replace(markdown, match =>
        {
            var path = match.Groups["path"].Value;
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var fullPath = ResolvePath(rootPath, path);
            if (fullPath is null || !File.Exists(fullPath))
                return string.Empty;
            var content = File.ReadAllText(fullPath);
            return Apply(content, rootPath, maxDepth - 1);
        });
    }

    private static string? ResolvePath(string rootPath, string path)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(rootPath, path));
    }
}
