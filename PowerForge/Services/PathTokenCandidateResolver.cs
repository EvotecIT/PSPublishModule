using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Resolves existing filesystem paths from configured paths that may contain
/// PowerForge angle-bracket or brace tokens, including tokens embedded in names.
/// </summary>
public static class PathTokenCandidateResolver
{
    private static readonly Regex TokenRegex = new(
        @"<[^<>]+>|\{[^{}]+\}",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Expands token-bearing path components by matching existing filesystem entries.
    /// </summary>
    public static IReadOnlyList<string> ResolveExistingPaths(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return Array.Empty<string>();

        var fullPath = PathTokenProtection.GetFullPath(Directory.GetCurrentDirectory(), configuredPath);
        if (!TokenRegex.IsMatch(fullPath))
            return File.Exists(fullPath) || Directory.Exists(fullPath) ? new[] { fullPath } : Array.Empty<string>();

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
            return Array.Empty<string>();

        var components = fullPath.Substring(root!.Length)
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        var candidates = new List<string> { root };
        for (var index = 0; index < components.Length; index++)
        {
            var component = components[index];
            var isLast = index == components.Length - 1;
            var next = new List<string>();
            foreach (var parent in candidates.Where(Directory.Exists))
            {
                if (TokenRegex.IsMatch(component))
                {
                    var namePattern = BuildNamePattern(component);
                    foreach (var entry in EnumerateFileSystemEntriesSafe(parent))
                    {
                        if (namePattern.IsMatch(Path.GetFileName(entry)) &&
                            (isLast || Directory.Exists(entry)))
                        {
                            next.Add(entry);
                        }
                    }
                }
                else
                {
                    var entry = Path.Combine(parent, component);
                    if (isLast ? File.Exists(entry) || Directory.Exists(entry) : Directory.Exists(entry))
                        next.Add(entry);
                }
            }

            candidates = next;
            if (candidates.Count == 0)
                break;
        }

        return candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Returns the most recent write timestamp within a file or directory tree.
    /// </summary>
    public static DateTime GetLatestWriteTimeUtc(string path)
    {
        try
        {
            if (File.Exists(path))
                return File.GetLastWriteTimeUtc(path);
            if (!Directory.Exists(path))
                return DateTime.MinValue;

            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(Directory.GetLastWriteTimeUtc(path))
                .Max();
        }
        catch (IOException)
        {
            return DateTime.MinValue;
        }
        catch (UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private static Regex BuildNamePattern(string component)
    {
        var pattern = new StringBuilder("^");
        var offset = 0;
        foreach (Match match in TokenRegex.Matches(component))
        {
            pattern.Append(Regex.Escape(component.Substring(offset, match.Index - offset)));
            pattern.Append(".+");
            offset = match.Index + match.Length;
        }

        pattern.Append(Regex.Escape(component.Substring(offset)));
        pattern.Append('$');
        return new Regex(pattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static IEnumerable<string> EnumerateFileSystemEntriesSafe(string directory)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(directory).ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
