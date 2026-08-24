using System.Runtime.InteropServices;

namespace PowerForge;

internal static class PowerShellCompilationPathSafety
{
    internal static StringComparison PathComparison { get; } = GetPathComparison(
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX));

    internal static StringComparer PathComparer { get; } =
        PathComparison == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    internal static void EnsureContained(string root, string path, string error)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(normalizedRoot, PathComparison))
            throw new InvalidOperationException(error);
    }

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

    internal static StringComparison GetPathComparison(bool isWindows, bool isMacOS)
        => isWindows || isMacOS ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
