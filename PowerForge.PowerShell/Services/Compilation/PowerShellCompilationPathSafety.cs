using System.Runtime.InteropServices;

namespace PowerForge;

internal static class PowerShellCompilationPathSafety
{
    internal static void EnsureContained(string root, string path, string error)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(normalizedRoot, GetPathComparison()))
            throw new InvalidOperationException(error);
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

    private static StringComparison GetPathComparison()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
