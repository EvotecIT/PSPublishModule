using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed class WebStaticOverlayOptions
{
    public string SourceRoot { get; set; } = string.Empty;
    public string DestinationRoot { get; set; } = string.Empty;
    public string[] Include { get; set; } = Array.Empty<string>();
    public string[] Exclude { get; set; } = Array.Empty<string>();
}

public static class WebStaticOverlay
{
    public static WebStaticOverlayResult Apply(WebStaticOverlayOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.SourceRoot))
            throw new ArgumentException("SourceRoot is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.DestinationRoot))
            throw new ArgumentException("DestinationRoot is required.", nameof(options));

        var sourceRoot = Path.GetFullPath(options.SourceRoot);
        var destinationRoot = Path.GetFullPath(options.DestinationRoot);
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Source root not found: {sourceRoot}");
        Directory.CreateDirectory(destinationRoot);

        var includes = NormalizePatterns(options.Include);
        var excludes = NormalizePatterns(options.Exclude);
        var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories);
        var copied = 0;

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
            if (excludes.Length > 0 && MatchesAny(excludes, relative))
                continue;
            if (includes.Length > 0 && !MatchesAny(includes, relative))
                continue;

            var target = Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            var targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(targetDir))
                Directory.CreateDirectory(targetDir);
            File.Copy(file, target, overwrite: true);
            copied++;
        }

        return new WebStaticOverlayResult { CopiedCount = copied };
    }

    private static string[] NormalizePatterns(string[] patterns)
    {
        return patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Replace('\\', '/').Trim())
            .ToArray();
    }

    private static bool MatchesAny(string[] patterns, string value)
    {
        foreach (var pattern in patterns)
        {
            if (GlobMatch(pattern, value))
                return true;
        }
        return false;
    }

    private static bool GlobMatch(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
    }
}
