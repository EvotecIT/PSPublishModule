using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class RepositoryArchitectureService
{
    private static string[] NormalizeChangedFiles(
        string repositoryRoot,
        IEnumerable<string>? changedFiles,
        ICollection<RepositoryArchitectureIssue> issues)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in changedFiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            try
            {
                var platformPath = ToPlatformPath(path);
                var candidate = Path.IsPathRooted(platformPath)
                    ? Path.GetFullPath(platformPath)
                    : Path.GetFullPath(Path.Combine(repositoryRoot, platformPath));
                EnsureInsideRoot(repositoryRoot, candidate, path);
                normalized.Add(ToRelativePath(repositoryRoot, candidate));
            }
            catch (Exception ex)
            {
                AddError(issues, "ARC020", ex.Message, path);
            }
        }

        return normalized.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ResolveRepositoryPath(string repositoryRoot, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, ToPlatformPath(relativePath)));
        EnsureInsideRoot(repositoryRoot, fullPath, relativePath);
        return fullPath;
    }

    private static void EnsureInsideRoot(string repositoryRoot, string fullPath, string suppliedPath)
    {
        var rootWithSeparator = EnsureTrailingSeparator(Path.GetFullPath(repositoryRoot));
        var candidate = Path.GetFullPath(fullPath);
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(rootWithSeparator, comparison)
            && !string.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                repositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                comparison))
        {
            throw new InvalidOperationException($"Architecture path escapes the repository root: {suppliedPath}");
        }
    }

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
           || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static string ToRelativePath(string root, string path)
    {
        var rootUri = new Uri(EnsureTrailingSeparator(Path.GetFullPath(root)));
        var pathUri = new Uri(Path.GetFullPath(path));
        var relative = Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString());
        return NormalizePath(relative);
    }

    private static string NormalizePath(string? path)
    {
        var normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized.Substring(2);
        return normalized.TrimStart('/');
    }

    private static string ToPlatformPath(string? path)
        => (path ?? string.Empty)
            .Trim()
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

    private static string[] NormalizeDistinct(IEnumerable<string>? paths)
        => (paths ?? Array.Empty<string>())
            .Select(NormalizePath)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsInfrastructurePath(string relativePath)
    {
        var path = "/" + NormalizePath(relativePath) + "/";
        return path.IndexOf("/.git/", StringComparison.OrdinalIgnoreCase) >= 0
               || path.IndexOf("/.powerforge/", StringComparison.OrdinalIgnoreCase) >= 0
               || path.IndexOf("/bin/", StringComparison.OrdinalIgnoreCase) >= 0
               || path.IndexOf("/obj/", StringComparison.OrdinalIgnoreCase) >= 0
               || path.IndexOf("/node_modules/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static IEnumerable<string> EnumerateRepositoryFiles(string repositoryRoot)
    {
        var pending = new Stack<string>();
        pending.Push(repositoryRoot);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(directory);
                directories = Directory.GetDirectories(directory);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var file in files)
                yield return file;
            foreach (var child in directories)
            {
                var name = Path.GetFileName(child);
                if (name.Equals(".git", StringComparison.OrdinalIgnoreCase)
                    || name.Equals(".powerforge", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
                    continue;
                pending.Push(child);
            }
        }
    }

    private static bool IsPathInside(string relativePath, string directoryPath)
    {
        var path = NormalizePath(relativePath);
        var directory = NormalizePath(directoryPath).TrimEnd('/');
        if (directory.Length == 0 || directory == ".")
            return true;
        return string.Equals(path, directory, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesAny(string path, IEnumerable<string>? patterns)
    {
        var candidates = patterns ?? Array.Empty<string>();
        return candidates.Any(pattern => GlobMatches(path, pattern));
    }

    private static bool GlobMatches(string path, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var normalizedPath = NormalizePath(path);
        var normalizedPattern = NormalizePath(pattern);
        var expression = "^" + Regex.Escape(normalizedPattern)
            .Replace(@"\*\*/", "(?:.*/)?")
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]") + "$";
        return Regex.IsMatch(normalizedPath, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static IEnumerable<string> EnumerateMatchingPaths(string repositoryRoot, string pattern)
        => EnumerateRepositoryFiles(repositoryRoot)
            .Select(path => ToRelativePath(repositoryRoot, path))
            .Where(path => GlobMatches(path, pattern));

    private static void AddError(
        ICollection<RepositoryArchitectureIssue> issues,
        string code,
        string message,
        string? path = null,
        string? capabilityId = null,
        string? projectId = null)
        => issues.Add(new RepositoryArchitectureIssue
        {
            Severity = RepositoryArchitectureIssueSeverity.Error,
            Code = code,
            Message = message,
            Path = string.IsNullOrWhiteSpace(path) ? null : NormalizePath(path),
            CapabilityId = capabilityId,
            ProjectId = projectId
        });
}
