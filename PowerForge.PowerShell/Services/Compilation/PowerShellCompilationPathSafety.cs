using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PowerForge;

internal static class PowerShellCompilationPathSafety
{
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly bool IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    private static readonly ConcurrentDictionary<string, bool> MacCaseSensitivityByDirectory = new(StringComparer.Ordinal);

    internal static FileSystemPathComparer PathComparer { get; } = new();

    internal static void EnsureContained(string root, string path, string error)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!PathStartsWith(Path.GetFullPath(path), normalizedRoot))
            throw new InvalidOperationException(error);
    }

    internal static bool PathEquals(string? first, string? second)
    {
        if (first is null || second is null)
            return first is null && second is null;
        return string.Equals(first, second, GetPathComparison(first));
    }

    internal static bool PathStartsWith(string path, string prefix)
        => path.StartsWith(prefix, GetPathComparison(prefix));

    /// <summary>
    /// Rejects a path when any existing segment between the filesystem root and the path is a link.
    /// Durable output replacement uses this stricter boundary so a linked ancestor cannot alias protected source.
    /// </summary>
    internal static void EnsureNoLinksFromFileSystemRoot(string path, string error)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException(error);
        EnsureNoLinks(root!, fullPath, error);
    }

    /// <summary>
    /// Rejects links in every existing ancestor of a prospective path. This is used before creating
    /// project-owned output, lock, restore, package, and installation paths whose leaf may not exist yet.
    /// </summary>
    internal static void EnsureNoLinksInExistingAncestors(string path, string error)
    {
        var current = Path.GetFullPath(path);
        while (!File.Exists(current) && !Directory.Exists(current))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || PathEquals(parent, current))
                throw new InvalidOperationException(error);
            current = parent;
        }
        EnsureNoLinksFromFileSystemRoot(current, error);
    }

    internal static void EnsureNoLinks(string root, string path, string error)
    {
        var relativePath = FrameworkCompatibility.GetRelativePath(root, path);
        var current = Path.GetFullPath(root);
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException(error);
        foreach (var segment in relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(error);
        }
    }

    internal static StringComparison GetPathComparison(string path)
    {
        if (IsWindows)
            return StringComparison.OrdinalIgnoreCase;
        if (!IsMacOS)
            return StringComparison.Ordinal;
        return GetPathComparison(IsWindows, IsMacOS, IsCaseSensitiveMacVolume(path));
    }

    internal static StringComparison GetPathComparison(bool isWindows, bool isMacOS, bool isCaseSensitiveFileSystem)
        => isWindows || isMacOS && !isCaseSensitiveFileSystem
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static bool IsCaseSensitiveMacVolume(string path)
    {
        var directory = FindExistingDirectory(path);
        return MacCaseSensitivityByDirectory.GetOrAdd(directory, ProbeDirectoryCaseSensitivity);
    }

    private static string FindExistingDirectory(string path)
    {
        var current = Path.GetFullPath(path);
        if (File.Exists(current))
            current = Path.GetDirectoryName(current) ?? current;
        while (!Directory.Exists(current))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || parent.Equals(current, StringComparison.Ordinal))
                return Path.GetPathRoot(current) ?? current;
            current = parent;
        }
        return current;
    }

    private static bool ProbeDirectoryCaseSensitivity(string directory)
    {
        try
        {
            var current = directory;
            while (true)
            {
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrWhiteSpace(parent) || parent.Equals(current, StringComparison.Ordinal))
                    return true;
                var name = Path.GetFileName(current);
                var alternateName = ToggleCase(name);
                if (alternateName is not null)
                {
                    var alternate = Path.Combine(parent, alternateName);
                    if (!Directory.Exists(alternate))
                        return true;
                    var matchingNames = Directory.EnumerateDirectories(parent)
                        .Select(Path.GetFileName)
                        .Count(candidate => candidate is not null && candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
                    return matchingNames != 1;
                }
                current = parent;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return true;
        }
    }

    private static string? ToggleCase(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var replacement = char.IsUpper(value[index])
                ? char.ToLowerInvariant(value[index])
                : char.ToUpperInvariant(value[index]);
            if (replacement == value[index])
                continue;
            var characters = value.ToCharArray();
            characters[index] = replacement;
            return new string(characters);
        }
        return null;
    }

    internal sealed class FileSystemPathComparer : IEqualityComparer<string>, IComparer<string>
    {
        public bool Equals(string? first, string? second) => PathEquals(first, second);

        public int GetHashCode(string value)
            => GetPathComparison(value) == StringComparison.OrdinalIgnoreCase
                ? StringComparer.OrdinalIgnoreCase.GetHashCode(value)
                : StringComparer.Ordinal.GetHashCode(value);

        public int Compare(string? first, string? second)
        {
            if (ReferenceEquals(first, second))
                return 0;
            if (first is null)
                return -1;
            if (second is null)
                return 1;
            return string.Compare(first, second, GetPathComparison(first));
        }
    }
}
