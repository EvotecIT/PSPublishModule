using System.IO;
using System.Runtime.InteropServices;

namespace PowerForge;

internal static class ModuleBuildPathPolicy
{
    private static readonly string[] PathTokens =
    {
        "<TagName>",
        "{TagName}",
        "<ModuleVersion>",
        "{ModuleVersion}",
        "<ModuleVersionWithPreRelease>",
        "{ModuleVersionWithPreRelease}",
        "<TagModuleVersionWithPreRelease>",
        "{TagModuleVersionWithPreRelease}",
        "<ModuleName>",
        "{ModuleName}"
    };

    internal static string? ResolveWorkspacePath(string workspaceRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            return path;

        if (ContainsToken(path!))
            return Path.Combine(workspaceRoot, PathValueResolver.Clean(path!));

        return PathValueResolver.Resolve(workspaceRoot, path!);
    }

    internal static string[] ResolveWorkspacePaths(string workspaceRoot, string[]? paths)
    {
        if (paths is null || paths.Length == 0)
            return Array.Empty<string>();

        return paths
            .Select(path => ResolveWorkspacePath(workspaceRoot, path) ?? string.Empty)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    internal static string ResolveConfigPath(string baseDir, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return PathValueResolver.Resolve(baseDir, path!);
    }

    internal static string? ResolveConfigPathNullable(string baseDir, string? path)
        => string.IsNullOrWhiteSpace(path)
            ? null
            : ResolveConfigPath(baseDir, path);

    internal static string? ResolveTokenAwareConfigPathNullable(string baseDir, string? path)
        => string.IsNullOrWhiteSpace(path)
            ? null
            : ContainsToken(path!)
                ? NormalizeForJson(path!)
                : ResolveConfigPath(baseDir, path);

    internal static string[] ResolveConfigPaths(string rootPath, string[]? paths)
    {
        if (paths is null || paths.Length == 0)
            return Array.Empty<string>();

        return paths
            .Select(path => ResolveConfigPathNullable(rootPath, path) ?? string.Empty)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    internal static string[] ResolveRootedConfigPaths(string rootPath, string[]? paths)
    {
        if (paths is null || paths.Length == 0)
            return Array.Empty<string>();

        return paths
            .Select(path =>
            {
                if (string.IsNullOrWhiteSpace(path))
                    return string.Empty;

                var cleaned = PathValueResolver.Clean(path);
                return Path.IsPathRooted(cleaned)
                    ? ResolveConfigPath(rootPath, path)
                    : NormalizeForJson(path);
            })
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    internal static string? MakeRelativeForProjectRoot(string projectRoot, string? path)
        => MakeRelativeForProjectRoot(projectRoot, path, preserveExternalRooted: false, projectRoot);

    internal static string? MakeRelativeForProjectRoot(string projectRoot, string? path, bool preserveExternalRooted)
        => MakeRelativeForProjectRoot(projectRoot, path, preserveExternalRooted, projectRoot);

    internal static string? MakeRelativeForProjectRoot(string projectRoot, string? path, bool preserveExternalRooted, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (ContainsToken(path!))
            return MakeTokenizedPathRelativeForProjectRoot(projectRoot, path!);

        var resolved = ResolveConfigPath(projectRoot, path);
        if (preserveExternalRooted &&
            Path.IsPathRooted(PathValueResolver.Clean(path!)) &&
            !IsSameOrChildPath(projectRoot, resolved) &&
            !IsSameOrChildPath(workspaceRoot, resolved))
        {
            return NormalizeForJson(resolved);
        }

        return MakeRelativeForConfig(projectRoot, resolved);
    }

    internal static string[] MakePathsRelativeForProjectRoot(string projectRoot, string[]? paths, bool preserveExternalRooted = false)
        => MakePathsRelativeForProjectRoot(projectRoot, paths, preserveExternalRooted, projectRoot);

    internal static string[] MakePathsRelativeForProjectRoot(string projectRoot, string[]? paths, bool preserveExternalRooted, string workspaceRoot)
    {
        if (paths is null || paths.Length == 0)
            return Array.Empty<string>();

        return paths
            .Select(path => MakeRelativeForProjectRoot(projectRoot, path, preserveExternalRooted, workspaceRoot) ?? string.Empty)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    internal static string MakeRelativeForConfig(string baseDir, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        try
        {
            var full = Path.GetFullPath(path);
            var rel = GetRelativePath(baseDir, full);
            return rel.Replace('\\', '/');
        }
        catch
        {
            return path.Replace('\\', '/');
        }
    }

    internal static string? MakeRelativeForConfigNullable(string baseDir, string? path)
        => string.IsNullOrWhiteSpace(path)
            ? null
            : MakeRelativeForConfig(baseDir, path!);

    internal static string NormalizeForJson(string path)
        => path.Replace('\\', '/');

    internal static bool ContainsToken(string path)
        => IndexOfToken(path) >= 0;

    internal static bool SamePath(string left, string right)
    {
        var normalizedLeft = Path.GetFullPath(left)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRight = Path.GetFullPath(right)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedLeft, normalizedRight, PathStringComparison);
    }

    internal static bool IsSameOrChildPath(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(root, candidate, PathStringComparison))
            return true;

        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        var candidateWithSeparator = candidate + Path.DirectorySeparatorChar;
        return candidateWithSeparator.StartsWith(rootWithSeparator, PathStringComparison);
    }

    private static StringComparison PathStringComparison
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string MakeTokenizedPathRelativeForProjectRoot(string projectRoot, string path)
    {
        var cleaned = PathValueResolver.Clean(path);
        if (!Path.IsPathRooted(cleaned))
            return NormalizeForJson(cleaned);

        var tokenIndex = IndexOfToken(cleaned);
        if (tokenIndex < 0)
            return NormalizeForJson(cleaned);

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var prefix = cleaned.Substring(0, tokenIndex).TrimEnd(separators);
        if (string.IsNullOrWhiteSpace(prefix))
            return NormalizeForJson(cleaned);

        var prefixFullPath = Path.GetFullPath(prefix);
        if (!IsSameOrChildPath(projectRoot, prefixFullPath))
            return NormalizeForJson(cleaned);

        var relativePrefix = MakeRelativeForConfig(projectRoot, prefixFullPath);
        var tokenStartsNewSegment = tokenIndex == 0 || separators.Contains(cleaned[tokenIndex - 1]);
        var suffix = tokenStartsNewSegment
            ? cleaned.Substring(tokenIndex).TrimStart(separators)
            : cleaned.Substring(tokenIndex);
        return string.Equals(relativePrefix, ".", StringComparison.Ordinal)
            ? NormalizeForJson(suffix)
            : NormalizeForJson(tokenStartsNewSegment
                ? Path.Combine(relativePrefix, suffix)
                : relativePrefix + suffix);
    }

    private static int IndexOfToken(string path)
    {
        var index = -1;
        foreach (var token in PathTokens)
        {
            var tokenIndex = path.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (tokenIndex >= 0 && (index < 0 || tokenIndex < index))
                index = tokenIndex;
        }

        return index;
    }

    private static string GetRelativePath(string baseDir, string fullPath)
    {
#if NET472
        var baseFull = EnsureTrailingSeparator(Path.GetFullPath(baseDir));
        var baseUri = new Uri(baseFull);
        var pathUri = new Uri(Path.GetFullPath(fullPath));

        if (!string.Equals(baseUri.Scheme, pathUri.Scheme, StringComparison.OrdinalIgnoreCase))
            return fullPath;

        var relativeUri = baseUri.MakeRelativeUri(pathUri);
        var relative = Uri.UnescapeDataString(relativeUri.ToString());
        return relative.Replace('/', Path.DirectorySeparatorChar);
#else
        return Path.GetRelativePath(baseDir, fullPath);
#endif

#if NET472
        static string EnsureTrailingSeparator(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            if (input.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                input.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                return input;
            return input + Path.DirectorySeparatorChar;
        }
#endif
    }
}
