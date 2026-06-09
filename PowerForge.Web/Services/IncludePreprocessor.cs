using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

internal static class IncludePreprocessor
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex IncludeRegex = new Regex(@"\{\{<\s*include\s+path=""(?<path>[^""]+)""\s*>\}\}", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
    private static readonly StringComparison FileSystemPathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    private static readonly StringComparer FileSystemPathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public static string Apply(
        string markdown,
        string rootPath,
        string? sourcePath = null,
        string? sourceRootPath = null,
        int maxDepth = 5)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
        if (maxDepth <= 0) return markdown;

        var roots = BuildAllowedRoots(rootPath, sourcePath, sourceRootPath);
        return ApplyInternal(markdown, roots, sourcePath, maxDepth);
    }

    private static string ApplyInternal(string markdown, IReadOnlyList<string> roots, string? sourcePath, int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
        if (maxDepth <= 0) return markdown;

        var state = new IncludeState(roots, sourcePath, maxDepth);
        return IncludeRegex.Replace(markdown, match =>
        {
            var current = state;
            var path = match.Groups["path"].Value;
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var fullPath = ResolvePath(current.Roots, current.SourcePath, path);
            if (fullPath is null || !File.Exists(fullPath))
                return string.Empty;
            var content = File.ReadAllText(fullPath);
            return ApplyInternal(content, current.Roots, fullPath, current.MaxDepth - 1);
        });
    }

    private static string? ResolvePath(IReadOnlyList<string> roots, string? sourcePath, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (Path.IsPathRooted(path))
        {
            var rooted = Path.GetFullPath(path);
            return IsWithinAnyRoot(roots, rooted) ? rooted : null;
        }

        var sourceDir = string.IsNullOrWhiteSpace(sourcePath) ? null : Path.GetDirectoryName(Path.GetFullPath(sourcePath));
        if (!string.IsNullOrWhiteSpace(sourceDir))
        {
            var sourceCandidate = Path.GetFullPath(Path.Combine(sourceDir, path));
            if (IsWithinAnyRoot(roots, sourceCandidate))
                return sourceCandidate;
        }

        foreach (var root in roots)
        {
            var candidate = Path.GetFullPath(Path.Combine(root, path));
            if (IsWithinAnyRoot(roots, candidate))
                return candidate;
        }

        return null;
    }

    private static IReadOnlyList<string> BuildAllowedRoots(string rootPath, string? sourcePath, string? sourceRootPath)
    {
        var roots = new List<string>();
        var siteRoot = NormalizeRoot(rootPath);
        if (!string.IsNullOrWhiteSpace(siteRoot))
            roots.Add(siteRoot);

        if (!string.IsNullOrWhiteSpace(sourceRootPath))
        {
            var sourceRoot = NormalizeRoot(sourceRootPath);
            if (!string.IsNullOrWhiteSpace(sourceRoot) &&
                !roots.Contains(sourceRoot, FileSystemPathComparer))
            {
                roots.Add(sourceRoot);
            }
        }

        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            var sourceDir = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            if (!string.IsNullOrWhiteSpace(sourceDir))
            {
                var normalized = NormalizeRoot(sourceDir);
                if (!string.IsNullOrWhiteSpace(normalized) &&
                    !roots.Contains(normalized, FileSystemPathComparer))
                {
                    roots.Add(normalized);
                }
            }
        }

        return roots;
    }

    private static bool IsWithinAnyRoot(IReadOnlyList<string> roots, string path)
    {
        if (roots is null || roots.Count == 0)
            return false;

        var fullPath = Path.GetFullPath(path);
        foreach (var root in roots)
        {
            if (fullPath.StartsWith(root, FileSystemPathComparison))
                return true;
        }

        return false;
    }

    private static string NormalizeRoot(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private readonly record struct IncludeState(IReadOnlyList<string> Roots, string? SourcePath, int MaxDepth);
}
